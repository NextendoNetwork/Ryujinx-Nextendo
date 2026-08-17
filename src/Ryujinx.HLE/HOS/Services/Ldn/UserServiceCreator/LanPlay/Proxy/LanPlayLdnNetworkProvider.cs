using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm.Proxy;
using System.Collections.Generic;
using System.Net;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay.Proxy
{
    /// <summary>
    /// LDN transport of the LAN Play mode: the ldn_mitm protocol carried by the virtual network interface,
    /// so discovery and session traffic reach the relay as IPv4 packets on the 10.13.0.0/16 network.
    /// </summary>
    internal class LanPlayLdnNetworkProvider : ILdnNetworkProvider
    {
        private readonly LanPlayNetworkInterface _networkInterface;

        public IPAddress LocalAddress => _networkInterface.Address;

        public IPAddress BroadcastAddress => _networkInterface.BroadcastAddress;

        public LanPlayLdnNetworkProvider(LanPlayNetworkInterface networkInterface)
        {
            _networkInterface = networkInterface;
        }

        public IReadOnlyList<ILdnUdpSocket> CreateUdpSockets(LanProtocol protocol, int port)
        {
            // One socket is enough: the virtual interface delivers unicast and broadcast alike, with none
            // of the host platform quirks that make ldn_mitm bind twice.
            return [new LanPlayLdnUdpSocket(protocol, _networkInterface, port)];
        }

        public ILdnTcpSocket CreateTcpServer(LanProtocol protocol, IPAddress address, int port)
        {
            return new LanPlayLdnTcpServer(protocol, _networkInterface, port);
        }

        public ILdnTcpSocket CreateTcpClient(LanProtocol protocol, IPAddress address, int port)
        {
            return new LanPlayLdnTcpClient(protocol, _networkInterface, address, port);
        }

        public void Dispose()
        {
        }
    }
}
