using Ryujinx.Common.Logging;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo] Connection checks the player can run on demand, without launching a game: how
    /// far away the servers are, and whether their NAT lets other players reach them directly.
    /// Both come out of a single probe of the NAT-check servers (nncs), because that is the one
    /// piece of infrastructure that speaks UDP to the same path the games use for P2P.
    /// </summary>
    public static class NextendoNetworkCheck
    {
        public enum NatType
        {
            /// <summary>Probe failed — servers unreachable, or UDP blocked outright.</summary>
            Unknown,

            /// <summary>Endpoint-independent mapping: peers can reach us directly.</summary>
            Open,

            /// <summary>Port-dependent mapping (symmetric): direct P2P will not hole-punch.</summary>
            Strict,
        }

        public sealed class Result
        {
            public NatType Nat = NatType.Unknown;

            /// <summary>Best UDP round-trip seen across the probes, in ms; 0 when unreachable.</summary>
            public long LatencyMs;

            public bool Reachable;

            /// <summary>External endpoint the servers observed, e.g. "203.0.113.7:39057".</summary>
            public string ExternalEndpoint = "";

            /// <summary>How many of the four probes answered — the honest confidence signal.</summary>
            public int ProbesAnswered;

            public string LatencyColor => !Reachable ? "#E8333E"
                : LatencyMs < 80 ? "#33E86B"
                : LatencyMs < 150 ? "#E8C83E"
                : "#E8833E";

            public string NatColor => Nat switch
            {
                NatType.Open => "#33E86B",
                NatType.Strict => "#E8833E",
                _ => "#808080",
            };
        }

        // The two NAT-check responders, at two DISTINCT addresses (the pair is required for the
        // NAT check). Addresses come from environment variables, not baked into this open-source tree.
        private static readonly string Nncs1 = Environment.GetEnvironmentVariable("NEXTENDO_SERVER_IP") ?? "127.0.0.1";
        private static readonly string Nncs2 = Environment.GetEnvironmentVariable("NEXTENDO_NAT_IP") ?? "127.0.0.1";
        private static readonly int[] _nncsPorts = [10025, 10125];

        // Test id the responder echoes back. The Switch sends 101/102/103; any id it knows is
        // fine for us, and 101 is the one it always sends first.
        private const uint TestId = 101;

        private const int ProbeTimeoutMs = 3000;

        /// <summary>
        /// Probes both responders on both ports from ONE socket and classifies the NAT from the
        /// external port they each report back.
        ///
        /// A NAT with endpoint-independent mapping reuses a single external port for every
        /// destination, so all four probes report the same number. A symmetric NAT allocates a
        /// fresh port per destination, so they disagree — and that is exactly the case Pia cannot
        /// hole-punch, which is why it is worth showing the player before they blame the game.
        ///
        /// Only mapping is measured, not filtering: telling "open" from "moderate" needs replies
        /// from an unsolicited address/port, and the deployed responder answers every probe from
        /// the socket it arrived on. Reporting a filtering verdict off this data would be a
        /// guess, so the display stays at Open/Strict rather than inventing a third state.
        /// </summary>
        public static async Task<Result> CheckAsync(CancellationToken cancellation = default)
        {
            Result result = new();

            try
            {
                using Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Bind(new IPEndPoint(IPAddress.Any, 0));

                byte[] probe = new byte[16];
                BinaryPrimitives.WriteUInt32BigEndian(probe, TestId);

                HashSet<uint> externalPorts = [];
                long best = long.MaxValue;

                foreach (string host in new[] { Nncs1, Nncs2 })
                {
                    foreach (int port in _nncsPorts)
                    {
                        (uint? external, long rtt, string endpoint) = await ProbeAsync(socket, probe, host, port, cancellation);
                        if (external is null)
                        {
                            continue;
                        }

                        result.ProbesAnswered++;
                        externalPorts.Add(external.Value);
                        best = Math.Min(best, rtt);
                        result.ExternalEndpoint = endpoint;
                    }
                }

                if (result.ProbesAnswered == 0)
                {
                    return result;
                }

                result.Reachable = true;
                result.LatencyMs = best;

                // One probe answering says nothing about mapping — it takes at least two
                // destinations to compare. Leave it Unknown rather than call it Open.
                result.Nat = result.ProbesAnswered < 2
                    ? NatType.Unknown
                    : externalPorts.Count == 1 ? NatType.Open : NatType.Strict;
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] NAT check failed: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// One probe: send 16 bytes, read the reply, return the external port the server saw.
        /// The reply is 4 big-endian u32: echoed id, observed source port, observed source IP,
        /// server IP.
        ///
        /// Only a reply from the exact endpoint we asked counts, and only one echoing our own test
        /// id. Both matter: the probes share a socket, so a late reply from a previous target would
        /// otherwise be read as this one's answer and the port comparison — which IS the whole
        /// classification — would compare two ports from the same destination. It also means an
        /// unsolicited datagram cannot walk in and dictate the verdict; UDP has no handshake, so
        /// anything can send us one.
        /// </summary>
        private static async Task<(uint? External, long Rtt, string Endpoint)> ProbeAsync(
            Socket socket, byte[] probe, string host, int port, CancellationToken cancellation)
        {
            try
            {
                IPEndPoint target = new(IPAddress.Parse(host), port);
                Stopwatch sw = Stopwatch.StartNew();

                await socket.SendToAsync(probe, SocketFlags.None, target, cancellation);

                byte[] buffer = new byte[64];

                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timeout.CancelAfter(ProbeTimeoutMs);

                // Keep reading until this target answers or the deadline passes: a datagram from
                // somewhere else is not an answer, and must not end the probe either.
                while (true)
                {
                    SocketReceiveFromResult received = await socket.ReceiveFromAsync(
                        buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), timeout.Token);

                    if (received.RemoteEndPoint is not IPEndPoint from
                        || !from.Address.Equals(target.Address)
                        || from.Port != target.Port)
                    {
                        continue;
                    }

                    if (received.ReceivedBytes < 16)
                    {
                        continue;
                    }

                    // The responder echoes the id back; anything else is a stale or unrelated reply.
                    if (BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(0, 4)) != TestId)
                    {
                        continue;
                    }

                    sw.Stop();

                    uint external = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(4, 4));
                    uint address = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(8, 4));

                    string endpoint = $"{new IPAddress(BinaryPrimitives.ReverseEndianness(address))}:{external}";

                    return (external, sw.ElapsedMilliseconds, endpoint);
                }
            }
            catch (OperationCanceledException)
            {
                // Timed out, or the whole check was cancelled: either way this probe has no answer.
                return (null, 0, "");
            }
            catch (Exception ex)
            {
                Logger.Debug?.Print(LogClass.Application, $"[Nextendo] probe {host}:{port} failed: {ex.Message}");

                return (null, 0, "");
            }
        }
    }
}
