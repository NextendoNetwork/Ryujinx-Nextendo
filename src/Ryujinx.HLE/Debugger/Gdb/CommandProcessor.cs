using Gommon;
using Ryujinx.Common;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Kernel.Threading;
using System.Linq;
using System.Text;

namespace Ryujinx.HLE.Debugger.Gdb
{
    class GdbCommandProcessor
    {
        public readonly GdbCommands Commands;

        private Debugger Debugger => Commands.Debugger;
        private BreakpointManager BreakpointManager => Commands.Debugger.BreakpointManager;
        private IDebuggableProcess DebugProcess => Commands.Debugger.DebugProcess;

        public GdbCommandProcessor(GdbCommands commands)
        {
            Commands = commands;
        }

        public void ReplyHex(string data) => Reply(Helpers.ToHex(data));
        public void ReplyHex(byte[] data) => Reply(Helpers.ToHex(data));

        public void Reply(string cmd)
        {
            Logger.Debug?.Print(LogClass.GdbStub, $"Reply: {cmd}");
            Commands.WriteStream.Write(Encoding.ASCII.GetBytes($"${cmd}#{Helpers.CalculateChecksum(cmd):x2}"));
        }

        public void ReplyOK() => Reply("OK");

        public void ReplyError() => Reply("E01");

        public void Reply(bool success)
        {
            if (success)
                ReplyOK();
            else ReplyError();
        }

        public void Reply(bool success, string cmd)
        {
            if (success)
                Reply(cmd);
            else ReplyError();
        }

        private string _previousThreadListXml = string.Empty;

        public void Process(string cmd)
        {
            StringStream ss = new(cmd);

            switch (ss.ReadChar())
            {
                case '!':
                    if (!ss.IsEmpty)
                    {
                        goto unknownCommand;
                    }

                    // Enable extended mode
                    ReplyOK();
                    break;
                case '?':
                    if (!ss.IsEmpty)
                    {
                        goto unknownCommand;
                    }

                    Commands.Query();
                    break;
                case 'c':
                    Commands.Continue(ss.IsEmpty ? null : ss.ReadRemainingAsHex());
                    break;
                case 'D':
                    if (!ss.IsEmpty)
                    {
                        goto unknownCommand;
                    }

                    Commands.Detach();
                    break;
                case 'g':
                    if (!ss.IsEmpty)
                    {
                        goto unknownCommand;
                    }

                    Commands.ReadRegisters();
                    break;
                case 'G':
                    Commands.WriteRegisters(ss);
                    break;
                case 'H':
                    {
                        char op = ss.ReadChar();
                        ulong? threadId = ss.ReadRemainingAsThreadUid();
                        Commands.SetThread(op, threadId);
                        break;
                    }
                case 'k':
                    Logger.Notice.Print(LogClass.GdbStub, "Kill request received, detach instead");
                    Reply(string.Empty);
                    Commands.Detach();
                    break;
                case 'm':
                    {
                        ulong addr = ss.ReadUntilAsHex(',');
                        ulong len = ss.ReadRemainingAsHex();
                        Commands.ReadMemory(addr, len);
                        break;
                    }
                case 'M':
                    {
                        ulong addr = ss.ReadUntilAsHex(',');
                        ulong len = ss.ReadUntilAsHex(':');
                        Commands.WriteMemory(addr, len, ss);
                        break;
                    }
                case 'p':
                    {
                        ulong gdbRegId = ss.ReadRemainingAsHex();
                        Commands.ReadRegister((int)gdbRegId);
                        break;
                    }
                case 'P':
                    {
                        ulong gdbRegId = ss.ReadUntilAsHex('=');
                        Commands.WriteRegister((int)gdbRegId, ss);
                        break;
                    }
                case 'q':
                    if (ss.ConsumeRemaining("GDBServerVersion"))
                    {
                        Reply($"name:Ryujinx;version:{ReleaseInformation.Version};");
                        break;
                    }

                    if (ss.ConsumeRemaining("HostInfo"))
                    {
                        Reply(
                            Debugger.IsProcess32Bit
                                ? $"triple:{Helpers.ToHex("arm-unknown-linux-android")};endian:little;ptrsize:4;hostname:{Helpers.ToHex("Ryujinx")};"
                                : $"triple:{Helpers.ToHex("aarch64-unknown-linux-android")};endian:little;ptrsize:8;hostname:{Helpers.ToHex("Ryujinx")};");

                        break;
                    }

                    if (ss.ConsumeRemaining("Attached"))
                    {
                        Reply("1");
                        break;
                    }

                    if (ss.ConsumeRemaining("ProcessInfo"))
                    {
                        Reply(
                            Debugger.IsProcess32Bit
                                ? $"pid:1;cputype:12;cpusubtype:0;triple:{Helpers.ToHex("arm-unknown-linux-android")};ostype:unknown;vendor:none;endian:little;ptrsize:4;"
                                : $"pid:1;cputype:100000c;cpusubtype:0;triple:{Helpers.ToHex("aarch64-unknown-linux-android")};ostype:unknown;vendor:none;endian:little;ptrsize:8;");

                        break;
                    }

                    if (ss.ConsumePrefix("Supported:") || ss.ConsumeRemaining("Supported"))
                    {
                        Reply("PacketSize=10000;qXfer:features:read+;qXfer:threads:read+;vContSupported+");
                        break;
                    }

                    if (ss.ConsumePrefix("Rcmd,"))
                    {
                        string hexCommand = ss.ReadRemaining();
                        Commands.Q_Rcmd(hexCommand);
                        break;
                    }

                    if (ss.ConsumeRemaining("fThreadInfo"))
                    {
                        Reply(
                            $"m{DebugProcess.ThreadUids.Select(x => $"{x:x}").JoinToString(",")}");
                        break;
                    }

                    if (ss.ConsumeRemaining("sThreadInfo"))
                    {
                        Reply("l");
                        break;
                    }

                    if (ss.ConsumePrefix("ThreadExtraInfo,"))
                    {
                        ulong? threadId = ss.ReadRemainingAsThreadUid();
                        if (threadId == null)
                        {
                            ReplyError();
                            break;
                        }

                        ReplyHex(
                            DebugProcess.IsThreadPaused(DebugProcess.GetThread(threadId.Value))
                                ? "Paused"
                                : "Running"
                        );

                        break;
                    }

                    if (ss.ConsumePrefix("Xfer:threads:read:"))
                    {
                        ss.ReadUntil(':');
                        ulong offset = ss.ReadUntilAsHex(',');
                        ulong len = ss.ReadRemainingAsHex();

                        string data;
                        if (offset > 0)
                        {
                            data = _previousThreadListXml;
                        }
                        else
                        {
                            _previousThreadListXml = data = GetThreadListXml();
                        }

                        if (offset >= (ulong)data.Length)
                        {
                            Reply("l");
                            break;
                        }

                        if (len >= (ulong)data.Length - offset)
                        {
                            Reply("l" + Helpers.ToBinaryFormat(data[(int)offset..]));
                        }
                        else
                        {
                            Reply("m" + Helpers.ToBinaryFormat(data.Substring((int)offset, (int)len)));
                        }

                        break;
                    }

                    if (ss.ConsumePrefix("Xfer:features:read:"))
                    {
                        string feature = ss.ReadUntil(':');
                        ulong offset = ss.ReadUntilAsHex(',');
                        ulong len = ss.ReadRemainingAsHex();

                        if (feature == "target.xml")
                        {
                            feature = Debugger.IsProcess32Bit ? "target32.xml" : "target64.xml";
                        }

                        if (!RegisterInformation.Features.TryGetValue(feature, out string data))
                        {
                            Reply("E00"); // Invalid annex
                            break;
                        }

                        if (offset >= (ulong)data.Length)
                        {
                            Reply("l");
                            break;
                        }

                        if (len >= (ulong)data.Length - offset)
                        {
                            Reply("l" + Helpers.ToBinaryFormat(data[(int)offset..]));
                        }
                        else
                        {
                            Reply("m" + Helpers.ToBinaryFormat(data.Substring((int)offset, (int)len)));
                        }

                        break;
                    }

                    goto unknownCommand;
                case 'Q':
                    goto unknownCommand;
                case 's':
                    Commands.Step(ss.IsEmpty ? null : ss.ReadRemainingAsHex());
                    break;
                case 'T':
                    {
                        ulong? threadId = ss.ReadRemainingAsThreadUid();
                        Commands.IsAlive(threadId);
                        break;
                    }
                case 'v':
                    if (ss.ConsumePrefix("Cont"))
                    {
                        if (ss.ConsumeRemaining("?"))
                        {
                            Reply("vCont;c;C;s;S");
                            break;
                        }

                        if (ss.ConsumePrefix(";"))
                        {
                            Commands.VCont(ss);
                            break;
                        }

                        goto unknownCommand;
                    }

                    if (ss.ConsumeRemaining("MustReplyEmpty"))
                    {
                        Reply(string.Empty);
                        break;
                    }

                    goto unknownCommand;
                case 'Z':
                    {
                        string type = ss.ReadUntil(',');
                        ulong addr = ss.ReadUntilAsHex(',');
                        ulong len = ss.ReadLengthAsHex(1);
                        string extra = ss.ReadRemaining();

                        if (extra.Length > 0)
                        {
                            Logger.Notice.Print(LogClass.GdbStub, $"Unsupported Z command extra data: {extra}");
                            ReplyError();
                            return;
                        }

                        switch (type)
                        {
                            case "0": // Software breakpoint
                                if (!BreakpointManager.SetBreakPoint(addr, len))
                                {
                                    ReplyError();
                                    return;
                                }

                                ReplyOK();
                                return;
                            // ReSharper disable RedundantCaseLabel
                            case "1": // Hardware breakpoint
                            case "2": // Write watchpoint
                            case "3": // Read watchpoint
                            case "4": // Access watchpoint
                            // ReSharper restore RedundantCaseLabel
                            default:
                                ReplyError();
                                return;
                        }
                    }
                case 'z':
                    {
                        string type = ss.ReadUntil(',');
                        ss.ConsumePrefix(",");
                        ulong addr = ss.ReadUntilAsHex(',');
                        ulong len = ss.ReadLengthAsHex(1);
                        string extra = ss.ReadRemaining();

                        if (extra.Length > 0)
                        {
                            Logger.Notice.Print(LogClass.GdbStub, $"Unsupported z command extra data: {extra}");
                            ReplyError();
                            return;
                        }

                        switch (type)
                        {
                            case "0": // Software breakpoint
                                if (!BreakpointManager.ClearBreakPoint(addr, len))
                                {
                                    ReplyError();
                                    return;
                                }

                                ReplyOK();
                                return;
                            // ReSharper disable RedundantCaseLabel
                            case "1": // Hardware breakpoint
                            case "2": // Write watchpoint
                            case "3": // Read watchpoint
                            case "4": // Access watchpoint
                            // ReSharper restore RedundantCaseLabel
                            default:
                                ReplyError();
                                return;
                        }
                    }
                default:
                    unknownCommand:
                    Logger.Notice.Print(LogClass.GdbStub, $"Unknown command: {cmd}");
                    Reply(string.Empty);
                    break;
            }
        }

        private string GetThreadListXml()
        {
            StringBuilder sb = new();
            sb.Append("<?xml version=\"1.0\"?><threads>\n");

            foreach (KThread thread in Debugger.GetThreads())
            {
                string threadName = System.Security.SecurityElement.Escape(thread.GetThreadName());
                sb.Append(
                    $"<thread id=\"{thread.ThreadUid:x}\" name=\"{threadName}\">{(DebugProcess.IsThreadPaused(thread) ? "Paused" : "Running")}</thread>\n");
            }

            sb.Append("</threads>");
            return sb.ToString();
        }
    }
}
