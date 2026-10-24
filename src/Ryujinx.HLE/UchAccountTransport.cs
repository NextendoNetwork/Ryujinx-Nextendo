using Ryujinx.HLE.HOS.Services.Sockets.Bsd;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd.Types;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;

namespace Ryujinx.HLE
{
    /// <summary>
    /// UCH admission ticket for emulators. The Nextendo UCH login response carries a short-lived NXU1 ticket and the
    /// relay endpoint; the game's own UNET connect request to that relay is preceded by one 36-byte NXU1 frame sent
    /// from the same gameplay socket, which is how the server binds the UDP peer to the verified account.
    /// The ticket is never logged or written anywhere: it lives in memory for at most 35 seconds and is replaced
    /// (and wiped) by the next login response. The frame may be re-sent for a retransmitted connect request.
    /// </summary>
    internal static class UchAccountTransport
    {
        private static readonly object _gate = new();
        private static byte[] _bootstrap;
        private static IPEndPoint _relay;
        private static long _expires;

        // UNET connection request fingerprint of UCH 1.13.13.765: type 1, version and config checksum.
        private const uint ConnectVersion = 16777984;
        private const uint ConnectConfigChecksum = 2944057210;

        public static bool Enabled(ulong programId) => UchOnline.IsTitle(programId);

        /// <summary>Forgets any stored ticket (used when the emulated process ends, and by tests).</summary>
        internal static void Clear()
        {
            lock (_gate)
            {
                if (_bootstrap != null)
                {
                    Array.Clear(_bootstrap);
                }

                _bootstrap = null;
                _relay = null;
                _expires = 0;
            }
        }

        public static void Observe(JsonElement root)
        {
            try
            {
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("nextendoTransport", out JsonElement extension) ||
                    extension.GetProperty("protocol").GetString() != "NXU1")
                {
                    return;
                }

                string ticket = extension.GetProperty("ticket").GetString();

                if (ticket == null || ticket.Length != 43)
                {
                    return;
                }

                byte[] decoded = Convert.FromBase64String(ticket.Replace('-', '+').Replace('_', '/') + "=");
                long expires = extension.GetProperty("expiresMs").GetInt64();

                if (decoded.Length != 32 ||
                    expires <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() ||
                    expires > DateTimeOffset.UtcNow.AddSeconds(35).ToUnixTimeMilliseconds())
                {
                    return;
                }

                if (!IPAddress.TryParse(extension.GetProperty("relayIP").GetString(), out IPAddress address) ||
                    address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
                    address.Equals(IPAddress.Any))
                {
                    return;
                }

                int port = extension.GetProperty("relayPort").GetInt32();

                if (port < 1 || port > 65535)
                {
                    return;
                }

                byte[] frame = new byte[36];
                frame[0] = (byte)'N';
                frame[1] = (byte)'X';
                frame[2] = (byte)'U';
                frame[3] = (byte)'1';
                decoded.CopyTo(frame, 4);
                Array.Clear(decoded);

                lock (_gate)
                {
                    if (_bootstrap != null)
                    {
                        Array.Clear(_bootstrap);
                    }

                    _bootstrap = frame;
                    _relay = new IPEndPoint(address, port);
                    _expires = expires;
                }
            }
            catch (Exception e) when (e is JsonException || e is InvalidOperationException ||
                                      e is KeyNotFoundException || e is FormatException)
            {
                // A malformed extension is simply not used.
            }
        }

        /// <summary>
        /// Sends the NXU1 frame in front of the game's UNET connect request to the relay. Returns true when the
        /// packet was the connect request (the caller then skips the original send only when an error is reported).
        /// </summary>
        public static bool Bootstrap(ulong programId, ISocket socket, ReadOnlySpan<byte> packet,
                                     BsdSocketFlags flags, IPEndPoint destination, out LinuxError error)
        {
            error = LinuxError.SUCCESS;

            if (!Enabled(programId) || packet.Length != 19 || packet[0] != 0 || packet[1] != 0 || packet[2] != 1 ||
                BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(11, 4)) != ConnectVersion ||
                BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(15, 4)) != ConnectConfigChecksum)
            {
                return false;
            }

            byte[] frame;

            lock (_gate)
            {
                IPAddress address = destination.Address.IsIPv4MappedToIPv6 ? destination.Address.MapToIPv4() : destination.Address;

                if (_bootstrap == null || _expires <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() ||
                    destination.Port != _relay.Port || !address.Equals(_relay.Address))
                {
                    error = LinuxError.EACCES;

                    return true;
                }

                frame = (byte[])_bootstrap.Clone();
            }

            error = socket.SendTo(out int sent, frame, frame.Length, flags, destination);
            Array.Clear(frame);

            if (error == LinuxError.SUCCESS && sent != 36)
            {
                error = LinuxError.EIO;
            }

            return true;
        }
    }
}
