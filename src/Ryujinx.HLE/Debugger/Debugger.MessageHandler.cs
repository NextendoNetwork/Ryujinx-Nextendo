using Ryujinx.Common.Logging;
using System;
using System.IO;

namespace Ryujinx.HLE.Debugger
{
    public partial class Debugger
    {
        private void MessageHandlerMain()
        {
            while (!_shuttingDown)
            {
                try
                {
                    switch (_messages.Take())
                    {
                        case Message { Type: MessageType.BreakIn }:
                            Logger.Notice.Print(LogClass.GdbStub, "Break-in requested");
                            _commands.Interrupt();
                            break;

                        case Message { Type: MessageType.SendNack }:
                            _writeStream.WriteByte((byte)'-');
                            break;

                        case Message { Type: MessageType.Kill }:
                            return;

                        case CommandMessage { Command: { } cmd }:
                            Logger.Debug?.Print(LogClass.GdbStub, $"Received Command: {cmd}");
                            _writeStream.WriteByte((byte)'+');
                            _commands.Processor.Process(cmd);
                            break;

                        case ThreadBreakMessage { Context: { } ctx }:
                            DebugProcess.DebugStop();
                            GThreadId = CThreadId = ctx.ThreadUid;
                            _breakHandlerEvent.Set();
                            _commands.Processor.Reply($"T05thread:{ctx.ThreadUid:x};");
                            break;
                    }
                }
                catch (IOException e)
                {
                    Logger.Error?.Print(LogClass.GdbStub, "Error while processing GDB messages", e);
                }
                catch (NullReferenceException e)
                {
                    Logger.Error?.Print(LogClass.GdbStub, "Error while processing GDB messages", e);
                }
                catch (ObjectDisposedException e)
                {
                    Logger.Error?.Print(LogClass.GdbStub, "Error while processing GDB messages", e);
                }
            }
        }
    }
}
