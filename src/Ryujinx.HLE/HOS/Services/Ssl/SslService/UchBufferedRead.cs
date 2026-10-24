using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.HLE.HOS.Services.Ssl.SslService
{
    /// <summary>
    /// Non-blocking TLS reads for UCH: poll for plaintext completion instead of polling the encrypted socket, so the
    /// game's callback completes when data was already decrypted.
    /// </summary>
    internal sealed class UchBufferedRead : IDisposable
    {
        private readonly Stream _stream;
        private readonly byte[] _buffer = new byte[16384];
        private readonly CancellationTokenSource _stop = new();
        private Task<int> _pending;
        private int _offset;
        private int _count;
        private bool _eof;

        public UchBufferedRead(Stream stream) => _stream = stream;

        public int? Read(Memory<byte> destination, bool peek = false)
        {
            if (destination.IsEmpty)
            {
                return 0;
            }

            if (_count == 0 && !_eof)
            {
                _pending ??= _stream.ReadAsync(_buffer.AsMemory(), _stop.Token).AsTask();

                if (!_pending.IsCompleted)
                {
                    return null;
                }

                Task<int> completed = _pending;
                _pending = null;
                _count = completed.GetAwaiter().GetResult();
                _offset = 0;
                _eof = _count == 0;
            }

            int length = Math.Min(destination.Length, _count);
            _buffer.AsMemory(_offset, length).CopyTo(destination);

            if (!peek)
            {
                _offset += length;
                _count -= length;
            }

            return length;
        }

        public int Pending => _count;

        public void Dispose()
        {
            _stop.Cancel();

            // Observe cancellation/failure even if the guest closes without another read.
            if (_pending != null)
            {
                _ = _pending.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            }

            _stop.Dispose();
        }
    }
}
