namespace Ryujinx.HLE.HOS.Services.Sockets.Bsd.Proxy
{
    /// <summary>
    /// A socket implementation that is not backed by a host socket, and therefore reports its readiness
    /// itself instead of going through <see cref="System.Net.Sockets.Socket.Select"/>.
    /// </summary>
    interface IPollableSocket : ISocketImpl
    {
        bool Readable { get; }

        bool Writable { get; }

        bool Error { get; }
    }
}
