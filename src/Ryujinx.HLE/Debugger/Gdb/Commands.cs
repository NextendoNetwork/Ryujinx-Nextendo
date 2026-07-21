using Ryujinx.Common.Logging;
using Ryujinx.Cpu;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;

namespace Ryujinx.HLE.Debugger.Gdb
{
    class GdbCommands
    {
        public readonly Debugger Debugger;

        private GdbCommandProcessor _processor;

        public GdbCommandProcessor Processor
            => _processor ??= new GdbCommandProcessor(this);

        internal readonly TcpListener ListenerSocket;
        internal readonly Socket ClientSocket;
        internal readonly NetworkStream ReadStream;
        internal readonly NetworkStream WriteStream;

        public GdbCommands(TcpListener listenerSocket, Socket clientSocket, NetworkStream readStream,
            NetworkStream writeStream, Debugger debugger)
        {
            ListenerSocket = listenerSocket;
            ClientSocket = clientSocket;
            ReadStream = readStream;
            WriteStream = writeStream;
            Debugger = debugger;
        }

        internal void Query()
        {
            // GDB is performing initial contact. Stop everything.
            Debugger.DebugProcess.DebugStop();
            Debugger.GThreadId = Debugger.CThreadId = Debugger.DebugProcess.ThreadUids.First();
            Processor.Reply($"T05thread:{Debugger.CThreadId:x};");
        }

        internal void Interrupt()
        {
            // GDB is requesting an interrupt. Stop everything.
            Debugger.DebugProcess.DebugStop();
            if (Debugger.GThreadId == null || Debugger.GetThreads().All(x => x.ThreadUid != Debugger.GThreadId.Value))
            {
                Debugger.GThreadId = Debugger.CThreadId = Debugger.DebugProcess.ThreadUids.First();
            }

            Processor.Reply($"T02thread:{Debugger.GThreadId:x};");
        }

        internal void Continue(ulong? newPc)
        {
            if (newPc.HasValue)
            {
                if (Debugger.CThreadId == null)
                {
                    Processor.ReplyError();
                    return;
                }

                Debugger.CThread.Context.DebugPc = newPc.Value;
            }

            Debugger.DebugProcess.DebugContinue();
            Processor.ReplyOK();
        }

        internal void Detach()
        {
            Debugger.BreakpointManager.ClearAll();
            Continue(null); // Continue() will call ReplyError/ReplyOK for us.
        }

        internal void ReadRegisters()
        {
            if (Debugger.GThreadId == null)
            {
                Processor.ReplyError();
                return;
            }

            IExecutionContext ctx = Debugger.GThread.Context;
            string registers = string.Empty;
            if (Debugger.IsProcess32Bit)
            {
                for (int i = 0; i < GdbRegisters.Count32; i++)
                {
                    registers += ctx.ReadRegister32(i);
                }
            }
            else
            {
                for (int i = 0; i < GdbRegisters.Count64; i++)
                {
                    registers += ctx.ReadRegister64(i);
                }
            }

            Processor.Reply(registers);
        }

        internal void WriteRegisters(StringStream ss)
        {
            if (Debugger.GThreadId == null)
            {
                Processor.ReplyError();
                return;
            }

            IExecutionContext ctx = Debugger.GThread.Context;
            if (Debugger.IsProcess32Bit)
            {
                for (int i = 0; i < GdbRegisters.Count32; i++)
                {
                    if (!ctx.WriteRegister32(i, ss))
                    {
                        Processor.ReplyError();
                        return;
                    }
                }
            }
            else
            {
                for (int i = 0; i < GdbRegisters.Count64; i++)
                {
                    if (!ctx.WriteRegister64(i, ss))
                    {
                        Processor.ReplyError();
                        return;
                    }
                }
            }

            Processor.Reply(ss.IsEmpty);
        }

        internal void SetThread(char op, ulong? threadId)
        {
            if (threadId is 0 or null)
            {
                KThread[] threads = Debugger.GetThreads();
                if (threads.Length == 0)
                {
                    Processor.ReplyError();
                    return;
                }

                threadId = threads.First().ThreadUid;
            }

            if (Debugger.DebugProcess.GetThread(threadId.Value) == null)
            {
                Processor.ReplyError();
                return;
            }

            switch (op)
            {
                case 'c':
                    Debugger.CThreadId = threadId;
                    Processor.ReplyOK();
                    return;
                case 'g':
                    Debugger.GThreadId = threadId;
                    Processor.ReplyOK();
                    return;
                default:
                    Processor.ReplyError();
                    return;
            }
        }

        internal void ReadMemory(ulong addr, ulong len)
        {
            try
            {
                byte[] data = new byte[len];
                Debugger.DebugProcess.CpuMemory.Read(addr, data);
                Processor.ReplyHex(data);
            }
            catch (InvalidMemoryRegionException)
            {
                // InvalidAccessHandler will show an error message, we log it again to tell user the error is from GDB (which can be ignored)
                // TODO: Do not let InvalidAccessHandler show the error message
                Logger.Notice.Print(LogClass.GdbStub, $"GDB failed to read memory at 0x{addr:X16}");
                Processor.ReplyError();
            }
        }

        internal void WriteMemory(ulong addr, ulong len, StringStream ss)
        {
            try
            {
                byte[] data = new byte[len];
                for (ulong i = 0; i < len; i++)
                {
                    data[i] = (byte)ss.ReadLengthAsHex(2);
                }

                Debugger.DebugProcess.CpuMemory.Write(addr, data);
                Debugger.DebugProcess.InvalidateCacheRegion(addr, len);
                Processor.ReplyOK();
            }
            catch (InvalidMemoryRegionException)
            {
                Processor.ReplyError();
            }
        }

        internal void ReadRegister(int gdbRegId)
        {
            if (Debugger.GThreadId == null)
            {
                Processor.ReplyError();
                return;
            }

            IExecutionContext ctx = Debugger.GThread.Context;
            string result = Debugger.ReadRegister(ctx, gdbRegId);

            Processor.Reply(result != null, result);
        }

        internal void WriteRegister(int gdbRegId, StringStream ss)
        {
            if (Debugger.GThreadId == null)
            {
                Processor.ReplyError();
                return;
            }

            Processor.Reply(
                success: Debugger.WriteRegister(Debugger.GThread.Context, gdbRegId, ss) && ss.IsEmpty
            );
        }

        internal void Step(ulong? newPc)
        {
            if (Debugger.CThreadId == null)
            {
                Processor.ReplyError();
                return;
            }

            KThread thread = Debugger.CThread;

            if (newPc.HasValue)
            {
                thread.Context.DebugPc = newPc.Value;
            }

            if (!Debugger.DebugProcess.DebugStep(thread))
            {
                Processor.ReplyError();
            }
            else
            {
                Debugger.GThreadId = Debugger.CThreadId = thread.ThreadUid;
                Processor.Reply($"T05thread:{thread.ThreadUid:x};");
            }
        }

        internal void IsAlive(ulong? threadId)
        {
            if (Debugger.GetThreads().Any(x => x.ThreadUid == threadId))
            {
                Processor.ReplyOK();
            }
            else
            {
                Processor.Reply("E00");
            }
        }

        enum VContAction
        {
            None,
            Continue,
            Stop,
            Step
        }

        record VContPendingAction(VContAction Action/*, ushort? Signal = null*/);

        internal void VCont(StringStream ss)
        {
            string[] rawActions = ss.ReadRemaining().Split(';', StringSplitOptions.RemoveEmptyEntries);

            Dictionary<ulong, VContPendingAction> threadActionMap = new();
            foreach (KThread thread in Debugger.GetThreads())
            {
                threadActionMap[thread.ThreadUid] = new VContPendingAction(VContAction.None);
            }

            VContAction defaultAction = VContAction.None;

            // For each inferior thread, the *leftmost* action with a matching thread-id is applied.
            for (int i = rawActions.Length - 1; i >= 0; i--)
            {
                string rawAction = rawActions[i];
                StringStream stream = new(rawAction);

                char cmd = stream.ReadChar();
                VContAction action = cmd switch
                {
                    'c' or 'C' => VContAction.Continue,
                    's' or 'S' => VContAction.Step,
                    't' => VContAction.Stop,
                    _ => VContAction.None
                };

                // TODO: Note: We don't support signals yet.
                //ushort? signal = null;
                if (cmd is 'C' or 'S')
                {
                    /*signal = (ushort)*/stream.ReadLengthAsHex(2);
                    // we still call the read length method even if we have signals commented
                    // since that method advances the underlying string position
                }

                ulong? threadId = stream.ConsumePrefix(":") 
                    ? stream.ReadRemainingAsThreadUid()
                    : null;

                if (threadId.HasValue)
                {
                    if (threadActionMap.ContainsKey(threadId.Value))
                    {
                        threadActionMap[threadId.Value] = new VContPendingAction(action /*, signal*/);
                    }
                }
                else
                {
                    foreach (ulong thread in threadActionMap.Keys)
                    {
                        threadActionMap[thread] = new VContPendingAction(action /*, signal*/);
                    }

                    if (action == VContAction.Continue)
                    {
                        defaultAction = action;
                    }
                    else
                    {
                        Logger.Warning?.Print(LogClass.GdbStub,
                            $"Received vCont command with unsupported default action: {rawAction}");
                    }
                }
            }

            bool hasError = false;

            foreach ((ulong threadUid, VContPendingAction action) in threadActionMap)
            {
                if (action.Action == VContAction.Step)
                {
                    KThread thread = Debugger.DebugProcess.GetThread(threadUid);
                    if (!Debugger.DebugProcess.DebugStep(thread))
                    {
                        hasError = true;
                    }
                }
            }

            // If we receive "vCont;c", just continue the process.
            // If we receive something like "vCont;c:2e;c:2f" (IDA Pro will send commands like this), continue these threads.
            // For "vCont;s:2f;c", `DebugProcess.DebugStep()` will continue and suspend other threads if needed, so we don't do anything here.
            if (threadActionMap.Values.All(a => a.Action == VContAction.Continue))
            {
                Debugger.DebugProcess.DebugContinue();
            }
            else if (defaultAction == VContAction.None)
            {
                foreach ((ulong threadUid, VContPendingAction action) in threadActionMap)
                {
                    if (action.Action == VContAction.Continue)
                    {
                        Debugger.DebugProcess.DebugContinue(Debugger.DebugProcess.GetThread(threadUid));
                    }
                }
            }

            Processor.Reply(!hasError);

            foreach ((ulong threadUid, VContPendingAction action) in threadActionMap)
            {
                if (action.Action == VContAction.Step)
                {
                    Debugger.GThreadId = Debugger.CThreadId = threadUid;
                    Processor.Reply($"T05thread:{threadUid:x};");
                }
            }
        }

        internal void Q_Rcmd(string hexCommand)
        {
            try
            {
                string command = Helpers.FromHex(hexCommand);
                Logger.Debug?.Print(LogClass.GdbStub, $"Received Rcmd: {command}");

                string response = Debugger.CallRcmdDelegate(Debugger, command);
                Processor.ReplyHex(response);
            }
            catch (Exception e)
            {
                Logger.Error?.Print(LogClass.GdbStub, $"Error processing Rcmd: {e.Message}");
                Processor.ReplyError();
            }
        }
    }
}
