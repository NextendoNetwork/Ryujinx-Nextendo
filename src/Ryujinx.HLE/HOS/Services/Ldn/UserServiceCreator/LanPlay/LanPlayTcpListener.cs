using Ryujinx.Common.Logging;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay
{
    /// <summary>
    /// A listening TCP port on the virtual network interface: turns incoming SYNs from the LAN Play
    /// network into <see cref="LanPlayTcpConnection"/> instances waiting to be accepted.
    /// </summary>
    class LanPlayTcpListener
    {
        private const int MaxPendingConnections = 16;

        private readonly LanPlayNetworkInterface _networkInterface;
        private readonly List<LanPlayTcpConnection> _pending = [];
        private readonly Lock _lock = new();
        private readonly AutoResetEvent _connectionPending = new(false);

        private bool _closed;

        public ushort LocalPort { get; }

        public IPEndPoint LocalEndPoint => new(_networkInterface.Address, LocalPort);

        /// <summary>
        /// Raised once a connection is established. When a handler is attached the connection is handed to
        /// it instead of being queued for <see cref="Accept"/>, which is what the event driven LDN
        /// discovery server needs.
        /// </summary>
        public event Action<LanPlayTcpConnection> ConnectionAccepted;

        public bool Readable
        {
            get
            {
                lock (_lock)
                {
                    foreach (LanPlayTcpConnection connection in _pending)
                    {
                        if (connection.Connected)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        public LanPlayTcpListener(LanPlayNetworkInterface networkInterface, ushort localPort)
        {
            _networkInterface = networkInterface;

            LocalPort = localPort;
        }

        internal void HandleSegment(uint remoteAddress, ushort remotePort, ReadOnlySpan<byte> segment)
        {
            const byte FlagSyn = 0x02;
            const byte FlagAck = 0x10;

            byte flags = segment[13];

            if (_closed || (flags & FlagSyn) == 0 || (flags & FlagAck) != 0)
            {
                return;
            }

            uint sequence = BinaryPrimitives.ReadUInt32BigEndian(segment[4..]);

            LanPlayTcpConnection connection = new(_networkInterface, LocalPort, remoteAddress, remotePort);

            lock (_lock)
            {
                _pending.RemoveAll(pending => pending.Aborted);

                if (_pending.Count >= MaxPendingConnections)
                {
                    Logger.Warning?.Print(LogClass.ServiceLdn, $"LAN Play: backlog full, refusing connection from {connection.RemoteEndPoint}.");

                    connection.Abort();

                    return;
                }

                _pending.Add(connection);
            }

            connection.Closed += () => Remove(connection);
            connection.Established += () => Publish(connection);

            connection.AcceptSyn(sequence);
        }

        private void Publish(LanPlayTcpConnection connection)
        {
            Action<LanPlayTcpConnection> handler = ConnectionAccepted;

            if (handler == null)
            {
                _connectionPending.Set();

                return;
            }

            Remove(connection);

            handler(connection);
        }

        private void Remove(LanPlayTcpConnection connection)
        {
            lock (_lock)
            {
                _pending.Remove(connection);
            }
        }

        /// <summary>
        /// Waits for an established incoming connection. A negative timeout waits forever.
        /// </summary>
        public LanPlayTcpConnection Accept(int timeoutMilliseconds)
        {
            long deadline = timeoutMilliseconds < 0 ? long.MaxValue : Environment.TickCount64 + timeoutMilliseconds;

            while (!_closed)
            {
                lock (_lock)
                {
                    _pending.RemoveAll(pending => pending.Aborted);

                    for (int i = 0; i < _pending.Count; i++)
                    {
                        if (_pending[i].Connected)
                        {
                            LanPlayTcpConnection connection = _pending[i];

                            _pending.RemoveAt(i);

                            return connection;
                        }
                    }
                }

                if (Environment.TickCount64 >= deadline)
                {
                    return null;
                }

                _connectionPending.WaitOne(20);
            }

            return null;
        }

        public void Close()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;

            _networkInterface.RemoveListener(this);

            LanPlayTcpConnection[] pending;

            lock (_lock)
            {
                pending = [.. _pending];

                _pending.Clear();
            }

            foreach (LanPlayTcpConnection connection in pending)
            {
                connection.Abort();
            }

            ConnectionAccepted = null;

            _connectionPending.Set();
        }
    }
}
