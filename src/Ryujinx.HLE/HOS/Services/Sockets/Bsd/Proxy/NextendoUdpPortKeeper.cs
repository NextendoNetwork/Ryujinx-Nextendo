using Ryujinx.Common.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Ryujinx.HLE.HOS.Services.Sockets.Bsd.Proxy
{
    /// <summary>
    /// [Nextendo] Keeps a guest UDP socket's NAT mapping alive across the guest closing and
    /// immediately reopening the same local port.
    ///
    /// Why. Pia probes the NAT-check servers, closes that socket, and reopens one on the same
    /// local port for the actual peer-to-peer traffic:
    ///
    ///     bind Udp/55963  ->  closed 300ms later  ->  bind Udp/55963 again  (lives for the match)
    ///
    /// A NAT mapping belongs to the SOCKET, not to the local port. Closing the first socket
    /// destroys the mapping, and the second one gets a fresh external port. So the endpoint the
    /// NAT-check server observed — the one our matchmaking server hands to the peer — is dead
    /// before the peer ever probes it. The peer's probe lands nowhere and the console reports
    /// the hole-punch as failed with rtt=0, which surfaces as a communication error
    /// (2618-0006) in Splatoon 2 and Smash alike: the socket churn is Pia's, common to both.
    ///
    /// A real console does not have this problem: its socket lives in the firmware and the
    /// mapping is never torn down mid-sequence. This restores that property by parking the host
    /// socket instead of closing it, and handing the same one back when the guest rebinds the
    /// same port.
    ///
    /// Deliberately narrow: UDP only, bound-to-a-real-port only, one socket per port, and only
    /// for a few seconds. Anything else closes exactly as before.
    /// </summary>
    static class NextendoUdpPortKeeper
    {
        /// <summary>
        /// How long a closed socket is held before it is really let go.
        ///
        /// The reopen happens within ~100ms, so this only has to outlive that. It is kept short
        /// because a parked socket still owns its port: hold it too long and a guest that meant
        /// to release the port cannot rebind it from a fresh socket, and we would be inventing a
        /// bug to fix one.
        /// </summary>
        private static readonly TimeSpan _parkDuration = TimeSpan.FromSeconds(5);

        /// <summary>A cap, so a title that churns ports cannot make us hold the whole range.</summary>
        private const int MaxParked = 8;

        private sealed class Parked
        {
            public ISocketImpl Socket;
            public long ExpiresAtMs;
        }

        private static readonly Dictionary<int, Parked> _parked = [];
        private static readonly object _lock = new();

        /// <summary>
        /// Parks a socket the guest just closed. Returns true when this keeper has taken
        /// ownership — the caller must NOT close it.
        /// </summary>
        public static bool TryPark(ISocketImpl socket)
        {
            if (socket is null || socket.SocketType != SocketType.Dgram || !socket.IsBound)
            {
                return false;
            }

            // A connected UDP socket is talking to one peer; the guest closing it is a real
            // teardown, not the probe/play swap this exists for.
            if (socket.Connected)
            {
                return false;
            }

            if (socket.LocalEndPoint is not IPEndPoint local || local.Port == 0)
            {
                return false;
            }

            lock (_lock)
            {
                DropExpired();

                // Someone already holds this port: keep the newer socket, since it carries the
                // mapping the guest was last using.
                if (_parked.TryGetValue(local.Port, out Parked existing))
                {
                    existing.Socket.Dispose();
                    _parked.Remove(local.Port);
                }
                else if (_parked.Count >= MaxParked)
                {
                    return false;
                }

                _parked[local.Port] = new Parked
                {
                    Socket = socket,
                    ExpiresAtMs = Environment.TickCount64 + (long)_parkDuration.TotalMilliseconds,
                };

                Logger.Debug?.Print(LogClass.ServiceBsd,
                    $"[Nextendo] Parked udp/{local.Port} to keep its NAT mapping alive");

                return true;
            }
        }

        /// <summary>
        /// Hands back the socket previously parked on this port, if it is still held.
        /// </summary>
        public static bool TryAdopt(EndPoint localEP, out ISocketImpl socket)
        {
            socket = null;

            if (localEP is not IPEndPoint local || local.Port == 0)
            {
                return false;
            }

            lock (_lock)
            {
                DropExpired();

                if (!_parked.Remove(local.Port, out Parked parked))
                {
                    return false;
                }

                socket = parked.Socket;

                Logger.Info?.Print(LogClass.ServiceBsd,
                    $"[Nextendo] Reusing the parked udp/{local.Port} socket: its NAT mapping — the one the " +
                    "NAT-check server observed — stays valid for peer-to-peer");

                return true;
            }
        }

        /// <summary>Releases everything held. Call when emulation stops.</summary>
        public static void Clear()
        {
            lock (_lock)
            {
                foreach (Parked p in _parked.Values)
                {
                    p.Socket.Dispose();
                }

                _parked.Clear();
            }
        }

        // Caller holds _lock.
        private static void DropExpired()
        {
            long now = Environment.TickCount64;

            List<int> expired = null;
            foreach ((int port, Parked p) in _parked)
            {
                if (p.ExpiresAtMs <= now)
                {
                    (expired ??= []).Add(port);
                }
            }

            if (expired is null)
            {
                return;
            }

            foreach (int port in expired)
            {
                _parked[port].Socket.Dispose();
                _parked.Remove(port);

                Logger.Debug?.Print(LogClass.ServiceBsd, $"[Nextendo] Released parked udp/{port} (not reclaimed)");
            }
        }
    }
}
