using Ryujinx.Common.Logging;
using Ryujinx.HLE.Debugger.Gdb;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Ryujinx.HLE.Debugger
{
    public partial class Debugger
    {
        private void MainLoop()
        {
            IPEndPoint endpoint = new(IPAddress.Any, GdbStubPort);
            _listenerSocket = new TcpListener(endpoint);
            
            try
            {
                _listenerSocket.Start();
            }
            catch (SocketException se)
            {
                Logger.Error?.Print(LogClass.GdbStub,
                    $"Failed to create TCP server on {endpoint} for GDB client: {Enum.GetName(se.SocketErrorCode)}");
                throw;
            }

            Logger.Notice.Print(LogClass.GdbStub, $"Currently waiting on {endpoint} for GDB client");

            while (!_shuttingDown)
            {
                try
                {
                    _clientSocket = _listenerSocket.AcceptSocket();
                }
                catch (SocketException se)
                {
                    Logger.Error?.Print(LogClass.GdbStub, 
                        $"Failed to accept incoming GDB client connection: {Enum.GetName(se.SocketErrorCode)}");
                    return;
                }

                // If the user connects before the application is running, wait for the application to start.
                int retries = 10;
                while ((DebugProcess == null || GetThreads().Length == 0) && retries-- > 0)
                {
                    Thread.Sleep(500);
                }

                if (DebugProcess == null || GetThreads().Length == 0)
                {
                    Logger.Warning?.Print(LogClass.GdbStub,
                        "Application is not running, cannot accept GDB client connection");
                    _clientSocket.Close();
                    continue;
                }

                _clientSocket.NoDelay = true;
                _readStream = new NetworkStream(_clientSocket, System.IO.FileAccess.Read);
                _writeStream = new NetworkStream(_clientSocket, System.IO.FileAccess.Write);
                _commands = new GdbCommands(_listenerSocket, _clientSocket, _readStream, _writeStream, this);

                Logger.Notice.Print(LogClass.GdbStub, "GDB client connected");

                while (true)
                {
                    try
                    {
                        switch (_readStream.ReadByte())
                        {
                            case -1:
                                goto EndOfLoop;
                            case '+':
                                continue;
                            case '-':
                                Logger.Notice.Print(LogClass.GdbStub, "NACK received!");
                                continue;
                            case '\x03':
                                _messages.Add(Message.BreakIn);
                                break;
                            case '$':
                                string cmd = string.Empty;
                                while (true)
                                {
                                    int x = _readStream.ReadByte();
                                    if (x == -1)
                                        goto EndOfLoop;
                                    if (x == '#')
                                        break;
                                    cmd += (char)x;
                                }

                                string checksum = $"{(char)_readStream.ReadByte()}{(char)_readStream.ReadByte()}";
                                if (checksum == $"{Helpers.CalculateChecksum(cmd):x2}")
                                {
                                    _messages.Add(new CommandMessage(cmd));
                                }
                                else
                                {
                                    _messages.Add(Message.SendNack);
                                }

                                break;
                        }
                    }
                    catch (IOException)
                    {
                        goto EndOfLoop;
                    }
                }

                EndOfLoop:
                Logger.Notice.Print(LogClass.GdbStub, "GDB client lost connection");
                _readStream.Close();
                _readStream = null;
                _writeStream.Close();
                _writeStream = null;
                _clientSocket.Close();
                _clientSocket = null;
                _commands = null;

                BreakpointManager.ClearAll();
            }
        }
    }
}
