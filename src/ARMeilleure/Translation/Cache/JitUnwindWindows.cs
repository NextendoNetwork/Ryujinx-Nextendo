// https://github.com/MicrosoftDocs/cpp-docs/blob/master/docs/build/exception-handling-x64.md

using ARMeilleure.CodeGen.Unwinding;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ARMeilleure.Translation.Cache
{
    static partial class JitUnwindWindows
    {
        private const int MaxUnwindCodesArraySize = 32; // Must be an even value.

        private struct RuntimeFunction
        {
            public uint BeginAddress;
            public uint EndAddress;
            public uint UnwindData;
        }

        private struct UnwindInfo
        {
            public byte VersionAndFlags;
            public byte SizeOfProlog;
            public byte CountOfUnwindCodes;
            public byte FrameRegister;
            public unsafe fixed ushort UnwindCodes[MaxUnwindCodesArraySize];
        }
        
        private unsafe struct InternalFunctionHandler
        {
            public InternalFunctionHandler(JitCache jitCache, nint workBufferPtr)
            {
                _jitCache = jitCache;
                
                _runtimeFunction = (RuntimeFunction*)workBufferPtr;

                _unwindInfo = (UnwindInfo*)(workBufferPtr + _sizeOfRuntimeFunction);
            }

            readonly JitCache _jitCache;

            readonly RuntimeFunction* _runtimeFunction;

            readonly UnwindInfo* _unwindInfo;
            
            public RuntimeFunction* FunctionTableHandler(ulong controlPc, nint context)
            {
                return JitUnwindWindows.FunctionTableHandler(_jitCache, _runtimeFunction, _unwindInfo,  controlPc,  context);
            }
        }

        private enum UnwindOp
        {
            PushNonvol = 0,
            AllocLarge = 1,
            AllocSmall = 2,
            SetFpreg = 3,
            SaveNonvol = 4,
            SaveNonvolFar = 5,
            SaveXmm128 = 8,
            SaveXmm128Far = 9,
            PushMachframe = 10,
        }

        private unsafe delegate RuntimeFunction* GetRuntimeFunctionCallback(ulong controlPc, nint context);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe partial bool RtlInstallFunctionTableCallback(
            ulong tableIdentifier,
            ulong baseAddress,
            uint length,
            GetRuntimeFunctionCallback callback,
            nint context,
            [MarshalAs(UnmanagedType.LPWStr)] string outOfProcessCallbackDll);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe partial bool RtlDeleteFunctionTable(
            ulong tableIdentifier);

        private static GetRuntimeFunctionCallback _getRuntimeFunctionCallback;

        private static readonly int _sizeOfRuntimeFunction;
        
        private static readonly ConcurrentDictionary<ulong, InternalFunctionHandler> _functionTableHandlers = new();

        static JitUnwindWindows()
        {
            _sizeOfRuntimeFunction = Marshal.SizeOf<RuntimeFunction>();
        }

        public static void InstallFunctionTableHandler(JitCache jitCache, nint codeCachePointer, uint codeCacheLength, nint workBufferPtr)
        {
            ulong codeCachePtr = (ulong)codeCachePointer.ToInt64();

            bool result;

            InternalFunctionHandler handler;

            unsafe
            {
                handler =  new InternalFunctionHandler(jitCache, workBufferPtr);

                _getRuntimeFunctionCallback = handler.FunctionTableHandler;

                result = RtlInstallFunctionTableCallback(
                    codeCachePtr | 3,
                    codeCachePtr,
                    codeCacheLength,
                    _getRuntimeFunctionCallback,
                    codeCachePointer,
                    null);
            }

            if (!result)
            {
                throw new InvalidOperationException("Failure installing function table callback.");
            }

            _functionTableHandlers.TryAdd(codeCachePtr, handler);
        }

        public static void RemoveFunctionTableHandler(nint codeCachePointer)
        {
            ulong codeCachePtr = (ulong)codeCachePointer.ToInt64();

            bool result;

            unsafe
            {
                result = RtlDeleteFunctionTable(codeCachePtr | 3);
            }

            if (!result)
            {
                throw new InvalidOperationException("Failure removing function table callback.");
            }
            
            _functionTableHandlers.Remove(codeCachePtr, out _);
        }

        private static unsafe RuntimeFunction* FunctionTableHandler(JitCache jitCache, RuntimeFunction* runtimeFunction, UnwindInfo* unwindInfo, ulong controlPc, nint context)
        {
            int offset = (int)((long)controlPc - context.ToInt64());

            if (!jitCache.TryFind(offset, out CacheEntry funcEntry, out _))
            {
                return null; // Not found.
            }

            CodeGen.Unwinding.UnwindInfo funcUnwindInfo = funcEntry.UnwindInfo;

            int codeIndex = 0;

            for (int index = funcUnwindInfo.PushEntries.Length - 1; index >= 0; index--)
            {
                UnwindPushEntry entry = funcUnwindInfo.PushEntries[index];

                switch (entry.PseudoOp)
                {
                    case UnwindPseudoOp.SaveXmm128:
                        {
                            int stackOffset = entry.StackOffsetOrAllocSize;

                            Debug.Assert(stackOffset % 16 == 0);

                            if (stackOffset <= 0xFFFF0)
                            {
                                unwindInfo->UnwindCodes[codeIndex++] = PackUnwindOp(UnwindOp.SaveXmm128, entry.PrologOffset, entry.RegIndex);
                                unwindInfo->UnwindCodes[codeIndex++] = (ushort)(stackOffset / 16);
                            }
                            else
                            {
                                unwindInfo->UnwindCodes[codeIndex++] = PackUnwindOp(UnwindOp.SaveXmm128Far, entry.PrologOffset, entry.RegIndex);
                                unwindInfo->UnwindCodes[codeIndex++] = (ushort)(stackOffset >> 0);
                                unwindInfo->UnwindCodes[codeIndex++] = (ushort)(stackOffset >> 16);
                            }

                            break;
                        }

                    case UnwindPseudoOp.AllocStack:
                        {
                            int allocSize = entry.StackOffsetOrAllocSize;

                            Debug.Assert(allocSize % 8 == 0);

                            if (allocSize <= 128)
                            {
                                unwindInfo->UnwindCodes[codeIndex++] = PackUnwindOp(UnwindOp.AllocSmall, entry.PrologOffset, (allocSize / 8) - 1);
                            }
                            else if (allocSize <= 0x7FFF8)
                            {
                                unwindInfo->UnwindCodes[codeIndex++] = PackUnwindOp(UnwindOp.AllocLarge, entry.PrologOffset, 0);
                                unwindInfo->UnwindCodes[codeIndex++] = (ushort)(allocSize / 8);
                            }
                            else
                            {
                                unwindInfo->UnwindCodes[codeIndex++] = PackUnwindOp(UnwindOp.AllocLarge, entry.PrologOffset, 1);
                                unwindInfo->UnwindCodes[codeIndex++] = (ushort)(allocSize >> 0);
                                unwindInfo->UnwindCodes[codeIndex++] = (ushort)(allocSize >> 16);
                            }

                            break;
                        }

                    case UnwindPseudoOp.PushReg:
                        {
                            unwindInfo->UnwindCodes[codeIndex++] = PackUnwindOp(UnwindOp.PushNonvol, entry.PrologOffset, entry.RegIndex);

                            break;
                        }

                    default:
                        throw new NotImplementedException($"({nameof(entry.PseudoOp)} = {entry.PseudoOp})");
                }
            }

            Debug.Assert(codeIndex <= MaxUnwindCodesArraySize);

            unwindInfo->VersionAndFlags = 1; // Flags: The function has no handler.
            unwindInfo->SizeOfProlog = (byte)funcUnwindInfo.PrologSize;
            unwindInfo->CountOfUnwindCodes = (byte)codeIndex;
            unwindInfo->FrameRegister = 0;

            runtimeFunction->BeginAddress = (uint)funcEntry.Offset;
            runtimeFunction->EndAddress = (uint)(funcEntry.Offset + funcEntry.Size);
            runtimeFunction->UnwindData = (uint)_sizeOfRuntimeFunction;

            return runtimeFunction;
        }

        private static ushort PackUnwindOp(UnwindOp op, int prologOffset, int opInfo)
        {
            return (ushort)(prologOffset | ((int)op << 8) | (opInfo << 12));
        }
    }
}
