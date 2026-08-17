namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm.Proxy
{
    internal interface ILdnTcpSocket : ILdnSocket
    {
        /// <summary>
        /// True when this socket hosts the LDN session (it listens), false when it joins one.
        /// </summary>
        bool IsServer { get; }

        bool Connect();
        void DisconnectAndStop();
    }
}
