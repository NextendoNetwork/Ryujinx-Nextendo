using Ryujinx.Common.Logging;
using Ryujinx.Common.Utilities;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay
{
    /// <summary>
    /// Checks a LAN Play relay without starting a game, so that a broken setup can be told apart from a
    /// game specific problem. It joins the relay exactly like the emulator does, sends the same LDN scan
    /// request a console sends, and reports what came back.
    /// </summary>
    public static class LanPlayConnectionTest
    {
        private const int ListenTimeMs = 4000;
        private const ushort LdnPort = 11452;
        private const uint LanMagic = 0x11451400;

        public readonly record struct Result(bool Success, string Message);

        public static Result Run(string server, string virtualAddress)
        {
            if (!LanPlayConfiguration.TryParse(server, virtualAddress, out LanPlayConfiguration configuration))
            {
                return new Result(false, "Could not use this server. Expected host:port, for example switch.example.com:11451. See the log for details.");
            }

            LanPlayClient client = null;
            LanPlayNetworkInterface networkInterface = null;

            try
            {
                client = new LanPlayClient(configuration);
                client.Start();

                HashSet<IPAddress> peers = [];
                int scanResponses = 0;
                string relayMessage = null;

                client.InfoReceived += message => relayMessage = message;

                client.Ipv4Received += packet =>
                {
                    if (!Ipv4Packet.TryParse(packet, out Ipv4Packet.Header header))
                    {
                        return;
                    }

                    lock (peers)
                    {
                        peers.Add(NetworkHelpers.ConvertUint(header.Source));
                    }

                    if (IsScanResponse(header, packet))
                    {
                        Interlocked.Increment(ref scanResponses);
                    }
                };

                uint address = VirtualAddressAllocator.Allocate(client, configuration.VirtualAddress);

                networkInterface = new LanPlayNetworkInterface(client, address);
                networkInterface.Announce();

                // The same scan request a console sends when it looks for a local session, so that any
                // console or emulator hosting one on this relay answers.
                LanPlayUdpEndpoint endpoint = networkInterface.BindUdp(LdnPort);

                endpoint.SendTo(new IPEndPoint(networkInterface.BroadcastAddress, LdnPort), BuildScanRequest());

                Thread.Sleep(ListenTimeMs);

                StringBuilder message = new();

                message.Append($"Joined the relay {configuration.RelayEndPoint} as {networkInterface.Address}. ");

                lock (peers)
                {
                    if (peers.Count == 0)
                    {
                        message.Append("No traffic came back, which is normal when nobody else is connected. " +
                                       "If other players are on this relay, check that they use the same address and port.");
                    }
                    else
                    {
                        message.Append($"Saw {peers.Count} other participant(s): {string.Join(", ", peers)}. ");
                        message.Append(scanResponses > 0
                            ? $"{scanResponses} of them answered the local session scan."
                            : "None of them is hosting a local session right now.");
                    }
                }

                if (relayMessage != null)
                {
                    message.Append($" Relay message: {relayMessage}");
                }

                string report = client.Diagnostics.GetReport();

                Logger.Info?.Print(LogClass.ServiceLdn, $"LAN Play test: {report}");

                return new Result(true, message.ToString());
            }
            catch (Exception ex)
            {
                Logger.Error?.Print(LogClass.ServiceLdn, $"LAN Play test failed: {ex}");

                return new Result(false, $"Could not join the relay: {ex.Message}");
            }
            finally
            {
                networkInterface?.Dispose();
                client?.Dispose();
            }
        }

        private static bool IsScanResponse(in Ipv4Packet.Header header, byte[] packet)
        {
            if (header.Protocol != Ipv4Packet.ProtocolUdp)
            {
                return false;
            }

            ReadOnlySpan<byte> datagram = packet.AsSpan(header.HeaderLength);

            // UDP header, then the ldn_mitm header: magic, type.
            if (datagram.Length < 8 + 5 || BinaryPrimitives.ReadUInt16BigEndian(datagram[2..]) != LdnPort)
            {
                return false;
            }

            ReadOnlySpan<byte> ldn = datagram[8..];

            return BinaryPrimitives.ReadUInt32LittleEndian(ldn) == LanMagic && ldn[4] == 1; // LanPacketType.ScanResponse
        }

        private static byte[] BuildScanRequest()
        {
            // ldn_mitm header of an empty scan request: magic, type, compressed, length, decompressed length.
            byte[] request = new byte[12];

            BinaryPrimitives.WriteUInt32LittleEndian(request, LanMagic);

            return request;
        }
    }
}
