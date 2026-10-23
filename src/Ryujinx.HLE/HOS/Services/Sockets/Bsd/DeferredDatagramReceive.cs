using Ryujinx.HLE.HOS.Services.Sockets.Bsd.Types;
using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Ryujinx.HLE.HOS.Services.Sockets.Bsd
{
    // Keep the guest's blocking receive pending without blocking the shared BSD server.
    internal static class DeferredDatagramReceive
    {
        internal static LinuxError Receive(ISocket socket, Span<byte> buffer, BsdSocketFlags flags,
            out int size, out IPEndPoint endpoint, out bool pending)
        {
            bool defer = socket.SocketType == SocketType.Dgram && socket.Blocking &&
                !flags.HasFlag(BsdSocketFlags.DontWait);
            LinuxError error = socket.ReceiveFrom(out size, buffer, buffer.Length,
                defer ? flags | BsdSocketFlags.DontWait : flags, out endpoint);
            pending = defer && error == LinuxError.EWOULDBLOCK;
            return error;
        }

        internal static long Deadline(ISocket socket, long now)
        {
            Span<byte> value = stackalloc byte[sizeof(int)];
            value.Clear();
            LinuxError error = socket.GetSocketOption(BsdSocketOption.SoRcvTimeo, SocketOptionLevel.Socket, value);
            int timeoutMs = BinaryPrimitives.ReadInt32LittleEndian(value);
            return error == LinuxError.SUCCESS && timeoutMs > 0 ? now + timeoutMs : long.MaxValue;
        }
    }
}
