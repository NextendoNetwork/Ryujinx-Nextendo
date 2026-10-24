using System;

namespace Ryujinx.HLE
{
    /// <summary>
    /// Ultimate Chicken Horse (0100FCF002A58000) online support. Everything here is scoped to that one title:
    /// other games never take any of these paths.
    /// </summary>
    internal static class UchOnline
    {
        public const ulong Title = 0x0100FCF002A58000;

        // The brainCloud RPC host and the three regional allocation hosts the game contacts. Their DNS
        // redirect lives in DnsMitmResolver next to the other Nextendo hosts.
        private static readonly string[] _hosts =
        {
            "api.braincloudservers.com",
            "na.ultimatechickenhorseserver.com",
            "eu.ultimatechickenhorseserver.com",
            "ap.ultimatechickenhorseserver.com",
        };

        public static bool IsTitle(ulong programId) => programId == Title;

        public static bool IsServiceHost(string host)
        {
            if (string.IsNullOrEmpty(host))
            {
                return false;
            }

            foreach (string candidate in _hosts)
            {
                if (host.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// UCH opens its UNET transport socket with the selector 0x10000000 and protocol IP, which the BSD layer
        /// cannot map to a socket type. It is a datagram socket: report UDP for exactly that selector.
        /// </summary>
        public static bool IsUdpSelector(ulong programId, int requestedType) =>
            IsTitle(programId) && requestedType == 0x10000000;
    }
}
