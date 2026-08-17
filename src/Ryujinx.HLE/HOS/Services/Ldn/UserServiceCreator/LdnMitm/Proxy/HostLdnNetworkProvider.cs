using System;
using System.Collections.Generic;
using System.Net;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm.Proxy
{
    /// <summary>
    /// LDN transport of the ldn_mitm mode: real sockets on the host network interface, so that other
    /// consoles (and tools such as switch-lan-play) see the traffic on the physical network.
    /// </summary>
    internal class HostLdnNetworkProvider : ILdnNetworkProvider
    {
        public IPAddress LocalAddress { get; }

        public IPAddress BroadcastAddress { get; }

        public HostLdnNetworkProvider(IPAddress localAddress, IPAddress subnetMask)
        {
            LocalAddress = localAddress;
            BroadcastAddress = GetBroadcastAddress(localAddress, subnetMask);
        }

        // NOTE: Credit to https://stackoverflow.com/a/39338188
        private static IPAddress GetBroadcastAddress(IPAddress address, IPAddress mask)
        {
            uint ipAddress = BitConverter.ToUInt32(address.GetAddressBytes(), 0);
            uint ipMaskV4 = BitConverter.ToUInt32(mask.GetAddressBytes(), 0);
            uint broadCastIpAddress = ipAddress | ~ipMaskV4;

            return new IPAddress(BitConverter.GetBytes(broadCastIpAddress));
        }

        public IReadOnlyList<ILdnUdpSocket> CreateUdpSockets(LanProtocol protocol, int port)
        {
            List<ILdnUdpSocket> sockets = [];

            // NOTE: Linux won't receive any broadcast packets if the socket is not bound to the broadcast address.
            //       Windows only works if bound to localhost or the local address.
            //       See this discussion: https://stackoverflow.com/questions/13666789/receiving-udp-broadcast-packets-on-linux
            ILdnUdpSocket broadcastSocket = OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
                ? new LdnProxyUdpServer(protocol, BroadcastAddress, port)
                : null;

            // The socket bound to the local address is the one used to send scan requests, so it comes first.
            sockets.Add(new LdnProxyUdpServer(protocol, LocalAddress, port));

            if (broadcastSocket != null)
            {
                sockets.Add(broadcastSocket);
            }

            return sockets;
        }

        public ILdnTcpSocket CreateTcpServer(LanProtocol protocol, IPAddress address, int port)
        {
            return new LdnProxyTcpServer(protocol, address, port);
        }

        public ILdnTcpSocket CreateTcpClient(LanProtocol protocol, IPAddress address, int port)
        {
            return new LdnProxyTcpClient(protocol, address, port);
        }

        public void Dispose()
        {
        }
    }
}
