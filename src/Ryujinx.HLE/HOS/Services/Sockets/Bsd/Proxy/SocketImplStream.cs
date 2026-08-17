using System;
using System.IO;
using System.Net.Sockets;

namespace Ryujinx.HLE.HOS.Services.Sockets.Bsd.Proxy
{
    /// <summary>
    /// A <see cref="Stream"/> over any <see cref="ISocketImpl"/>, so that code needing a stream (the SSL
    /// service, for instance) also works with the socket implementations that are not backed by a host
    /// socket, such as the LAN Play and RyuLDN ones.
    /// </summary>
    class SocketImplStream : Stream
    {
        private readonly ISocketImpl _socket;

        public SocketImplStream(ISocketImpl socket)
        {
            _socket = socket;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            int read = _socket.Receive(buffer, SocketFlags.None, out SocketError socketError);

            if (socketError != SocketError.Success)
            {
                throw new IOException($"Receive failed: {socketError}", new SocketException((int)socketError));
            }

            return read;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            while (!buffer.IsEmpty)
            {
                int sent = _socket.Send(buffer, SocketFlags.None, out SocketError socketError);

                if (socketError != SocketError.Success)
                {
                    throw new IOException($"Send failed: {socketError}", new SocketException((int)socketError));
                }

                if (sent <= 0)
                {
                    throw new IOException("The socket accepted no data.");
                }

                buffer = buffer[sent..];
            }
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
