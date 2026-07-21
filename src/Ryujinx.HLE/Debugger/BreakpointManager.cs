using Ryujinx.Common.Logging;
using Ryujinx.Memory;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Ryujinx.HLE.Debugger
{
    internal class Breakpoint
    {
        public byte[] OriginalData { get; }

        public Breakpoint(byte[] originalData)
        {
            OriginalData = originalData;
        }
    }

    /// <summary>
    /// Manages software breakpoints for the debugger.
    /// </summary>
    public class BreakpointManager
    {
        private readonly Debugger _debugger;
        private readonly ConcurrentDictionary<ulong, Breakpoint> _breakpoints = new();

        private static readonly byte[] _aarch64BreakInstruction = [0x00, 0x00, 0x20, 0xD4]; // BRK #0
        private static readonly byte[] _aarch32BreakInstruction = [0xFE, 0xDE, 0xFF, 0xE7]; // TRAP
        private static readonly byte[] _aarch32ThumbBreakInstruction = [0x80, 0xB6];

        public BreakpointManager(Debugger debugger)
        {
            _debugger = debugger;
        }

        /// <summary>
        /// Sets a software breakpoint at a specified address.
        /// </summary>
        /// <param name="address">The memory address to set the breakpoint at.</param>
        /// <param name="length">The length of the instruction to replace.</param>
        /// <param name="isStep">Indicates if this is a single-step breakpoint.</param>
        /// <returns>True if the breakpoint was set successfully; otherwise, false.</returns>
        public bool SetBreakPoint(ulong address, ulong length)
        {
            if (_breakpoints.ContainsKey(address))
            {
                return false;
            }

            byte[] breakInstruction = GetBreakInstruction(length);
            if (breakInstruction == null)
            {
                Logger.Error?.Print(LogClass.GdbStub, $"Unsupported instruction length for breakpoint: {length}");
                return false;
            }

            byte[] originalInstruction = new byte[length];
            if (!ReadMemory(address, originalInstruction))
            {
                Logger.Error?.Print(LogClass.GdbStub, $"Failed to read memory at 0x{address:X16} to set breakpoint.");
                return false;
            }

            if (!WriteMemory(address, breakInstruction))
            {
                Logger.Error?.Print(LogClass.GdbStub, $"Failed to write breakpoint at 0x{address:X16}.");
                return false;
            }

            Breakpoint breakpoint = new(originalInstruction);
            if (_breakpoints.TryAdd(address, breakpoint))
            {
                Logger.Debug?.Print(LogClass.GdbStub, $"Breakpoint set at 0x{address:X16}");
                return true;
            }

            Logger.Error?.Print(LogClass.GdbStub, $"Failed to add breakpoint at 0x{address:X16}.");
            return false;
        }

        /// <summary>
        /// Clears a software breakpoint at a specified address.
        /// </summary>
        /// <param name="address">The memory address of the breakpoint to clear.</param>
        /// <param name="length">The length of the instruction (unused).</param>
        /// <returns>True if the breakpoint was cleared successfully; otherwise, false.</returns>
        public bool ClearBreakPoint(ulong address, ulong length)
        {
            if (_breakpoints.TryGetValue(address, out Breakpoint breakpoint))
            {
                if (!WriteMemory(address, breakpoint.OriginalData))
                {
                    Logger.Error?.Print(LogClass.GdbStub, $"Failed to restore original instruction at 0x{address:X16} to clear breakpoint.");
                    return false;
                }

                _breakpoints.TryRemove(address, out _);
                Logger.Debug?.Print(LogClass.GdbStub, $"Breakpoint cleared at 0x{address:X16}");
                return true;
            }

            Logger.Warning?.Print(LogClass.GdbStub, $"No breakpoint found at address 0x{address:X16}");
            return false;
        }

        /// <summary>
        /// Clears all currently set software breakpoints.
        /// </summary>
        public void ClearAll()
        {
            foreach (KeyValuePair<ulong, Breakpoint> bp in _breakpoints)
            {
                if (!WriteMemory(bp.Key, bp.Value.OriginalData))
                {
                    Logger.Error?.Print(LogClass.GdbStub, $"Failed to restore original instruction at 0x{bp.Key:X16} while clearing all breakpoints.");
                }
                
            }
            _breakpoints.Clear();
            Logger.Debug?.Print(LogClass.GdbStub, "All breakpoints cleared.");
        }

        private byte[] GetBreakInstruction(ulong length)
        {
            if (_debugger.IsProcess32Bit)
            {
                if (length == 2)
                {
                    return _aarch32ThumbBreakInstruction;
                }
                
                if (length == 4)
                {
                     return _aarch32BreakInstruction;
                }
            }
            else
            {
                if (length == 4)
                {
                    return _aarch64BreakInstruction;
                }
            }

            return null;
        }

        private bool ReadMemory(ulong address, byte[] data)
        {
            try
            {
                _debugger.DebugProcess.CpuMemory.Read(address, data);
                return true;
            }
            catch (InvalidMemoryRegionException)
            {
                return false;
            }
        }

        private bool WriteMemory(ulong address, byte[] data)
        {
            try
            {
                _debugger.DebugProcess.CpuMemory.Write(address, data);
                _debugger.DebugProcess.InvalidateCacheRegion(address, (ulong)data.Length);
                return true;
            }
            catch (InvalidMemoryRegionException)
            {
                return false;
            }
        }
    }
}
