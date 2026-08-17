using System;
using System.Collections.Generic;
using System.Net;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm.Proxy
{
    /// <summary>
    /// Transport used by <see cref="LanDiscovery"/>: either the host network stack (ldn_mitm mode) or the
    /// virtual LAN Play network interface (LAN Play mode). The ldn_mitm protocol itself is identical in
    /// both cases, only the sockets underneath differ.
    /// </summary>
    internal interface ILdnNetworkProvider : IDisposable
    {
        /// <summary>
        /// Address the emulated console presents to other LDN participants.
        /// </summary>
        IPAddress LocalAddress { get; }

        /// <summary>
        /// Address used to send LDN scan requests to every participant.
        /// </summary>
        IPAddress BroadcastAddress { get; }

        /// <summary>
        /// Creates the sockets used for scanning. The first one is used to send scan requests.
        /// </summary>
        IReadOnlyList<ILdnUdpSocket> CreateUdpSockets(LanProtocol protocol, int port);

        ILdnTcpSocket CreateTcpServer(LanProtocol protocol, IPAddress address, int port);

        ILdnTcpSocket CreateTcpClient(LanProtocol protocol, IPAddress address, int port);
    }
}
