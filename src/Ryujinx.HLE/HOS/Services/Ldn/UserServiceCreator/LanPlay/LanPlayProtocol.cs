using System;
using System.Buffers.Binary;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay
{
    /// <summary>
    /// Wire format spoken between a switch-lan-play client and a LAN Play relay server.
    /// Every datagram sent to (or received from) the relay is a single type byte followed by a payload.
    /// See https://github.com/spacemeowx2/switch-lan-play (src/lan-client.c).
    /// </summary>
    static class LanPlayProtocol
    {
        /// <summary>
        /// Virtual network shared by every client of a relay (SUBNET_NET / SUBNET_MASK in switch-lan-play).
        /// </summary>
        public const uint SubnetNetwork = 0x0A0D0000; // 10.13.0.0
        public const uint SubnetMask = 0xFFFF0000;    // 255.255.0.0
        public const uint SubnetBroadcast = SubnetNetwork | ~SubnetMask; // 10.13.255.255

        /// <summary>
        /// Address the switch-lan-play client itself answers on (its gateway / "fake internet" address).
        /// Never handed out to a console, so we must not use it either.
        /// </summary>
        public const uint GatewayAddress = 0x0A0D2501; // 10.13.37.1

        public const int DefaultRelayPort = 11451;

        /// <summary>
        /// The relay drops clients that go quiet; the reference client sends a keepalive every 10 seconds.
        /// </summary>
        public const int KeepAliveIntervalMs = 10 * 1000;

        /// <summary>
        /// Largest payload the relay is expected to handle in a single datagram (ETHER_MTU).
        /// </summary>
        public const int MaxIpv4PacketSize = 1500;

        public const int AuthKeyLength = 20;

        public enum PacketType : byte
        {
            KeepAlive = 0x00,
            Ipv4 = 0x01,
            Ping = 0x02,
            Ipv4Fragment = 0x03,
            AuthMe = 0x04,
            Info = 0x10,
        }

        /// <summary>
        /// Header of a <see cref="PacketType.Ipv4Fragment"/> payload (LC_FRAG_* in switch-lan-play).
        /// </summary>
        public const int FragmentHeaderSize = 16;

        public struct FragmentHeader
        {
            public uint Source;
            public uint Destination;
            public ushort Id;
            public byte Part;
            public byte TotalParts;
            public ushort Length;
            public ushort PathMtu;
        }

        public static bool TryReadFragmentHeader(ReadOnlySpan<byte> payload, out FragmentHeader header)
        {
            header = default;

            if (payload.Length < FragmentHeaderSize)
            {
                return false;
            }

            header.Source = BinaryPrimitives.ReadUInt32BigEndian(payload);
            header.Destination = BinaryPrimitives.ReadUInt32BigEndian(payload[4..]);
            header.Id = BinaryPrimitives.ReadUInt16BigEndian(payload[8..]);
            header.Part = payload[10];
            header.TotalParts = payload[11];
            header.Length = BinaryPrimitives.ReadUInt16BigEndian(payload[12..]);
            header.PathMtu = BinaryPrimitives.ReadUInt16BigEndian(payload[14..]);

            return header.TotalParts is > 0 and <= 32 && payload.Length >= FragmentHeaderSize + header.Length;
        }

        public static void WriteFragmentHeader(Span<byte> destination, in FragmentHeader header)
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination, header.Source);
            BinaryPrimitives.WriteUInt32BigEndian(destination[4..], header.Destination);
            BinaryPrimitives.WriteUInt16BigEndian(destination[8..], header.Id);
            destination[10] = header.Part;
            destination[11] = header.TotalParts;
            BinaryPrimitives.WriteUInt16BigEndian(destination[12..], header.Length);
            BinaryPrimitives.WriteUInt16BigEndian(destination[14..], header.PathMtu);
        }

        /// <summary>
        /// Builds the response to a <see cref="PacketType.AuthMe"/> challenge of auth type 0:
        /// sha1(sha1(password) + challenge) followed by the user name in plain text.
        /// </summary>
        public static byte[] BuildAuthResponse(ReadOnlySpan<byte> passwordHash, ReadOnlySpan<byte> challenge, string userName)
        {
            byte[] userNameBytes = System.Text.Encoding.UTF8.GetBytes(userName);
            byte[] response = new byte[AuthKeyLength + userNameBytes.Length];

            byte[] hashInput = new byte[passwordHash.Length + challenge.Length];
            passwordHash.CopyTo(hashInput);
            challenge.CopyTo(hashInput.AsSpan(passwordHash.Length));

            System.Security.Cryptography.SHA1.HashData(hashInput, response.AsSpan(0, AuthKeyLength));
            userNameBytes.CopyTo(response.AsSpan(AuthKeyLength));

            return response;
        }
    }
}
