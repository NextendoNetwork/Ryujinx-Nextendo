using Ryujinx.Common.Logging;
using Ryujinx.Common.Utilities;
using System;
using System.Buffers.Binary;
using System.Text;
using System.Threading;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay
{
    /// <summary>
    /// Why a packet was thrown away. Every drop is counted so that a session that "does not work" can be
    /// diagnosed from the log without a packet capture.
    /// </summary>
    enum LanPlayDropReason
    {
        /// <summary>Too short or not a valid IPv4 packet.</summary>
        Malformed,
        /// <summary>Addressed to another console; the relay floods more than it routes.</summary>
        NotForUs,
        /// <summary>Our own traffic coming back from the relay.</summary>
        LoopedBack,
        /// <summary>The IPv4 header checksum did not match.</summary>
        BadHeaderChecksum,
        /// <summary>The UDP or TCP checksum did not match.</summary>
        BadTransportChecksum,
        /// <summary>Nothing is bound to the destination UDP port.</summary>
        NoUdpEndpoint,
        /// <summary>No TCP connection or listener for the segment; a reset was sent back.</summary>
        NoTcpConnection,
        /// <summary>An IPv4 datagram could not be reassembled.</summary>
        Reassembly,
        /// <summary>A LAN Play level fragment could not be reassembled.</summary>
        RelayFragment,
        /// <summary>A protocol the virtual interface does not implement.</summary>
        UnsupportedProtocol,
        /// <summary>A receive queue was full.</summary>
        QueueFull,
    }

    /// <summary>
    /// Counters and tracing for a LAN Play session.
    /// <para>
    /// Three levels of detail, so that testing a game does not require a debugger:
    /// counters and a periodic summary (info), a line per packet (network logs), and hex dumps of the
    /// first few relay datagrams (trace). It also raises a warning when the relay stops answering,
    /// which is by far the most common failure.
    /// </para>
    /// </summary>
    class LanPlayDiagnostics
    {
        private const int SummaryIntervalMs = 30 * 1000;
        private const int SilenceWarningMs = 30 * 1000;
        private const int HexDumpPacketCount = 8;
        private const int HexDumpMaxBytes = 64;

        private readonly string _relay;
        private readonly long _startTime = Environment.TickCount64;

        private long _relayDatagramsSent;
        private long _relayDatagramsReceived;
        private long _relayBytesSent;
        private long _relayBytesReceived;
        private long _keepAlivesSent;
        private long _relayFragmentsSent;
        private long _relayFragmentsReceived;

        private long _ipv4Sent;
        private long _ipv4Received;
        private long _ipv4FragmentsSent;
        private long _udpSent;
        private long _udpReceived;
        private long _tcpSegmentsSent;
        private long _tcpSegmentsReceived;
        private long _tcpRetransmits;
        private long _tcpResets;
        private long _icmpSent;
        private long _icmpReceived;

        private readonly long[] _drops = new long[Enum.GetValues<LanPlayDropReason>().Length];

        private long _lastRelayReceiveTime;
        private long _lastSummaryTime = Environment.TickCount64;
        private bool _silenceReported;
        private int _hexDumpsLeft = HexDumpPacketCount;

        /// <summary>
        /// True when the user asked for network logs, which turns on the per packet trace.
        /// </summary>
        public static bool IsTraceEnabled => Logger.NetLog != null;

        public LanPlayDiagnostics(string relay)
        {
            _relay = relay;
        }

        #region Relay level

        public void RelayDatagramSent(LanPlayProtocol.PacketType type, int size)
        {
            Interlocked.Increment(ref _relayDatagramsSent);
            Interlocked.Add(ref _relayBytesSent, size);

            switch (type)
            {
                case LanPlayProtocol.PacketType.KeepAlive:
                    Interlocked.Increment(ref _keepAlivesSent);
                    break;

                case LanPlayProtocol.PacketType.Ipv4Fragment:
                    Interlocked.Increment(ref _relayFragmentsSent);
                    break;
            }
        }

        public void RelayDatagramReceived(LanPlayProtocol.PacketType type, ReadOnlySpan<byte> datagram)
        {
            Interlocked.Increment(ref _relayDatagramsReceived);
            Interlocked.Add(ref _relayBytesReceived, datagram.Length);

            _lastRelayReceiveTime = Environment.TickCount64;

            if (_silenceReported)
            {
                _silenceReported = false;

                Logger.Info?.Print(LogClass.ServiceLdn, $"LAN Play: the relay {_relay} is answering again.");
            }

            if (type == LanPlayProtocol.PacketType.Ipv4Fragment)
            {
                Interlocked.Increment(ref _relayFragmentsReceived);
            }

            if (_hexDumpsLeft > 0 && Logger.Trace != null && type != LanPlayProtocol.PacketType.KeepAlive)
            {
                _hexDumpsLeft--;

                Logger.Trace?.Print(LogClass.ServiceLdn, $"LAN Play: relay datagram {type} ({datagram.Length} bytes): {Hex(datagram)}");
            }
        }

        #endregion

        #region Interface level

        public void Ipv4Sent(uint destination, byte protocol, int length, int fragments)
        {
            Interlocked.Increment(ref _ipv4Sent);

            if (fragments > 1)
            {
                Interlocked.Add(ref _ipv4FragmentsSent, fragments);
            }

            switch (protocol)
            {
                case Ipv4Packet.ProtocolUdp:
                    Interlocked.Increment(ref _udpSent);
                    break;

                case Ipv4Packet.ProtocolTcp:
                    Interlocked.Increment(ref _tcpSegmentsSent);
                    break;

                case Ipv4Packet.ProtocolIcmp:
                    Interlocked.Increment(ref _icmpSent);
                    break;
            }

            if (IsTraceEnabled)
            {
                Logger.NetLog?.PrintMsg(LogClass.ServiceLdn,
                    $"LAN Play out: {ProtocolName(protocol)} -> {NetworkHelpers.ConvertUint(destination)} {length} bytes{(fragments > 1 ? $" in {fragments} fragments" : string.Empty)}");
            }
        }

        public void Ipv4Received(in Ipv4Packet.Header header, ReadOnlySpan<byte> payload)
        {
            Interlocked.Increment(ref _ipv4Received);

            switch (header.Protocol)
            {
                case Ipv4Packet.ProtocolUdp:
                    Interlocked.Increment(ref _udpReceived);
                    break;

                case Ipv4Packet.ProtocolTcp:
                    Interlocked.Increment(ref _tcpSegmentsReceived);
                    break;

                case Ipv4Packet.ProtocolIcmp:
                    Interlocked.Increment(ref _icmpReceived);
                    break;
            }

            if (IsTraceEnabled)
            {
                Logger.NetLog?.PrintMsg(LogClass.ServiceLdn,
                    $"LAN Play in: {ProtocolName(header.Protocol)} {NetworkHelpers.ConvertUint(header.Source)} -> {NetworkHelpers.ConvertUint(header.Destination)}" +
                    $"{Ports(header.Protocol, payload)} {header.TotalLength} bytes");
            }
        }

        public void TcpRetransmit(int attempt)
        {
            Interlocked.Increment(ref _tcpRetransmits);

            if (IsTraceEnabled)
            {
                Logger.NetLog?.PrintMsg(LogClass.ServiceLdn, $"LAN Play: TCP retransmission (attempt {attempt})");
            }
        }

        public void TcpReset()
        {
            Interlocked.Increment(ref _tcpResets);
        }

        public void Dropped(LanPlayDropReason reason, string detail = null)
        {
            Interlocked.Increment(ref _drops[(int)reason]);

            // Drops that always happen on a relay (flooded traffic, our own echoes) are not worth a line.
            if (IsTraceEnabled && reason is not (LanPlayDropReason.NotForUs or LanPlayDropReason.LoopedBack))
            {
                Logger.NetLog?.PrintMsg(LogClass.ServiceLdn, $"LAN Play: dropped a packet ({reason}){(detail == null ? string.Empty : $": {detail}")}");
            }
        }

        #endregion

        /// <summary>
        /// Called periodically: logs a summary and warns when the relay has gone quiet.
        /// </summary>
        public void Tick()
        {
            long now = Environment.TickCount64;

            if (!_silenceReported &&
                _relayDatagramsSent > 0 &&
                now - Math.Max(_lastRelayReceiveTime, _startTime) > SilenceWarningMs)
            {
                _silenceReported = true;

                Logger.Warning?.Print(LogClass.ServiceLdn,
                    _lastRelayReceiveTime == 0
                        ? $"LAN Play: nothing received from the relay {_relay} in {SilenceWarningMs / 1000} seconds. " +
                          "Check the address and port, that outbound UDP is not blocked, and that the relay is up."
                        : $"LAN Play: nothing received from the relay {_relay} in {SilenceWarningMs / 1000} seconds. " +
                          "The relay or every other player may have gone away.");
            }

            if (now - _lastSummaryTime < SummaryIntervalMs)
            {
                return;
            }

            _lastSummaryTime = now;

            // Only worth logging once there is traffic to talk about.
            if (_relayDatagramsReceived > 0 || _ipv4Sent > 0)
            {
                Logger.Info?.Print(LogClass.ServiceLdn, $"LAN Play status: {GetReport()}");
            }
        }

        /// <summary>
        /// One line summary of the session, also logged on shutdown so every log ends with it.
        /// </summary>
        public string GetReport()
        {
            StringBuilder report = new();

            report.Append($"relay {_relay}, ");
            report.Append($"sent {_ipv4Sent} packets ({_relayBytesSent} bytes, {_keepAlivesSent} keepalives), ");
            report.Append($"received {_ipv4Received} packets ({_relayBytesReceived} bytes), ");
            report.Append($"udp {_udpSent}/{_udpReceived}, tcp {_tcpSegmentsSent}/{_tcpSegmentsReceived}, icmp {_icmpSent}/{_icmpReceived}");

            if (_ipv4FragmentsSent > 0 || _relayFragmentsSent > 0 || _relayFragmentsReceived > 0)
            {
                report.Append($", fragments ip {_ipv4FragmentsSent} relay {_relayFragmentsSent}/{_relayFragmentsReceived}");
            }

            if (_tcpRetransmits > 0 || _tcpResets > 0)
            {
                report.Append($", tcp retransmits {_tcpRetransmits}, resets {_tcpResets}");
            }

            bool anyDrop = false;

            for (int i = 0; i < _drops.Length; i++)
            {
                if (_drops[i] == 0)
                {
                    continue;
                }

                report.Append(anyDrop ? ", " : ", dropped ");
                report.Append($"{(LanPlayDropReason)i}={_drops[i]}");

                anyDrop = true;
            }

            if (_lastRelayReceiveTime == 0)
            {
                report.Append(", never heard from the relay");
            }

            return report.ToString();
        }

        public void LogFinalReport()
        {
            Logger.Info?.Print(LogClass.ServiceLdn, $"LAN Play session ended: {GetReport()}");
        }

        private static string ProtocolName(byte protocol) => protocol switch
        {
            Ipv4Packet.ProtocolIcmp => "ICMP",
            Ipv4Packet.ProtocolTcp => "TCP",
            Ipv4Packet.ProtocolUdp => "UDP",
            _ => $"protocol {protocol}",
        };

        private static string Ports(byte protocol, ReadOnlySpan<byte> payload)
        {
            if (protocol is not (Ipv4Packet.ProtocolTcp or Ipv4Packet.ProtocolUdp) || payload.Length < 4)
            {
                return string.Empty;
            }

            return $" ports {BinaryPrimitives.ReadUInt16BigEndian(payload)} -> {BinaryPrimitives.ReadUInt16BigEndian(payload[2..])}";
        }

        private static string Hex(ReadOnlySpan<byte> data)
        {
            int length = Math.Min(data.Length, HexDumpMaxBytes);
            StringBuilder hex = new(length * 3);

            for (int i = 0; i < length; i++)
            {
                hex.Append(data[i].ToString("x2"));
                hex.Append(' ');
            }

            if (data.Length > length)
            {
                hex.Append("...");
            }

            return hex.ToString();
        }
    }
}
