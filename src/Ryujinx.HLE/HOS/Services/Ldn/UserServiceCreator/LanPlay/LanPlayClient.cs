using Ryujinx.Common.Logging;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay
{
    /// <summary>
    /// Embedded switch-lan-play client: carries raw IPv4 packets between the emulated console's virtual
    /// network interface and a LAN Play relay server over a single host UDP socket.
    /// </summary>
    class LanPlayClient : IDisposable
    {
        private const int ReceiveBufferSize = 4096;
        private const int FragmentSlots = 32;
        private const int FragmentTimeoutMs = 5000;

        private sealed class Fragment
        {
            public ushort Id;
            public uint Source;
            public uint ReceivedParts;
            public int TotalLength;
            public long Timestamp;
            public byte[] Buffer = new byte[LanPlayProtocol.MaxIpv4PacketSize * 4];
        }

        private readonly LanPlayConfiguration _configuration;
        private readonly Socket _socket;
        private readonly EndPoint _relayEndPoint;
        private readonly Fragment[] _fragments = new Fragment[FragmentSlots];
        private readonly Lock _fragmentLock = new();
        private readonly Lock _sendLock = new();

        private Thread _receiveThread;
        private Timer _keepAliveTimer;
        private ushort _fragmentId;
        private bool _disposed;

        /// <summary>
        /// Raised for every complete IPv4 packet received from the relay. The callback runs on the
        /// client's receive thread, so it must not block for long.
        /// </summary>
        public event Action<byte[]> Ipv4Received;

        /// <summary>
        /// Raised when the relay sends us a human readable message (<see cref="LanPlayProtocol.PacketType.Info"/>).
        /// </summary>
        public event Action<string> InfoReceived;

        public IPEndPoint RelayEndPoint { get; }

        public bool IsRunning { get; private set; }

        /// <summary>
        /// Timestamp (<see cref="Environment.TickCount64"/>) of the last datagram received from the relay,
        /// or 0 if nothing was ever received.
        /// </summary>
        public long LastReceiveTime { get; private set; }

        public ulong SentPackets { get; private set; }
        public ulong ReceivedPackets { get; private set; }

        public LanPlayClient(LanPlayConfiguration configuration)
        {
            _configuration = configuration;

            RelayEndPoint = configuration.RelayEndPoint;
            _relayEndPoint = RelayEndPoint;

            _socket = new Socket(RelayEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            _socket.Bind(new IPEndPoint(RelayEndPoint.AddressFamily == AddressFamily.InterNetworkV6
                ? IPAddress.IPv6Any
                : IPAddress.Any, 0));
        }

        public void Start()
        {
            if (IsRunning)
            {
                return;
            }

            IsRunning = true;

            _receiveThread = new Thread(ReceiveLoop)
            {
                Name = "LanPlay.Receive",
                IsBackground = true,
            };

            _receiveThread.Start();

            _keepAliveTimer = new Timer(_ => SendKeepAlive(), null, 0, LanPlayProtocol.KeepAliveIntervalMs);

            Logger.Info?.Print(LogClass.ServiceLdn, $"LAN Play: connected to relay {RelayEndPoint}.");
        }

        /// <summary>
        /// Sends a complete IPv4 packet (starting at its header) to the relay, fragmenting it at the
        /// configured path MTU if needed. The relay routes it using the packet's destination address.
        /// </summary>
        public void SendIpv4(ReadOnlySpan<byte> packet)
        {
            if (!IsRunning || packet.Length < Ipv4Packet.MinHeaderSize)
            {
                return;
            }

            int pathMtu = _configuration.PathMtu;

            if (pathMtu > 0 && packet.Length > pathMtu)
            {
                SendFragmented(packet, pathMtu);

                return;
            }

            Send(LanPlayProtocol.PacketType.Ipv4, packet);
        }

        private void SendFragmented(ReadOnlySpan<byte> packet, int pathMtu)
        {
            int totalParts = (packet.Length + pathMtu - 1) / pathMtu;

            if (totalParts > 32)
            {
                Logger.Warning?.Print(LogClass.ServiceLdn, $"LAN Play: dropping {packet.Length} byte packet, too large for the configured path MTU.");

                return;
            }

            LanPlayProtocol.FragmentHeader header = new()
            {
                Source = BinaryPrimitives.ReadUInt32BigEndian(packet[Ipv4Packet.SourceOffset..]),
                Destination = BinaryPrimitives.ReadUInt32BigEndian(packet[Ipv4Packet.DestinationOffset..]),
                TotalParts = (byte)totalParts,
                PathMtu = (ushort)pathMtu,
            };

            ushort id;

            lock (_sendLock)
            {
                id = _fragmentId++;
            }

            header.Id = id;

            byte[] buffer = new byte[LanPlayProtocol.FragmentHeaderSize + pathMtu];

            for (int part = 0; part < totalParts; part++)
            {
                int offset = part * pathMtu;
                int length = Math.Min(pathMtu, packet.Length - offset);

                header.Part = (byte)part;
                header.Length = (ushort)length;

                LanPlayProtocol.WriteFragmentHeader(buffer, header);
                packet.Slice(offset, length).CopyTo(buffer.AsSpan(LanPlayProtocol.FragmentHeaderSize));

                Send(LanPlayProtocol.PacketType.Ipv4Fragment, buffer.AsSpan(0, LanPlayProtocol.FragmentHeaderSize + length));
            }
        }

        private void SendKeepAlive()
        {
            Send(LanPlayProtocol.PacketType.KeepAlive, ReadOnlySpan<byte>.Empty);
        }

        private void Send(LanPlayProtocol.PacketType type, ReadOnlySpan<byte> payload)
        {
            if (_disposed)
            {
                return;
            }

            Span<byte> datagram = payload.Length + 1 <= 2048
                ? stackalloc byte[payload.Length + 1]
                : new byte[payload.Length + 1];

            datagram[0] = (byte)type;
            payload.CopyTo(datagram[1..]);

            try
            {
                lock (_sendLock)
                {
                    _socket.SendTo(datagram, SocketFlags.None, _relayEndPoint);
                }

                SentPackets++;
            }
            catch (SocketException ex)
            {
                Logger.Warning?.Print(LogClass.ServiceLdn, $"LAN Play: failed to send {type} to the relay: {ex.SocketErrorCode}.");
            }
            catch (ObjectDisposedException)
            {
                // The client was disposed while sending.
            }
        }

        private void ReceiveLoop()
        {
            byte[] buffer = new byte[ReceiveBufferSize];
            EndPoint remote = new IPEndPoint(RelayEndPoint.AddressFamily == AddressFamily.InterNetworkV6
                ? IPAddress.IPv6Any
                : IPAddress.Any, 0);

            while (IsRunning)
            {
                int size;

                try
                {
                    size = _socket.ReceiveFrom(buffer, ref remote);
                }
                catch (SocketException ex)
                {
                    if (!IsRunning)
                    {
                        break;
                    }

                    // An ICMP error queued against our socket must not stop the client.
                    if (ex.SocketErrorCode is SocketError.ConnectionReset or SocketError.NetworkReset or SocketError.MessageSize)
                    {
                        continue;
                    }

                    Logger.Warning?.Print(LogClass.ServiceLdn, $"LAN Play: relay socket error {ex.SocketErrorCode}, stopping.");

                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (size < 1)
                {
                    continue;
                }

                LastReceiveTime = Environment.TickCount64;
                ReceivedPackets++;

                HandleDatagram(buffer.AsSpan(0, size));
            }

            IsRunning = false;
        }

        private void HandleDatagram(ReadOnlySpan<byte> datagram)
        {
            // The high bit marks an encrypted packet, which no known relay uses.
            LanPlayProtocol.PacketType type = (LanPlayProtocol.PacketType)(datagram[0] & 0x7F);
            ReadOnlySpan<byte> payload = datagram[1..];

            switch (type)
            {
                case LanPlayProtocol.PacketType.KeepAlive:
                case LanPlayProtocol.PacketType.Ping:
                    break;

                case LanPlayProtocol.PacketType.Ipv4:
                    DeliverIpv4(payload);
                    break;

                case LanPlayProtocol.PacketType.Ipv4Fragment:
                    HandleFragment(payload);
                    break;

                case LanPlayProtocol.PacketType.AuthMe:
                    HandleAuthMe(payload);
                    break;

                case LanPlayProtocol.PacketType.Info:
                    string message = System.Text.Encoding.UTF8.GetString(payload);

                    Logger.Info?.Print(LogClass.ServiceLdn, $"LAN Play relay: {message}");

                    InfoReceived?.Invoke(message);
                    break;

                default:
                    Logger.Debug?.Print(LogClass.ServiceLdn, $"LAN Play: ignoring unknown relay packet type 0x{(byte)type:X2}.");
                    break;
            }
        }

        private void DeliverIpv4(ReadOnlySpan<byte> packet)
        {
            if (packet.Length < Ipv4Packet.MinHeaderSize)
            {
                return;
            }

            try
            {
                Ipv4Received?.Invoke(packet.ToArray());
            }
            catch (Exception ex)
            {
                Logger.Error?.Print(LogClass.ServiceLdn, $"LAN Play: error while handling a received packet: {ex}");
            }
        }

        private void HandleFragment(ReadOnlySpan<byte> payload)
        {
            if (!LanPlayProtocol.TryReadFragmentHeader(payload, out LanPlayProtocol.FragmentHeader header))
            {
                return;
            }

            byte[] completed = null;
            int completedLength = 0;

            lock (_fragmentLock)
            {
                long now = Environment.TickCount64;
                Fragment fragment = null;
                int free = -1;

                for (int i = 0; i < _fragments.Length; i++)
                {
                    Fragment candidate = _fragments[i];

                    if (candidate == null)
                    {
                        free = i;

                        continue;
                    }

                    if (now - candidate.Timestamp > FragmentTimeoutMs)
                    {
                        _fragments[i] = null;
                        free = i;

                        continue;
                    }

                    if (candidate.Id == header.Id && candidate.Source == header.Source)
                    {
                        fragment = candidate;

                        break;
                    }
                }

                if (fragment == null)
                {
                    if (free == -1)
                    {
                        Logger.Warning?.Print(LogClass.ServiceLdn, "LAN Play: fragment buffer is full, dropping fragment.");

                        return;
                    }

                    fragment = new Fragment
                    {
                        Id = header.Id,
                        Source = header.Source,
                    };

                    _fragments[free] = fragment;
                }

                fragment.Timestamp = now;

                int offset = header.PathMtu * header.Part;

                if (offset + header.Length > fragment.Buffer.Length)
                {
                    return;
                }

                payload.Slice(LanPlayProtocol.FragmentHeaderSize, header.Length).CopyTo(fragment.Buffer.AsSpan(offset));

                fragment.ReceivedParts |= 1u << header.Part;

                if (header.Part == header.TotalParts - 1)
                {
                    fragment.TotalLength = offset + header.Length;
                }

                uint completeMask = header.TotalParts == 32 ? uint.MaxValue : (1u << header.TotalParts) - 1;

                if (fragment.ReceivedParts == completeMask && fragment.TotalLength != 0)
                {
                    completed = fragment.Buffer;
                    completedLength = fragment.TotalLength;

                    for (int i = 0; i < _fragments.Length; i++)
                    {
                        if (_fragments[i] == fragment)
                        {
                            _fragments[i] = null;

                            break;
                        }
                    }
                }
            }

            if (completed != null)
            {
                DeliverIpv4(completed.AsSpan(0, completedLength));
            }
        }

        private void HandleAuthMe(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 1)
            {
                return;
            }

            if (_configuration.UserName == null || _configuration.PasswordHash == null)
            {
                Logger.Warning?.Print(LogClass.ServiceLdn, "LAN Play: the relay requires a user name and password. Set them in the LAN Play server field as user:password@host:port.");

                return;
            }

            byte authType = payload[0];

            if (authType != 0)
            {
                Logger.Warning?.Print(LogClass.ServiceLdn, $"LAN Play: unsupported relay authentication type {authType}.");

                return;
            }

            byte[] response = LanPlayProtocol.BuildAuthResponse(_configuration.PasswordHash, payload[1..], _configuration.UserName);

            Send(LanPlayProtocol.PacketType.AuthMe, response);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            IsRunning = false;

            _keepAliveTimer?.Dispose();
            _keepAliveTimer = null;

            try
            {
                _socket.Close();
            }
            catch (SocketException)
            {
                // Nothing useful to do while tearing down.
            }

            _socket.Dispose();

            lock (_fragmentLock)
            {
                Array.Clear(_fragments);
            }

            Ipv4Received = null;
            InfoReceived = null;
        }
    }
}
