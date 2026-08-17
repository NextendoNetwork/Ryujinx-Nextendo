using System;
using System.Buffers.Binary;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay
{
    /// <summary>
    /// Reading and writing of the IPv4 headers exchanged with the LAN Play relay.
    /// </summary>
    static class Ipv4Packet
    {
        public const int MinHeaderSize = 20;

        public const int TotalLengthOffset = 2;
        public const int IdentificationOffset = 4;
        public const int FlagsOffset = 6;
        public const int ProtocolOffset = 9;
        public const int ChecksumOffset = 10;
        public const int SourceOffset = 12;
        public const int DestinationOffset = 16;

        public const byte ProtocolIcmp = 1;
        public const byte ProtocolTcp = 6;
        public const byte ProtocolUdp = 17;

        public const ushort FlagDontFragment = 0x4000;
        public const ushort FlagMoreFragments = 0x2000;
        public const ushort FragmentOffsetMask = 0x1FFF;

        /// <summary>
        /// Largest IPv4 payload we put on the wire before fragmenting, so that a datagram stays within
        /// the Ethernet MTU a real console (and every switch-lan-play client) would use.
        /// </summary>
        public const int MaxPayloadSize = LanPlayProtocol.MaxIpv4PacketSize - MinHeaderSize;

        public struct Header
        {
            public int HeaderLength;
            public int TotalLength;
            public ushort Identification;
            public ushort Flags;
            public int FragmentOffset;
            public byte Protocol;
            public uint Source;
            public uint Destination;

            public bool MoreFragments => (Flags & FlagMoreFragments) != 0;
            public bool IsFragment => MoreFragments || FragmentOffset != 0;
        }

        public static bool TryParse(ReadOnlySpan<byte> packet, out Header header)
        {
            header = default;

            if (packet.Length < MinHeaderSize)
            {
                return false;
            }

            if ((packet[0] >> 4) != 4)
            {
                return false;
            }

            int headerLength = (packet[0] & 0xF) * 4;

            if (headerLength < MinHeaderSize || headerLength > packet.Length)
            {
                return false;
            }

            int totalLength = BinaryPrimitives.ReadUInt16BigEndian(packet[TotalLengthOffset..]);

            // Some senders pad the frame; never read past what we actually received.
            if (totalLength < headerLength || totalLength > packet.Length)
            {
                totalLength = packet.Length;
            }

            ushort flagsAndOffset = BinaryPrimitives.ReadUInt16BigEndian(packet[FlagsOffset..]);

            header = new Header
            {
                HeaderLength = headerLength,
                TotalLength = totalLength,
                Identification = BinaryPrimitives.ReadUInt16BigEndian(packet[IdentificationOffset..]),
                Flags = (ushort)(flagsAndOffset & ~FragmentOffsetMask),
                FragmentOffset = (flagsAndOffset & FragmentOffsetMask) * 8,
                Protocol = packet[ProtocolOffset],
                Source = BinaryPrimitives.ReadUInt32BigEndian(packet[SourceOffset..]),
                Destination = BinaryPrimitives.ReadUInt32BigEndian(packet[DestinationOffset..]),
            };

            return true;
        }

        public static void WriteHeader(
            Span<byte> destination,
            uint source,
            uint dest,
            byte protocol,
            int payloadLength,
            ushort identification,
            ushort flagsAndFragmentOffset = 0,
            byte ttl = 128)
        {
            destination[..MinHeaderSize].Clear();

            destination[0] = 0x45;
            destination[1] = 0;

            BinaryPrimitives.WriteUInt16BigEndian(destination[TotalLengthOffset..], (ushort)(MinHeaderSize + payloadLength));
            BinaryPrimitives.WriteUInt16BigEndian(destination[IdentificationOffset..], identification);
            BinaryPrimitives.WriteUInt16BigEndian(destination[FlagsOffset..], flagsAndFragmentOffset);

            destination[8] = ttl;
            destination[ProtocolOffset] = protocol;

            BinaryPrimitives.WriteUInt32BigEndian(destination[SourceOffset..], source);
            BinaryPrimitives.WriteUInt32BigEndian(destination[DestinationOffset..], dest);
            BinaryPrimitives.WriteUInt16BigEndian(destination[ChecksumOffset..], Checksum(destination[..MinHeaderSize]));
        }

        /// <summary>
        /// Standard internet checksum (RFC 1071) over the given span.
        /// </summary>
        public static ushort Checksum(ReadOnlySpan<byte> data)
        {
            return FinishChecksum(PartialChecksum(data, 0));
        }

        public static uint PartialChecksum(ReadOnlySpan<byte> data, uint sum)
        {
            int i = 0;

            for (; i + 1 < data.Length; i += 2)
            {
                sum += (uint)((data[i] << 8) | data[i + 1]);
            }

            if (i < data.Length)
            {
                sum += (uint)(data[i] << 8);
            }

            return sum;
        }

        public static ushort FinishChecksum(uint sum)
        {
            while ((sum >> 16) != 0)
            {
                sum = (sum & 0xFFFF) + (sum >> 16);
            }

            return (ushort)~sum;
        }

        /// <summary>
        /// Checksum of a TCP or UDP segment, including the IPv4 pseudo header.
        /// </summary>
        public static ushort TransportChecksum(uint source, uint dest, byte protocol, ReadOnlySpan<byte> segment)
        {
            Span<byte> pseudoHeader = stackalloc byte[12];

            BinaryPrimitives.WriteUInt32BigEndian(pseudoHeader, source);
            BinaryPrimitives.WriteUInt32BigEndian(pseudoHeader[4..], dest);
            pseudoHeader[8] = 0;
            pseudoHeader[9] = protocol;
            BinaryPrimitives.WriteUInt16BigEndian(pseudoHeader[10..], (ushort)segment.Length);

            uint sum = PartialChecksum(pseudoHeader, 0);
            sum = PartialChecksum(segment, sum);

            return FinishChecksum(sum);
        }
    }
}
