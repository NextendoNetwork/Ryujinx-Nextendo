using Ryujinx.Common.Logging;
using Ryujinx.Common.Utilities;
using System;
using System.Buffers.Binary;
using System.Net;
using System.Threading;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay
{
    /// <summary>
    /// Picks the 10.13.x.x address the emulated console presents on the LAN Play network.
    /// <para>
    /// The host keeps its own address; this one only exists inside Ryujinx. A candidate is probed before
    /// being adopted: we ask the relay to deliver an ICMP echo request to it and watch for any traffic
    /// coming back from that address, which is how a real console's duplicate address detection behaves
    /// on a LAN. This is best effort, because the relay only routes on the IPv4 destination.
    /// </para>
    /// </summary>
    static class VirtualAddressAllocator
    {
        private const int ProbeTimeoutMs = 500;
        private const int ProbeAttempts = 4;
        private const byte EchoReply = 0;
        private const byte EchoRequest = 8;
        private const ushort ProbeIdentifier = 0x5279; // "Ry"

        /// <summary>
        /// Addresses that must never be handed out: the network and broadcast addresses of 10.13.0.0/16,
        /// anything ending in .0 or .255 (a real LAN Play setup avoids those), and the address the
        /// switch-lan-play client itself answers on.
        /// </summary>
        public static bool IsUsableAddress(uint address)
        {
            if ((address & LanPlayProtocol.SubnetMask) != LanPlayProtocol.SubnetNetwork)
            {
                return false;
            }

            if (address == LanPlayProtocol.GatewayAddress)
            {
                return false;
            }

            byte third = (byte)(address >> 8);
            byte fourth = (byte)address;

            return fourth is not (0 or 255) && !(third == 0 && fourth == 1);
        }

        public static uint Allocate(LanPlayClient client, IPAddress preferredAddress)
        {
            if (preferredAddress != null)
            {
                uint preferred = NetworkHelpers.ConvertIpv4Address(preferredAddress);

                if (IsUsableAddress(preferred))
                {
                    Logger.Info?.Print(LogClass.ServiceLdn, $"LAN Play: using the configured virtual address {preferredAddress}.");

                    return preferred;
                }
            }

            Random random = new();

            for (int attempt = 0; attempt < ProbeAttempts; attempt++)
            {
                uint candidate = RandomAddress(random);

                if (!IsAddressTaken(client, candidate))
                {
                    Logger.Info?.Print(LogClass.ServiceLdn, $"LAN Play: using the virtual address {NetworkHelpers.ConvertUint(candidate)}.");

                    return candidate;
                }

                Logger.Info?.Print(LogClass.ServiceLdn, $"LAN Play: virtual address {NetworkHelpers.ConvertUint(candidate)} is already in use, trying another one.");
            }

            uint fallback = RandomAddress(random);

            Logger.Warning?.Print(LogClass.ServiceLdn, $"LAN Play: could not confirm a free virtual address, using {NetworkHelpers.ConvertUint(fallback)}.");

            return fallback;
        }

        private static uint RandomAddress(Random random)
        {
            while (true)
            {
                uint candidate = LanPlayProtocol.SubnetNetwork | (uint)(random.Next(0, 256) << 8) | (uint)random.Next(1, 255);

                if (IsUsableAddress(candidate))
                {
                    return candidate;
                }
            }
        }

        /// <summary>
        /// Asks the relay to deliver an ICMP echo request to the candidate address and reports whether
        /// somebody answered it.
        /// </summary>
        public static bool IsAddressTaken(LanPlayClient client, uint candidate)
        {
            using ManualResetEventSlim answered = new(false);

            // The probe is sent from a throwaway address rather than from the candidate itself: a relay
            // routes packets to whichever client last used an address, so probing "as" the candidate would
            // make the relay send the probe straight back to us instead of to the console we are looking for.
            uint probeSource = RandomAddress(Random.Shared);

            while (probeSource == candidate)
            {
                probeSource = RandomAddress(Random.Shared);
            }

            void OnPacket(byte[] packet)
            {
                if (!Ipv4Packet.TryParse(packet, out Ipv4Packet.Header header) ||
                    header.Source != candidate ||
                    header.Protocol != Ipv4Packet.ProtocolIcmp)
                {
                    return;
                }

                ReadOnlySpan<byte> message = packet.AsSpan(header.HeaderLength);

                // Only an echo *reply* means somebody answered. Relays route on the addresses they have
                // seen, so they can reflect the probe itself back at us, which proves nothing.
                if (message.Length >= 8 && message[0] == EchoReply && BinaryPrimitives.ReadUInt16BigEndian(message[4..]) == ProbeIdentifier)
                {
                    answered.Set();
                }
            }

            client.Ipv4Received += OnPacket;

            try
            {
                client.SendIpv4(BuildEchoRequest(probeSource, candidate));

                return answered.Wait(ProbeTimeoutMs);
            }
            finally
            {
                client.Ipv4Received -= OnPacket;
            }
        }

        private static byte[] BuildEchoRequest(uint source, uint candidate)
        {
            byte[] packet = new byte[Ipv4Packet.MinHeaderSize + 16];
            Span<byte> message = packet.AsSpan(Ipv4Packet.MinHeaderSize);

            message[0] = EchoRequest;
            BinaryPrimitives.WriteUInt16BigEndian(message[4..], ProbeIdentifier);
            BinaryPrimitives.WriteUInt16BigEndian(message[6..], 1);
            BinaryPrimitives.WriteUInt16BigEndian(message[2..], Ipv4Packet.Checksum(message));

            Ipv4Packet.WriteHeader(packet, source, candidate, Ipv4Packet.ProtocolIcmp, message.Length, 1);

            return packet;
        }
    }
}
