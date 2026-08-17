using Ryujinx.Common.Logging;
using Ryujinx.Common.Utilities;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd.Proxy;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay.Proxy
{
    /// <summary>
    /// Socket of the emulated console while LAN Play is active.
    /// <para>
    /// Traffic for the LAN Play network (10.13.0.0/16 and broadcasts) is carried by the virtual network
    /// interface, so it reaches the relay as IPv4 packets exactly like a real console's traffic reaches a
    /// switch-lan-play client. Anything else keeps using the host network stack through a
    /// <see cref="DefaultSocket"/>, so enabling LAN Play does not take the guest off the internet.
    /// </para>
    /// </summary>
    class LanPlaySocket : IPollableSocket
    {
        private const int PollIntervalMs = 15;

        private readonly LanPlayNetworkInterface _networkInterface;
        private readonly LanPlayStack _stack;
        private readonly string _lanInterfaceId;

        private LanPlayUdpEndpoint _udp;
        private LanPlayTcpListener _listener;
        private LanPlayTcpConnection _tcp;
        private ISocketImpl _hostSocket;

        private IPEndPoint _requestedLocalEndPoint;
        private IPEndPoint _connectedTo;
        private int _receiveTimeout = -1;
        private bool _closed;

        /// <summary>
        /// Every option the guest set on this socket, in the order it set them, so that they can be replayed
        /// on the host socket whenever that one ends up being created (see <see cref="EnsureHostSocket"/>).
        /// </summary>
        private readonly List<(SocketOptionLevel Level, SocketOptionName Name, object Value)> _guestSocketOptions = new();

        private readonly Dictionary<SocketOptionName, int> _socketOptions = new()
        {
            { SocketOptionName.Broadcast, 1 },
            { SocketOptionName.DontLinger, 0 },
            { SocketOptionName.Debug, 0 },
            { SocketOptionName.Error, 0 },
            { SocketOptionName.KeepAlive, 0 },
            { SocketOptionName.OutOfBandInline, 0 },
            { SocketOptionName.ReceiveBuffer, 131072 },
            { SocketOptionName.ReceiveTimeout, -1 },
            { SocketOptionName.SendBuffer, 131072 },
            { SocketOptionName.SendTimeout, -1 },
            { SocketOptionName.Type, 0 },
            { SocketOptionName.ReuseAddress, 0 },
        };

        public AddressFamily AddressFamily { get; }

        public SocketType SocketType { get; }

        public ProtocolType ProtocolType { get; }

        public bool Blocking { get; set; } = true;

        public EndPoint LocalEndPoint
        {
            get
            {
                if (_tcp != null)
                {
                    return _tcp.LocalEndPoint;
                }

                if (_listener != null)
                {
                    return _listener.LocalEndPoint;
                }

                if (_udp != null)
                {
                    return _udp.LocalEndPoint;
                }

                return _hostSocket?.LocalEndPoint ?? _requestedLocalEndPoint;
            }
        }

        public EndPoint RemoteEndPoint => _tcp?.RemoteEndPoint ?? _hostSocket?.RemoteEndPoint;

        public bool Connected => _tcp?.Connected ?? _hostSocket?.Connected ?? false;

        public bool IsBound => _udp != null || _listener != null || _tcp != null || (_hostSocket?.IsBound ?? false);

        public int Available
        {
            get
            {
                int available = _udp?.Available ?? _tcp?.Available ?? 0;

                if (_hostSocket != null)
                {
                    try
                    {
                        available += _hostSocket.Available;
                    }
                    catch (SocketException)
                    {
                        // The host socket is gone; only the virtual side counts.
                    }
                }

                return available;
            }
        }

        public bool Readable
        {
            get
            {
                if (_listener != null && _listener.Readable)
                {
                    return true;
                }

                if (_udp != null && _udp.Readable)
                {
                    return true;
                }

                if (_tcp != null && _tcp.Readable)
                {
                    return true;
                }

                try
                {
                    return _hostSocket != null && _hostSocket.Poll(0, SelectMode.SelectRead);
                }
                catch (SocketException)
                {
                    return true;
                }
            }
        }

        public bool Writable
        {
            get
            {
                if (ProtocolType == ProtocolType.Udp)
                {
                    return true;
                }

                if (_tcp != null)
                {
                    return _tcp.Writable;
                }

                try
                {
                    return _hostSocket == null || _hostSocket.Poll(0, SelectMode.SelectWrite);
                }
                catch (SocketException)
                {
                    return false;
                }
            }
        }

        public bool Error => _tcp?.Aborted ?? false;

        public LanPlaySocket(AddressFamily domain, SocketType type, ProtocolType protocol, LanPlayStack stack, string lanInterfaceId)
        {
            AddressFamily = domain;
            SocketType = type;
            ProtocolType = protocol;

            _stack = stack;
            _networkInterface = stack.NetworkInterface;
            _lanInterfaceId = lanInterfaceId;

            _socketOptions[SocketOptionName.Type] = (int)type;
        }

        private LanPlaySocket(LanPlayStack stack, LanPlayTcpConnection connection, string lanInterfaceId)
        {
            AddressFamily = AddressFamily.InterNetwork;
            SocketType = SocketType.Stream;
            ProtocolType = ProtocolType.Tcp;

            _stack = stack;
            _networkInterface = stack.NetworkInterface;
            _lanInterfaceId = lanInterfaceId;
            _tcp = connection;

            _socketOptions[SocketOptionName.Type] = (int)SocketType.Stream;
        }

        private bool IsLanPlayAddress(EndPoint endPoint)
        {
            return endPoint is IPEndPoint ipEndPoint &&
                   ipEndPoint.AddressFamily == AddressFamily.InterNetwork &&
                   _networkInterface.IsHandledAddress(NetworkHelpers.ConvertIpv4Address(ipEndPoint.Address));
        }

        private ISocketImpl EnsureHostSocket()
        {
            if (_hostSocket != null)
            {
                return _hostSocket;
            }

            _hostSocket = new DefaultSocket(AddressFamily, SocketType, ProtocolType, _lanInterfaceId)
            {
                Blocking = Blocking,
            };

            // Makes it obvious in a log that traffic which is not for the LAN Play network (an online
            // service, for instance) still leaves through the host network while LAN Play is selected.
            Logger.Info?.Print(LogClass.ServiceBsd,
                $"LAN Play: {ProtocolType} socket is using the host network for destinations outside {_networkInterface.Address}/16.");

            // The host socket is created on first use, so everything the guest set on the socket before
            // that has to be replayed on it, in order and before it is bound, or the host socket silently
            // behaves differently from the one the guest configured. SO_BROADCAST is the one that bites:
            // without it a broadcast fails with EACCES on Linux and macOS.
            foreach ((SocketOptionLevel level, SocketOptionName name, object value) in _guestSocketOptions)
            {
                try
                {
                    if (value is int intValue)
                    {
                        _hostSocket.SetSocketOption(level, name, intValue);
                    }
                    else
                    {
                        _hostSocket.SetSocketOption(level, name, value);
                    }
                }
                catch (SocketException ex)
                {
                    Logger.Debug?.Print(LogClass.ServiceBsd, $"LAN Play: the host socket refused {level}/{name}: {ex.SocketErrorCode}");
                }
            }

            if (ProtocolType == ProtocolType.Udp && _requestedLocalEndPoint != null)
            {
                try
                {
                    // Keep the port the guest asked for so replies from the host network still arrive.
                    _hostSocket.Bind(new IPEndPoint(IPAddress.Any, _requestedLocalEndPoint.Port));
                }
                catch (SocketException)
                {
                    _hostSocket.Bind(new IPEndPoint(IPAddress.Any, 0));
                }
            }

            return _hostSocket;
        }

        public void Bind(EndPoint localEP)
        {
            ArgumentNullException.ThrowIfNull(localEP);

            if (localEP is not IPEndPoint ipEndPoint)
            {
                throw new NotSupportedException();
            }

            _requestedLocalEndPoint = new IPEndPoint(ipEndPoint.Address, ipEndPoint.Port);

            if (ProtocolType == ProtocolType.Udp)
            {
                _udp = _networkInterface.BindUdp((ushort)ipEndPoint.Port);
            }
        }

        public void Listen(int backlog)
        {
            if (ProtocolType != ProtocolType.Tcp)
            {
                throw new SocketException((int)SocketError.OperationNotSupported);
            }

            _listener = _networkInterface.ListenTcp((ushort)(_requestedLocalEndPoint?.Port ?? 0));
        }

        public ISocketImpl Accept()
        {
            if (_listener == null)
            {
                throw new InvalidOperationException();
            }

            LanPlayTcpConnection connection = _listener.Accept(Blocking ? _receiveTimeout : 0);

            if (connection == null)
            {
                throw new SocketException((int)(Blocking ? SocketError.TimedOut : SocketError.WouldBlock));
            }

            _stack.MarkGuestActive("an incoming connection on the LAN Play network");

            return new LanPlaySocket(_stack, connection, _lanInterfaceId);
        }

        public void Connect(EndPoint remoteEP)
        {
            if (ProtocolType == ProtocolType.Udp)
            {
                // A connected UDP socket only fixes the default destination; both sides stay usable.
                if (!IsLanPlayAddress(remoteEP))
                {
                    EnsureHostSocket().Connect(remoteEP);
                }

                _udp ??= _networkInterface.BindUdp((ushort)(_requestedLocalEndPoint?.Port ?? 0));

                _connectedTo = remoteEP as IPEndPoint;

                return;
            }

            if (!IsLanPlayAddress(remoteEP))
            {
                EnsureHostSocket().Connect(remoteEP);

                return;
            }

            IPEndPoint target = (IPEndPoint)remoteEP;
            ushort localPort = (ushort)(_requestedLocalEndPoint is { Port: > 0 } local ? local.Port : _networkInterface.AllocateTcpPort());

            _stack.MarkGuestActive("a connection to a peer on the LAN Play network");

            _tcp = new LanPlayTcpConnection(_networkInterface, localPort, NetworkHelpers.ConvertIpv4Address(target.Address), (ushort)target.Port);

            if (!Blocking)
            {
                // A real stack starts the handshake and reports EINPROGRESS; the guest polls for writability.
                System.Threading.ThreadPool.QueueUserWorkItem(_ => _tcp.Connect(5000));

                throw new SocketException((int)SocketError.WouldBlock);
            }

            if (!_tcp.Connect(_receiveTimeout > 0 ? _receiveTimeout : 5000))
            {
                _tcp = null;

                throw new SocketException((int)SocketError.TimedOut);
            }
        }

        public int Send(ReadOnlySpan<byte> buffer) => Send(buffer, SocketFlags.None);

        public int Send(ReadOnlySpan<byte> buffer, SocketFlags flags)
        {
            if (_tcp != null)
            {
                return _tcp.Send(buffer);
            }

            if (_udp != null && _connectedTo != null && IsLanPlayAddress(_connectedTo))
            {
                _stack.MarkGuestActive("traffic to a peer on the LAN Play network");

                return _udp.SendTo(_connectedTo, buffer);
            }

            if (_hostSocket != null)
            {
                return _hostSocket.Send(buffer, flags);
            }

            throw new SocketException((int)SocketError.NotConnected);
        }

        public int Send(ReadOnlySpan<byte> buffer, SocketFlags flags, out SocketError socketError)
        {
            try
            {
                socketError = SocketError.Success;

                return Send(buffer, flags);
            }
            catch (SocketException ex)
            {
                socketError = ex.SocketErrorCode;

                return -1;
            }
        }

        public int SendTo(ReadOnlySpan<byte> buffer, SocketFlags flags, EndPoint remoteEP)
        {
            if (!IsLanPlayAddress(remoteEP))
            {
                return EnsureHostSocket().SendTo(buffer, flags, remoteEP);
            }

            if (ProtocolType != ProtocolType.Udp)
            {
                return Send(buffer, flags);
            }

            _udp ??= _networkInterface.BindUdp((ushort)(_requestedLocalEndPoint?.Port ?? 0));

            uint destination = NetworkHelpers.ConvertIpv4Address(((IPEndPoint)remoteEP).Address);

            _stack.MarkGuestActive(_networkInterface.IsBroadcast(destination) || LanPlayNetworkInterface.IsHostBroadcast(destination)
                ? "a broadcast on the LAN Play network"
                : "traffic to a peer on the LAN Play network");

            return _udp.SendTo((IPEndPoint)remoteEP, buffer);
        }

        public int Receive(Span<byte> buffer)
        {
            EndPoint dummy = new IPEndPoint(IPAddress.Any, 0);

            return ReceiveFrom(buffer, SocketFlags.None, ref dummy);
        }

        public int Receive(Span<byte> buffer, SocketFlags flags)
        {
            EndPoint dummy = new IPEndPoint(IPAddress.Any, 0);

            return ReceiveFrom(buffer, flags, ref dummy);
        }

        public int Receive(Span<byte> buffer, SocketFlags flags, out SocketError socketError)
        {
            try
            {
                socketError = SocketError.Success;

                return Receive(buffer, flags);
            }
            catch (SocketException ex)
            {
                socketError = ex.SocketErrorCode;

                return -1;
            }
        }

        public int ReceiveFrom(Span<byte> buffer, SocketFlags flags, ref EndPoint remoteEP)
        {
            if (_tcp != null)
            {
                remoteEP = _tcp.RemoteEndPoint;

                return _tcp.Receive(buffer, _receiveTimeout, (flags & SocketFlags.Peek) != 0, Blocking);
            }

            if (_udp == null && _hostSocket == null)
            {
                throw new SocketException((int)SocketError.NotConnected);
            }

            long deadline = _receiveTimeout > 0 ? Environment.TickCount64 + _receiveTimeout : long.MaxValue;

            while (true)
            {
                if (_closed)
                {
                    throw new SocketException((int)SocketError.Interrupted);
                }

                if (_udp != null && TryReceiveVirtual(buffer, flags, ref remoteEP, out int read))
                {
                    _stack.MarkGuestActive("traffic received from the LAN Play network");

                    return read;
                }

                if (_hostSocket != null && _hostSocket.Poll(0, SelectMode.SelectRead))
                {
                    return _hostSocket.ReceiveFrom(buffer, flags, ref remoteEP);
                }

                if (!Blocking)
                {
                    throw new SocketException((int)SocketError.WouldBlock);
                }

                if (Environment.TickCount64 >= deadline)
                {
                    throw new SocketException((int)SocketError.TimedOut);
                }

                // Both sides have to be watched, so wait in short slices instead of blocking on one.
                if (_udp != null)
                {
                    _udp.WaitForData(PollIntervalMs);
                }
                else
                {
                    System.Threading.Thread.Sleep(PollIntervalMs);
                }
            }
        }

        private bool TryReceiveVirtual(Span<byte> buffer, SocketFlags flags, ref EndPoint remoteEP, out int read)
        {
            read = 0;

            bool peek = (flags & SocketFlags.Peek) != 0;

            if (!(peek ? _udp.TryPeek(out LanPlayUdpEndpoint.Datagram datagram) : _udp.TryDequeue(out datagram)))
            {
                return false;
            }

            remoteEP = datagram.Source;

            read = Math.Min(buffer.Length, datagram.Data.Length);

            datagram.Data.AsSpan(0, read).CopyTo(buffer);

            return true;
        }

        public bool Poll(int microSeconds, SelectMode mode)
        {
            long deadline = Environment.TickCount64 + Math.Max(microSeconds / 1000, 0);

            while (true)
            {
                bool ready = mode switch
                {
                    SelectMode.SelectRead => Readable,
                    SelectMode.SelectWrite => Writable,
                    SelectMode.SelectError => Error,
                    _ => false,
                };

                if (ready || microSeconds == 0)
                {
                    return ready;
                }

                if (microSeconds > 0 && Environment.TickCount64 >= deadline)
                {
                    return false;
                }

                System.Threading.Thread.Sleep(PollIntervalMs);
            }
        }

        public void GetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, byte[] optionValue)
        {
            if (optionLevel != SocketOptionLevel.Socket || !_socketOptions.TryGetValue(optionName, out int value))
            {
                _hostSocket?.GetSocketOption(optionLevel, optionName, optionValue);

                return;
            }

            byte[] data = BitConverter.GetBytes(value);

            data.AsSpan(0, Math.Min(data.Length, optionValue.Length)).CopyTo(optionValue);
        }

        public void SetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, int optionValue)
        {
            if (optionLevel == SocketOptionLevel.Socket)
            {
                _socketOptions[optionName] = optionValue;

                if (optionName == SocketOptionName.ReceiveTimeout)
                {
                    _receiveTimeout = optionValue;
                }
            }

            RememberForHostSocket(optionLevel, optionName, optionValue);

            try
            {
                _hostSocket?.SetSocketOption(optionLevel, optionName, optionValue);
            }
            catch (SocketException)
            {
                // The virtual side accepted it; the host socket is best effort.
            }
        }

        public void SetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, object optionValue)
        {
            RememberForHostSocket(optionLevel, optionName, optionValue);

            try
            {
                _hostSocket?.SetSocketOption(optionLevel, optionName, optionValue);
            }
            catch (SocketException)
            {
                // Same as above.
            }
        }

        private void RememberForHostSocket(SocketOptionLevel optionLevel, SocketOptionName optionName, object optionValue)
        {
            _guestSocketOptions.RemoveAll(option => option.Level == optionLevel && option.Name == optionName);
            _guestSocketOptions.Add((optionLevel, optionName, optionValue));
        }

        public void Shutdown(SocketShutdown how)
        {
            if (_tcp != null && how != SocketShutdown.Receive)
            {
                _tcp.Close();
            }

            _hostSocket?.Shutdown(how);
        }

        public void Disconnect(bool reuseSocket)
        {
            _tcp?.Close();
            _tcp = null;
            _connectedTo = null;

            _hostSocket?.Disconnect(reuseSocket);
        }

        public void Close()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;

            _udp?.Close();
            _udp = null;

            _listener?.Close();
            _listener = null;

            _tcp?.Close();
            _tcp = null;

            try
            {
                _hostSocket?.Close();
            }
            catch (SocketException ex)
            {
                Logger.Debug?.Print(LogClass.ServiceBsd, $"LAN Play: error while closing the host socket: {ex.SocketErrorCode}");
            }
        }

        public void Dispose()
        {
            Close();

            _hostSocket?.Dispose();
            _hostSocket = null;
        }
    }
}
