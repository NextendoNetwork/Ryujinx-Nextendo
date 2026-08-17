using Ryujinx.HLE.HOS.Services.Ldn.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm.Proxy
{
    /// <summary>
    /// Bookkeeping of the scan responses received since the last scan, shared by every LDN UDP socket
    /// implementation.
    /// </summary>
    internal class LdnScanResults
    {
        private const long ScanFrequency = 1000;

        private readonly Lock _lock = new();
        private readonly AutoResetEvent _scanResponse = new(false);

        private Dictionary<ulong, NetworkInfo> _previous = new();
        private Dictionary<ulong, NetworkInfo> _current = new();
        private long _lastScanTime;

        public void Add(NetworkInfo info)
        {
            Span<byte> mac = stackalloc byte[8];

            info.Common.MacAddress.AsSpan().CopyTo(mac);

            lock (_lock)
            {
                _current[BitConverter.ToUInt64(mac)] = info;

                _scanResponse.Set();
            }
        }

        public void Clear()
        {
            // Rate limit scans.

            long timeMs = Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000);
            long delay = ScanFrequency - (timeMs - _lastScanTime);

            if (delay > 0)
            {
                Thread.Sleep((int)delay);
            }

            _lastScanTime = timeMs;

            lock (_lock)
            {
                Dictionary<ulong, NetworkInfo> newResults = _previous;
                newResults.Clear();

                _previous = _current;
                _current = newResults;

                _scanResponse.Reset();
            }
        }

        public Dictionary<ulong, NetworkInfo> Get()
        {
            // NOTE: Try to minimize waiting time for scan results.
            // After we receive the first response, wait a short time for follow-ups and return.
            // Responses that were too late to catch will appear in the next scan.

            // ldn_mitm does not do this, but this improves latency for games that expect it to be low (it is on console).

            if (_scanResponse.WaitOne(1000))
            {
                // Wait a short while longer in case there are some other responses.
                Thread.Sleep(33);
            }

            lock (_lock)
            {
                Dictionary<ulong, NetworkInfo> results = new();

                foreach (KeyValuePair<ulong, NetworkInfo> last in _previous)
                {
                    results[last.Key] = last.Value;
                }

                foreach (KeyValuePair<ulong, NetworkInfo> scan in _current)
                {
                    results[scan.Key] = scan.Value;
                }

                return results;
            }
        }

        public void Dispose()
        {
            _scanResponse.Dispose();
        }
    }
}
