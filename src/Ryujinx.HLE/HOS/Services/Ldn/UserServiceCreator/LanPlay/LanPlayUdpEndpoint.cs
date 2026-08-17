using Ryujinx.Common.Utilities;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay
{
    /// <summary>
    /// A UDP port bound on the virtual network interface. Datagrams are queued for pull based readers
    /// (the emulated BSD sockets) and also raised as an event for push based readers (LDN discovery).
    /// </summary>
    class LanPlayUdpEndpoint
    {
        private const int MaxQueuedDatagrams = 256;

        public readonly struct Datagram
        {
            public IPEndPoint Source { get; init; }
            public byte[] Data { get; init; }
            public bool WasBroadcast { get; init; }
        }

        private readonly LanPlayNetworkInterface _networkInterface;
        private readonly Queue<Datagram> _queue = new();
        private readonly Lock _queueLock = new();
        private readonly AutoResetEvent _dataAvailable = new(false);

        private bool _closed;

        public ushort LocalPort { get; }

        public IPEndPoint LocalEndPoint => new(_networkInterface.Address, LocalPort);

        /// <summary>
        /// Raised on the relay receive thread for every datagram. When a handler is attached, datagrams
        /// are not queued, so a single endpoint is either event driven or queue driven, not both.
        /// </summary>
        public event Action<IPEndPoint, byte[]> PacketReceived;

        public int Available
        {
            get
            {
                lock (_queueLock)
                {
                    int total = 0;

                    foreach (Datagram datagram in _queue)
                    {
                        total += datagram.Data.Length;
                    }

                    return total;
                }
            }
        }

        public bool Readable
        {
            get
            {
                lock (_queueLock)
                {
                    return _queue.Count > 0;
                }
            }
        }

        public LanPlayUdpEndpoint(LanPlayNetworkInterface networkInterface, ushort localPort)
        {
            _networkInterface = networkInterface;

            LocalPort = localPort;
        }

        internal void HandleDatagram(IPEndPoint source, uint destinationAddress, ReadOnlySpan<byte> data)
        {
            if (_closed)
            {
                return;
            }

            byte[] payload = data.ToArray();

            Action<IPEndPoint, byte[]> handler = PacketReceived;

            if (handler != null)
            {
                handler(source, payload);

                return;
            }

            lock (_queueLock)
            {
                if (_queue.Count >= MaxQueuedDatagrams)
                {
                    // The guest is not reading fast enough; drop the oldest datagram, like a real interface.
                    _queue.Dequeue();

                    _networkInterface.Diagnostics.Dropped(LanPlayDropReason.QueueFull, $"udp port {LocalPort}");
                }

                _queue.Enqueue(new Datagram
                {
                    Source = source,
                    Data = payload,
                    WasBroadcast = _networkInterface.IsBroadcast(destinationAddress),
                });
            }

            _dataAvailable.Set();
        }

        public bool TryDequeue(out Datagram datagram)
        {
            lock (_queueLock)
            {
                return _queue.TryDequeue(out datagram);
            }
        }

        public bool TryPeek(out Datagram datagram)
        {
            lock (_queueLock)
            {
                return _queue.TryPeek(out datagram);
            }
        }

        /// <summary>
        /// Waits until a datagram is queued or the timeout expires. A negative timeout waits forever.
        /// </summary>
        public bool WaitForData(int timeoutMilliseconds)
        {
            return _dataAvailable.WaitOne(timeoutMilliseconds < 0 ? Timeout.Infinite : timeoutMilliseconds);
        }

        public int SendTo(IPEndPoint destination, ReadOnlySpan<byte> data)
        {
            if (_closed)
            {
                return 0;
            }

            _networkInterface.SendUdp(LocalPort, NetworkHelpers.ConvertIpv4Address(destination.Address), (ushort)destination.Port, data);

            return data.Length;
        }

        public void Close()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;

            _networkInterface.UnbindUdp(this);

            PacketReceived = null;

            lock (_queueLock)
            {
                _queue.Clear();
            }

            // Wake up any blocked reader; the event itself is not disposed because a reader may still
            // be inside WaitForData while the socket is being closed from another thread.
            _dataAvailable.Set();
        }
    }
}
