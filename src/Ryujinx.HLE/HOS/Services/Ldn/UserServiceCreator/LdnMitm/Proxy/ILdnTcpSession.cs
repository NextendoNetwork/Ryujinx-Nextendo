using Ryujinx.HLE.HOS.Services.Ldn.Types;
using System;
using System.Net;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm.Proxy
{
    /// <summary>
    /// A station connected to the LDN session host, seen from the host side.
    /// Implemented by the host socket based session and by the LAN Play one.
    /// </summary>
    internal interface ILdnTcpSession : IDisposable
    {
        int NodeId { get; set; }

        NodeInfo NodeInfo { get; set; }

        bool IsConnected { get; }

        bool IsDisposed { get; }

        EndPoint RemoteEndPoint { get; }

        /// <summary>
        /// Copies the session's node id and connection state into <see cref="NodeInfo"/>.
        /// </summary>
        void OverrideInfo();

        bool SendPacket(byte[] data);

        /// <summary>
        /// Closes the session. Returns false when it was not connected.
        /// </summary>
        bool Disconnect();
    }
}
