using Ryujinx.Common.Logging;
using Ryujinx.Common.Utilities;
using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay
{
    /// <summary>
    /// Settings needed to join a LAN Play relay, parsed from the user facing server string.
    /// </summary>
    class LanPlayConfiguration
    {
        public IPEndPoint RelayEndPoint { get; private init; }

        public string UserName { get; private init; }

        public byte[] PasswordHash { get; private init; }

        /// <summary>
        /// Virtual address the emulated console should use, or null to pick one automatically.
        /// </summary>
        public IPAddress VirtualAddress { get; private init; }

        /// <summary>
        /// Maximum payload size of a relay datagram before the LAN Play fragmentation layer kicks in.
        /// 0 disables it, matching the reference client's default.
        /// </summary>
        public int PathMtu { get; private init; }

        /// <summary>
        /// Parses a server string of the form <c>[user[:password]@]host[:port]</c>.
        /// </summary>
        public static bool TryParse(string server, string virtualAddress, out LanPlayConfiguration configuration)
        {
            configuration = null;

            if (string.IsNullOrWhiteSpace(server))
            {
                Logger.Error?.Print(LogClass.ServiceLdn, "LAN Play: no relay server configured.");

                return false;
            }

            string address = server.Trim();
            string userName = null;
            byte[] passwordHash = null;

            int credentialsEnd = address.LastIndexOf('@');

            if (credentialsEnd != -1)
            {
                string credentials = address[..credentialsEnd];
                address = address[(credentialsEnd + 1)..];

                int passwordStart = credentials.IndexOf(':');

                if (passwordStart == -1)
                {
                    userName = credentials;
                }
                else
                {
                    userName = credentials[..passwordStart];
                    passwordHash = SHA1.HashData(Encoding.UTF8.GetBytes(credentials[(passwordStart + 1)..]));
                }
            }

            if (!TryParseEndPoint(address, out IPEndPoint relayEndPoint))
            {
                return false;
            }

            IPAddress parsedVirtualAddress = null;

            if (!string.IsNullOrWhiteSpace(virtualAddress) &&
                !virtualAddress.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                if (!IPAddress.TryParse(virtualAddress.Trim(), out parsedVirtualAddress) ||
                    !VirtualAddressAllocator.IsUsableAddress(NetworkHelpers.ConvertIpv4Address(parsedVirtualAddress)))
                {
                    Logger.Warning?.Print(LogClass.ServiceLdn, $"LAN Play: \"{virtualAddress}\" is not a usable 10.13.x.x address, one will be picked automatically.");

                    parsedVirtualAddress = null;
                }
            }

            configuration = new LanPlayConfiguration
            {
                RelayEndPoint = relayEndPoint,
                UserName = string.IsNullOrEmpty(userName) ? null : userName,
                PasswordHash = passwordHash,
                VirtualAddress = parsedVirtualAddress,
                PathMtu = 0,
            };

            return true;
        }

        private static bool TryParseEndPoint(string address, out IPEndPoint endPoint)
        {
            endPoint = null;

            string host = address;
            int port = LanPlayProtocol.DefaultRelayPort;

            int portStart = address.LastIndexOf(':');

            // A bare IPv6 literal also contains colons, so only treat the last one as a port separator
            // when what follows it is a number and the host is not an unbracketed IPv6 address.
            if (portStart != -1 && address.IndexOf(':') == portStart)
            {
                if (!int.TryParse(address[(portStart + 1)..], out port))
                {
                    Logger.Error?.Print(LogClass.ServiceLdn, $"LAN Play: \"{address}\" has an invalid port.");

                    return false;
                }

                host = address[..portStart];
            }

            if (port is <= 0 or > ushort.MaxValue)
            {
                Logger.Error?.Print(LogClass.ServiceLdn, $"LAN Play: \"{address}\" has an invalid port.");

                return false;
            }

            if (!IPAddress.TryParse(host, out IPAddress ipAddress))
            {
                try
                {
                    IPAddress[] addresses = Dns.GetHostAddresses(host);

                    foreach (IPAddress candidate in addresses)
                    {
                        if (candidate.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            ipAddress = candidate;

                            break;
                        }
                    }

                    ipAddress ??= addresses.Length > 0 ? addresses[0] : null;
                }
                catch (Exception ex)
                {
                    Logger.Error?.Print(LogClass.ServiceLdn, $"LAN Play: could not resolve relay host \"{host}\": {ex.Message}");

                    return false;
                }
            }

            if (ipAddress == null)
            {
                Logger.Error?.Print(LogClass.ServiceLdn, $"LAN Play: could not resolve relay host \"{host}\".");

                return false;
            }

            endPoint = new IPEndPoint(ipAddress, port);

            return true;
        }
    }
}
