using Ryujinx.Common.Logging;
using Ryujinx.Common.Utilities;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnRyu.Proxy;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay
{
    /// <summary>
    /// Virtual network interface of the emulated console on the LAN Play network.
    /// <para>
    /// It owns a 10.13.x.x address that only exists inside Ryujinx, turns the traffic of the emulated
    /// console into IPv4 packets for <see cref="LanPlayClient"/>, and dispatches the IPv4 packets coming
    /// back from the relay to the UDP endpoints, TCP connections and ICMP handler registered on it.
    /// No host network interface is touched and no ARP is needed, because the relay boundary is IPv4.
    /// </para>
    /// </summary>
    class LanPlayNetworkInterface : IDisposable
    {
        private const int ReassemblySlots = 16;
        private const int ReassemblyTimeoutMs = 5000;
        private const int TimerIntervalMs = 100;

        private sealed class Reassembly
        {
            public ushort Identification;
            public uint Source;
            public uint Destination;
            public byte Protocol;
            public int Length;
            public bool HasLast;
            public int ReceivedBytes;
            public long Timestamp;
            public byte[] Buffer = new byte[ushort.MaxValue];
        }

        // Broadcast addresses of the host's own networks, read once: the stack only exists while a game is
        // using the network, and a guest socket asks about them for every datagram it sends.
        private static readonly Lazy<HashSet<uint>> _hostBroadcastAddresses = new(CollectHostBroadcastAddresses);

        private readonly LanPlayClient _client;
        private readonly EphemeralPortPool _udpPorts = new();
        private readonly EphemeralPortPool _tcpPorts = new();

        private readonly Dictionary<ushort, LanPlayUdpEndpoint> _udpEndpoints = new();
        private readonly Dictionary<ushort, LanPlayTcpListener> _tcpListeners = new();
        private readonly Dictionary<(ushort LocalPort, uint RemoteAddress, ushort RemotePort), LanPlayTcpConnection> _tcpConnections = new();
        private readonly Reassembly[] _reassemblies = new Reassembly[ReassemblySlots];

        private readonly Lock _udpLock = new();
        private readonly Lock _tcpLock = new();
        private readonly Lock _reassemblyLock = new();

        private readonly Timer _timer;
        private int _identification;
        private bool _disposed;

        public uint AddressV4 { get; }

        public IPAddress Address { get; }

        public IPAddress SubnetMask { get; } = NetworkHelpers.ConvertUint(LanPlayProtocol.SubnetMask);

        public IPAddress BroadcastAddress { get; } = NetworkHelpers.ConvertUint(LanPlayProtocol.SubnetBroadcast);

        public LanPlayClient Client => _client;

        public LanPlayDiagnostics Diagnostics => _client.Diagnostics;

        public LanPlayNetworkInterface(LanPlayClient client, uint address)
        {
            _client = client;

            AddressV4 = address;
            Address = NetworkHelpers.ConvertUint(address);

            _client.Ipv4Received += HandleIpv4;

            _timer = new Timer(_ => Tick(), null, TimerIntervalMs, TimerIntervalMs);
        }

        public bool IsInSubnet(uint address)
        {
            return (address & LanPlayProtocol.SubnetMask) == LanPlayProtocol.SubnetNetwork;
        }

        /// <summary>
        /// True for anything the LAN Play relay floods to every client: the subnet broadcast address and
        /// the all-ones broadcast address a guest may use for discovery.
        /// </summary>
        public bool IsBroadcast(uint address)
        {
            return address is LanPlayProtocol.SubnetBroadcast or uint.MaxValue;
        }

        /// <summary>
        /// True for the broadcast address of one of the host's own IPv4 networks, such as 192.168.0.255 for
        /// a host on 192.168.0.91/24.
        /// <para>
        /// A game that implements its own LAN mode asks nifm for the console's address and broadcasts to
        /// that network's broadcast address, so it uses the host network's broadcast address for as long as
        /// the console still presents its host address (see <see cref="LanPlayStack.IsGuestActive"/>). Those
        /// broadcasts are LAN discovery and belong on the LAN Play network: leaving through the host network
        /// they could never reach the relay, and on Linux and macOS they fail outright with EACCES unless
        /// the game asked for SO_BROADCAST.
        /// </para>
        /// </summary>
        public static bool IsHostBroadcast(uint address)
        {
            return _hostBroadcastAddresses.Value.Contains(address);
        }

        /// <summary>
        /// True when traffic for this address belongs on the LAN Play network rather than on the host network.
        /// </summary>
        public bool IsHandledAddress(uint address)
        {
            return IsInSubnet(address) || IsBroadcast(address) || IsHostBroadcast(address);
        }

        private static HashSet<uint> CollectHostBroadcastAddresses()
        {
            HashSet<uint> addresses = new();

            try
            {
                // The Ldn namespace has a NetworkInterface of its own, hence the qualified name here.
                foreach (System.Net.NetworkInformation.NetworkInterface adapter in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    foreach (UnicastIPAddressInformation unicastAddress in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (unicastAddress.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(unicastAddress.Address))
                        {
                            continue;
                        }

                        IPAddress mask = unicastAddress.IPv4Mask;

                        if (mask == null)
                        {
                            continue;
                        }

                        uint maskValue = NetworkHelpers.ConvertIpv4Address(mask);

                        // A /32 has no broadcast address of its own, and an unusable mask is not one.
                        if (maskValue is 0 or uint.MaxValue)
                        {
                            continue;
                        }

                        addresses.Add(NetworkHelpers.ConvertIpv4Address(unicastAddress.Address) | ~maskValue);
                    }
                }
            }
            catch (Exception exception)
            {
                Logger.Warning?.Print(LogClass.ServiceLdn, $"LAN Play: could not read the host's networks: {exception.Message}");
            }

            return addresses;
        }

        #region UDP

        public LanPlayUdpEndpoint BindUdp(ushort port)
        {
            lock (_udpLock)
            {
                if (port == 0)
                {
                    port = _udpPorts.Get();
                }
                else if (_udpEndpoints.ContainsKey(port))
                {
                    throw new SocketException((int)SocketError.AddressAlreadyInUse);
                }

                LanPlayUdpEndpoint endpoint = new(this, port);

                _udpEndpoints[port] = endpoint;

                return endpoint;
            }
        }

        public void UnbindUdp(LanPlayUdpEndpoint endpoint)
        {
            lock (_udpLock)
            {
                if (_udpEndpoints.TryGetValue(endpoint.LocalPort, out LanPlayUdpEndpoint registered) && registered == endpoint)
                {
                    _udpEndpoints.Remove(endpoint.LocalPort);
                    _udpPorts.Return(endpoint.LocalPort);
                }
            }
        }

        public void SendUdp(ushort sourcePort, uint destinationAddress, ushort destinationPort, ReadOnlySpan<byte> data)
        {
            // The relay floods the LAN Play broadcast address, and a peer drops anything addressed to
            // neither itself nor a broadcast address, so a broadcast aimed at the host's network has to be
            // re-addressed to the LAN Play network to be seen by anybody.
            if (IsHostBroadcast(destinationAddress))
            {
                destinationAddress = LanPlayProtocol.SubnetBroadcast;
            }

            int length = 8 + data.Length;
            byte[] segment = new byte[length];

            BinaryPrimitives.WriteUInt16BigEndian(segment, sourcePort);
            BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), destinationPort);
            BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(4), (ushort)length);

            data.CopyTo(segment.AsSpan(8));

            ushort checksum = Ipv4Packet.TransportChecksum(AddressV4, destinationAddress, Ipv4Packet.ProtocolUdp, segment);

            // A zero checksum means "not computed" in UDP, so the all-ones form has to be used instead.
            BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(6), checksum == 0 ? (ushort)0xFFFF : checksum);

            SendIpv4(destinationAddress, Ipv4Packet.ProtocolUdp, segment);
        }

        private void HandleUdp(in Ipv4Packet.Header header, ReadOnlySpan<byte> segment)
        {
            if (segment.Length < 8)
            {
                return;
            }

            ushort sourcePort = BinaryPrimitives.ReadUInt16BigEndian(segment);
            ushort destinationPort = BinaryPrimitives.ReadUInt16BigEndian(segment[2..]);
            int length = BinaryPrimitives.ReadUInt16BigEndian(segment[4..]);

            if (length < 8 || length > segment.Length)
            {
                length = segment.Length;
            }

            // A zero checksum means the sender did not compute one, which UDP allows.
            if (BinaryPrimitives.ReadUInt16BigEndian(segment[6..]) != 0 &&
                Ipv4Packet.TransportChecksum(header.Source, header.Destination, Ipv4Packet.ProtocolUdp, segment[..length]) != 0)
            {
                Diagnostics.Dropped(LanPlayDropReason.BadTransportChecksum, $"udp {sourcePort} -> {destinationPort}");

                return;
            }

            LanPlayUdpEndpoint endpoint;

            lock (_udpLock)
            {
                _udpEndpoints.TryGetValue(destinationPort, out endpoint);
            }

            if (endpoint == null)
            {
                Diagnostics.Dropped(LanPlayDropReason.NoUdpEndpoint, $"udp port {destinationPort}");

                return;
            }

            endpoint.HandleDatagram(
                new IPEndPoint(NetworkHelpers.ConvertUint(header.Source), sourcePort),
                header.Destination,
                segment[8..length]);
        }

        #endregion

        #region TCP

        public LanPlayTcpListener ListenTcp(ushort port)
        {
            lock (_tcpLock)
            {
                if (port == 0)
                {
                    port = _tcpPorts.Get();
                }
                else if (_tcpListeners.ContainsKey(port))
                {
                    throw new SocketException((int)SocketError.AddressAlreadyInUse);
                }

                LanPlayTcpListener listener = new(this, port);

                _tcpListeners[port] = listener;

                return listener;
            }
        }

        public void RemoveListener(LanPlayTcpListener listener)
        {
            lock (_tcpLock)
            {
                if (_tcpListeners.TryGetValue(listener.LocalPort, out LanPlayTcpListener registered) && registered == listener)
                {
                    _tcpListeners.Remove(listener.LocalPort);
                }
            }
        }

        public ushort AllocateTcpPort()
        {
            return _tcpPorts.Get();
        }

        public void RegisterConnection(LanPlayTcpConnection connection)
        {
            lock (_tcpLock)
            {
                _tcpConnections[(connection.LocalPort, connection.RemoteAddress, connection.RemotePort)] = connection;
            }
        }

        public void UnregisterConnection(LanPlayTcpConnection connection)
        {
            lock (_tcpLock)
            {
                (ushort, uint, ushort) key = (connection.LocalPort, connection.RemoteAddress, connection.RemotePort);

                if (_tcpConnections.TryGetValue(key, out LanPlayTcpConnection registered) && registered == connection)
                {
                    _tcpConnections.Remove(key);
                }
            }
        }

        private void HandleTcp(in Ipv4Packet.Header header, ReadOnlySpan<byte> segment)
        {
            if (segment.Length < LanPlayTcpConnection.HeaderSize || IsBroadcast(header.Destination))
            {
                Diagnostics.Dropped(LanPlayDropReason.Malformed, "tcp segment");

                return;
            }

            ushort sourcePort = BinaryPrimitives.ReadUInt16BigEndian(segment);
            ushort destinationPort = BinaryPrimitives.ReadUInt16BigEndian(segment[2..]);

            if (Ipv4Packet.TransportChecksum(header.Source, header.Destination, Ipv4Packet.ProtocolTcp, segment) != 0)
            {
                Diagnostics.Dropped(LanPlayDropReason.BadTransportChecksum, $"tcp {sourcePort} -> {destinationPort}");

                return;
            }

            LanPlayTcpConnection connection;
            LanPlayTcpListener listener = null;

            lock (_tcpLock)
            {
                if (!_tcpConnections.TryGetValue((destinationPort, header.Source, sourcePort), out connection))
                {
                    _tcpListeners.TryGetValue(destinationPort, out listener);
                }
            }

            if (connection != null)
            {
                connection.HandleSegment(segment);
            }
            else if (listener != null)
            {
                listener.HandleSegment(header.Source, sourcePort, segment);
            }
            else
            {
                Diagnostics.Dropped(LanPlayDropReason.NoTcpConnection, $"tcp port {destinationPort}");

                LanPlayTcpConnection.SendReset(this, destinationPort, header.Source, sourcePort, segment);
            }
        }

        #endregion

        #region ICMP

        private void HandleIcmp(in Ipv4Packet.Header header, ReadOnlySpan<byte> message)
        {
            if (message.Length < 8)
            {
                return;
            }

            const byte EchoReply = 0;
            const byte EchoRequest = 8;

            // Answering pings makes the emulated console behave like a real one on the LAN Play network,
            // and lets other clients detect that our virtual address is taken.
            if (message[0] == EchoRequest && !IsBroadcast(header.Destination))
            {
                byte[] reply = message.ToArray();

                reply[0] = EchoReply;
                BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(2), 0);
                BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(2), Ipv4Packet.Checksum(reply));

                SendIpv4(header.Source, Ipv4Packet.ProtocolIcmp, reply);
            }
        }

        /// <summary>
        /// Announces our virtual address to the relay, which routes on the addresses it has seen its
        /// clients use. A real console does this implicitly with its ARP and discovery traffic; without it
        /// a relay would not know where to send packets addressed to us until we spoke first.
        /// </summary>
        public void Announce()
        {
            byte[] message = new byte[16];

            message[0] = 8;
            BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(4), (ushort)AddressV4);
            BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(2), Ipv4Packet.Checksum(message));

            SendIpv4(LanPlayProtocol.SubnetBroadcast, Ipv4Packet.ProtocolIcmp, message);
        }

        #endregion

        /// <summary>
        /// Wraps a transport payload in an IPv4 header and hands it to the relay, fragmenting it the way a
        /// real interface would when it does not fit in a single Ethernet frame.
        /// </summary>
        public void SendIpv4(uint destination, byte protocol, ReadOnlySpan<byte> payload)
        {
            if (_disposed)
            {
                return;
            }

            uint sourceAddress = AddressV4;
            ushort identification = (ushort)Interlocked.Increment(ref _identification);

            if (payload.Length <= Ipv4Packet.MaxPayloadSize)
            {
                byte[] packet = new byte[Ipv4Packet.MinHeaderSize + payload.Length];

                Ipv4Packet.WriteHeader(packet, sourceAddress, destination, protocol, payload.Length, identification);
                payload.CopyTo(packet.AsSpan(Ipv4Packet.MinHeaderSize));

                _client.SendIpv4(packet);

                Diagnostics.Ipv4Sent(destination, protocol, packet.Length, 1);

                return;
            }

            // IPv4 fragment payloads must be a multiple of 8 bytes, except for the last one.
            int fragmentPayload = Ipv4Packet.MaxPayloadSize & ~7;
            int fragmentCount = (payload.Length + fragmentPayload - 1) / fragmentPayload;

            Diagnostics.Ipv4Sent(destination, protocol, payload.Length, fragmentCount);

            for (int offset = 0; offset < payload.Length; offset += fragmentPayload)
            {
                int length = Math.Min(fragmentPayload, payload.Length - offset);
                bool last = offset + length >= payload.Length;

                ushort flags = (ushort)((last ? 0 : Ipv4Packet.FlagMoreFragments) | ((offset / 8) & Ipv4Packet.FragmentOffsetMask));

                byte[] packet = new byte[Ipv4Packet.MinHeaderSize + length];

                Ipv4Packet.WriteHeader(packet, sourceAddress, destination, protocol, length, identification, flags);
                payload.Slice(offset, length).CopyTo(packet.AsSpan(Ipv4Packet.MinHeaderSize));

                _client.SendIpv4(packet);
            }
        }

        private void HandleIpv4(byte[] packet)
        {
            if (!Ipv4Packet.TryParse(packet, out Ipv4Packet.Header header))
            {
                Diagnostics.Dropped(LanPlayDropReason.Malformed, $"{packet.Length} byte ipv4 packet");

                return;
            }

            if (Ipv4Packet.Checksum(packet.AsSpan(0, header.HeaderLength)) != 0)
            {
                Diagnostics.Dropped(LanPlayDropReason.BadHeaderChecksum);

                return;
            }

            // The relay hands us everything it routes our way, plus anything it floods. Drop what a real
            // interface would drop: packets addressed to somebody else, and our own echoed traffic.
            if (header.Destination != AddressV4 && !IsBroadcast(header.Destination))
            {
                Diagnostics.Dropped(LanPlayDropReason.NotForUs);

                return;
            }

            // Traffic that appears to come from us is either our own broadcast coming back or a relay
            // reflecting our own packets.
            if (header.Source == AddressV4)
            {
                Diagnostics.Dropped(LanPlayDropReason.LoopedBack);

                return;
            }

            if (header.IsFragment)
            {
                if (!TryReassemble(header, packet, out byte[] reassembled, out int reassembledLength))
                {
                    return;
                }

                Diagnostics.Ipv4Received(header, reassembled.AsSpan(0, reassembledLength));

                Dispatch(header, reassembled.AsSpan(0, reassembledLength));

                return;
            }

            ReadOnlySpan<byte> transportPayload = packet.AsSpan(header.HeaderLength, header.TotalLength - header.HeaderLength);

            Diagnostics.Ipv4Received(header, transportPayload);

            Dispatch(header, transportPayload);
        }

        private void Dispatch(in Ipv4Packet.Header header, ReadOnlySpan<byte> payload)
        {
            switch (header.Protocol)
            {
                case Ipv4Packet.ProtocolUdp:
                    HandleUdp(header, payload);
                    break;

                case Ipv4Packet.ProtocolTcp:
                    HandleTcp(header, payload);
                    break;

                case Ipv4Packet.ProtocolIcmp:
                    HandleIcmp(header, payload);
                    break;

                default:
                    Diagnostics.Dropped(LanPlayDropReason.UnsupportedProtocol, $"ip protocol {header.Protocol}");
                    break;
            }
        }

        private bool TryReassemble(in Ipv4Packet.Header header, byte[] packet, out byte[] payload, out int length)
        {
            payload = null;
            length = 0;

            int fragmentLength = header.TotalLength - header.HeaderLength;

            if (fragmentLength <= 0 || header.FragmentOffset + fragmentLength > ushort.MaxValue)
            {
                Diagnostics.Dropped(LanPlayDropReason.Reassembly, $"fragment of {fragmentLength} bytes at offset {header.FragmentOffset}");

                return false;
            }

            lock (_reassemblyLock)
            {
                long now = Environment.TickCount64;
                Reassembly entry = null;
                int free = -1;

                for (int i = 0; i < _reassemblies.Length; i++)
                {
                    Reassembly candidate = _reassemblies[i];

                    if (candidate == null)
                    {
                        free = i;

                        continue;
                    }

                    if (now - candidate.Timestamp > ReassemblyTimeoutMs)
                    {
                        _reassemblies[i] = null;
                        free = i;

                        continue;
                    }

                    if (candidate.Identification == header.Identification &&
                        candidate.Source == header.Source &&
                        candidate.Destination == header.Destination &&
                        candidate.Protocol == header.Protocol)
                    {
                        entry = candidate;

                        break;
                    }
                }

                if (entry == null)
                {
                    if (free == -1)
                    {
                        Diagnostics.Dropped(LanPlayDropReason.Reassembly, "reassembly buffer is full");

                        return false;
                    }

                    entry = new Reassembly
                    {
                        Identification = header.Identification,
                        Source = header.Source,
                        Destination = header.Destination,
                        Protocol = header.Protocol,
                    };

                    _reassemblies[free] = entry;
                }

                entry.Timestamp = now;
                entry.ReceivedBytes += fragmentLength;

                packet.AsSpan(header.HeaderLength, fragmentLength).CopyTo(entry.Buffer.AsSpan(header.FragmentOffset));

                if (!header.MoreFragments)
                {
                    entry.HasLast = true;
                    entry.Length = header.FragmentOffset + fragmentLength;
                }

                if (!entry.HasLast || entry.ReceivedBytes < entry.Length)
                {
                    return false;
                }

                payload = entry.Buffer;
                length = entry.Length;

                for (int i = 0; i < _reassemblies.Length; i++)
                {
                    if (_reassemblies[i] == entry)
                    {
                        _reassemblies[i] = null;

                        break;
                    }
                }

                return true;
            }
        }

        private void Tick()
        {
            Diagnostics.Tick();

            List<LanPlayTcpConnection> connections;

            lock (_tcpLock)
            {
                connections = [.. _tcpConnections.Values];
            }

            foreach (LanPlayTcpConnection connection in connections)
            {
                try
                {
                    connection.Tick();
                }
                catch (Exception ex)
                {
                    Logger.Warning?.Print(LogClass.ServiceLdn, $"LAN Play: error while servicing a TCP connection: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _timer.Dispose();
            _client.Ipv4Received -= HandleIpv4;

            LanPlayUdpEndpoint[] endpoints;
            LanPlayTcpConnection[] connections;
            LanPlayTcpListener[] listeners;

            lock (_udpLock)
            {
                endpoints = [.. _udpEndpoints.Values];
                _udpEndpoints.Clear();
            }

            lock (_tcpLock)
            {
                connections = [.. _tcpConnections.Values];
                listeners = [.. _tcpListeners.Values];

                _tcpConnections.Clear();
                _tcpListeners.Clear();
            }

            foreach (LanPlayUdpEndpoint endpoint in endpoints)
            {
                endpoint.Close();
            }

            foreach (LanPlayTcpConnection connection in connections)
            {
                connection.Abort();
            }

            foreach (LanPlayTcpListener listener in listeners)
            {
                listener.Close();
            }
        }
    }
}
