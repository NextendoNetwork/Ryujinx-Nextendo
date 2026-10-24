using System;
using System.Threading;

namespace Ryujinx.HLE.HOS.Services.Ssl.SslService
{
    /// <summary>
    /// Counts the live TLS connections of one SSL context. Each connection releases its slot exactly once when it is
    /// disposed, so the count the game reads through GetConnectionCount goes back down. The old monotonically
    /// increasing counter eventually exhausted the game's connection pool during normal request churn.
    /// </summary>
    sealed class SslConnectionTracker
    {
        private int _active;

        public uint Count => (uint)Volatile.Read(ref _active);

        public Action Register()
        {
            Interlocked.Increment(ref _active);

            int released = 0;

            return () =>
            {
                if (Interlocked.Exchange(ref released, 1) == 0)
                {
                    Interlocked.Decrement(ref _active);
                }
            };
        }
    }
}
