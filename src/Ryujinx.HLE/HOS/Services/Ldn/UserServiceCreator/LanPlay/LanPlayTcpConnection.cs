using Ryujinx.Common.Logging;
using Ryujinx.Common.Utilities;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay
{
    enum LanPlayTcpState
    {
        Closed,
        SynSent,
        SynReceived,
        Established,
        FinWait1,
        FinWait2,
        CloseWait,
        LastAck,
        TimeWait,
    }

    /// <summary>
    /// A TCP connection carried over the LAN Play relay.
    /// <para>
    /// Only what LAN traffic between consoles needs is implemented: the three way handshake in both
    /// directions, in order delivery with cumulative acknowledgements, a single retransmission timer, and
    /// orderly or abortive close. Out of order segments are dropped and recovered by the sender's
    /// retransmission, which is acceptable on a relayed LAN but is not a general purpose TCP.
    /// </para>
    /// </summary>
    class LanPlayTcpConnection
    {
        public const int HeaderSize = 20;

        private const byte FlagFin = 0x01;
        private const byte FlagSyn = 0x02;
        private const byte FlagRst = 0x04;
        private const byte FlagPsh = 0x08;
        private const byte FlagAck = 0x10;

        private const int MaxSegmentSize = 1400;
        private const int ReceiveBufferSize = 64 * 1024;
        private const int SendBufferSize = 64 * 1024;

        private const int InitialRetransmitTimeoutMs = 300;
        private const int MaxRetransmitTimeoutMs = 4000;
        private const int MaxRetransmits = 10;
        private const int TimeWaitMs = 2000;

        private readonly LanPlayNetworkInterface _networkInterface;
        private readonly Lock _lock = new();
        private readonly AutoResetEvent _stateChanged = new(false);
        private readonly AutoResetEvent _dataAvailable = new(false);

        private readonly byte[] _receiveBuffer = new byte[ReceiveBufferSize];
        private int _receiveHead;
        private int _receiveLength;

        private readonly List<byte> _sendBuffer = [];

        private uint _sndUna;
        private uint _sndNxt;
        private uint _rcvNxt;
        private bool _finSent;
        private bool _finAcked;

        private long _retransmitDeadline;
        private int _retransmitTimeout = InitialRetransmitTimeoutMs;
        private int _retransmitCount;
        private long _timeWaitDeadline;

        public ushort LocalPort { get; }

        public uint RemoteAddress { get; }

        public ushort RemotePort { get; }

        public LanPlayTcpState State { get; private set; } = LanPlayTcpState.Closed;

        public IPEndPoint LocalEndPoint => new(_networkInterface.Address, LocalPort);

        public IPEndPoint RemoteEndPoint => new(NetworkHelpers.ConvertUint(RemoteAddress), RemotePort);

        public bool Connected => State is LanPlayTcpState.Established or LanPlayTcpState.CloseWait;

        /// <summary>
        /// True once the peer has closed its side of the connection, or the connection is gone.
        /// </summary>
        public bool RemoteClosed { get; private set; }

        public bool Aborted { get; private set; }

        public int Available
        {
            get
            {
                lock (_lock)
                {
                    return _receiveLength;
                }
            }
        }

        public bool Readable
        {
            get
            {
                lock (_lock)
                {
                    return _receiveLength > 0 || RemoteClosed || Aborted;
                }
            }
        }

        public bool Writable => Connected;

        /// <summary>
        /// When set, received payloads are handed to this callback instead of being buffered for
        /// <see cref="Receive"/>. Used by the LDN discovery sockets, which are event driven.
        /// </summary>
        public event Action<byte[]> DataReceived;

        /// <summary>
        /// Raised once when the handshake completes.
        /// </summary>
        public event Action Established;

        /// <summary>
        /// Raised once when the connection is closed or aborted.
        /// </summary>
        public event Action Closed;

        public LanPlayTcpConnection(LanPlayNetworkInterface networkInterface, ushort localPort, uint remoteAddress, ushort remotePort)
        {
            _networkInterface = networkInterface;

            LocalPort = localPort;
            RemoteAddress = remoteAddress;
            RemotePort = remotePort;

            _sndUna = _sndNxt = (uint)Random.Shared.Next();
        }

        #region Sequence number helpers

        private static bool SeqLeq(uint a, uint b) => (int)(a - b) <= 0;

        private static bool SeqGt(uint a, uint b) => (int)(a - b) > 0;

        #endregion

        /// <summary>
        /// Performs an active open. Returns false if the peer never answered or refused the connection.
        /// </summary>
        public bool Connect(int timeoutMilliseconds)
        {
            lock (_lock)
            {
                if (State != LanPlayTcpState.Closed)
                {
                    throw new SocketException((int)SocketError.IsConnected);
                }

                State = LanPlayTcpState.SynSent;
            }

            _networkInterface.RegisterConnection(this);

            SendSegment(FlagSyn, ReadOnlySpan<byte>.Empty);

            long deadline = Environment.TickCount64 + (timeoutMilliseconds < 0 ? 10000 : timeoutMilliseconds);

            while (Environment.TickCount64 < deadline)
            {
                lock (_lock)
                {
                    if (State == LanPlayTcpState.Established)
                    {
                        return true;
                    }

                    if (Aborted || State == LanPlayTcpState.Closed)
                    {
                        return false;
                    }
                }

                _stateChanged.WaitOne(50);
            }

            Abort();

            return false;
        }

        /// <summary>
        /// Completes a passive open: answers an incoming SYN with SYN|ACK.
        /// </summary>
        internal void AcceptSyn(uint remoteSequence)
        {
            lock (_lock)
            {
                _rcvNxt = remoteSequence + 1;
                State = LanPlayTcpState.SynReceived;
            }

            _networkInterface.RegisterConnection(this);

            SendSegment((byte)(FlagSyn | FlagAck), ReadOnlySpan<byte>.Empty);
        }

        public int Send(ReadOnlySpan<byte> data)
        {
            lock (_lock)
            {
                if (Aborted)
                {
                    throw new SocketException((int)SocketError.ConnectionReset);
                }

                if (!Connected)
                {
                    throw new SocketException((int)SocketError.NotConnected);
                }

                int room = SendBufferSize - _sendBuffer.Count;

                if (room <= 0)
                {
                    throw new SocketException((int)SocketError.WouldBlock);
                }

                int length = Math.Min(room, data.Length);

                for (int i = 0; i < length; i++)
                {
                    _sendBuffer.Add(data[i]);
                }

                TransmitPending();

                return length;
            }
        }

        /// <summary>
        /// Copies received bytes into the buffer, waiting up to the timeout for data to arrive.
        /// Returns 0 when the peer closed the connection.
        /// </summary>
        public int Receive(Span<byte> buffer, int timeoutMilliseconds, bool peek, bool blocking)
        {
            while (true)
            {
                lock (_lock)
                {
                    if (_receiveLength > 0)
                    {
                        return ReadFromBuffer(buffer, peek);
                    }

                    if (Aborted)
                    {
                        throw new SocketException((int)SocketError.ConnectionReset);
                    }

                    if (RemoteClosed)
                    {
                        return 0;
                    }

                    if (!blocking)
                    {
                        throw new SocketException((int)SocketError.WouldBlock);
                    }
                }

                if (!_dataAvailable.WaitOne(timeoutMilliseconds <= 0 ? Timeout.Infinite : timeoutMilliseconds))
                {
                    throw new SocketException((int)SocketError.TimedOut);
                }
            }
        }

        private int ReadFromBuffer(Span<byte> buffer, bool peek)
        {
            int length = Math.Min(buffer.Length, _receiveLength);

            for (int i = 0; i < length; i++)
            {
                buffer[i] = _receiveBuffer[(_receiveHead + i) % ReceiveBufferSize];
            }

            if (!peek)
            {
                _receiveHead = (_receiveHead + length) % ReceiveBufferSize;
                _receiveLength -= length;

                if (_receiveLength > 0)
                {
                    _dataAvailable.Set();
                }
            }

            return length;
        }

        /// <summary>
        /// Orderly close: sends FIN after everything that was queued and lets the peer finish.
        /// </summary>
        public void Close()
        {
            lock (_lock)
            {
                switch (State)
                {
                    case LanPlayTcpState.Established:
                        State = LanPlayTcpState.FinWait1;
                        break;

                    case LanPlayTcpState.CloseWait:
                        State = LanPlayTcpState.LastAck;
                        break;

                    case LanPlayTcpState.SynSent:
                    case LanPlayTcpState.SynReceived:
                        break;

                    default:
                        return;
                }

                if (State is LanPlayTcpState.SynSent or LanPlayTcpState.SynReceived)
                {
                    // Nothing was established yet, so there is nothing to shut down gracefully.
                }
                else
                {
                    _finSent = true;

                    SendSegmentLocked((byte)(FlagFin | FlagAck), ReadOnlySpan<byte>.Empty, _sndNxt);

                    _retransmitDeadline = Environment.TickCount64 + _retransmitTimeout;

                    return;
                }
            }

            Abort();
        }

        /// <summary>
        /// Abortive close: sends RST (if the connection was live) and forgets everything.
        /// </summary>
        public void Abort()
        {
            bool sendReset;

            lock (_lock)
            {
                if (Aborted)
                {
                    return;
                }

                sendReset = State is LanPlayTcpState.Established or LanPlayTcpState.CloseWait
                    or LanPlayTcpState.SynReceived or LanPlayTcpState.FinWait1 or LanPlayTcpState.FinWait2;

                Aborted = true;
                RemoteClosed = true;
                State = LanPlayTcpState.Closed;
            }

            if (sendReset)
            {
                SendSegment((byte)(FlagRst | FlagAck), ReadOnlySpan<byte>.Empty);
            }

            Cleanup();
        }

        private void Cleanup()
        {
            _networkInterface.UnregisterConnection(this);

            _stateChanged.Set();
            _dataAvailable.Set();

            Action closed = Closed;
            Closed = null;

            closed?.Invoke();
        }

        internal void HandleSegment(ReadOnlySpan<byte> segment)
        {
            if (segment.Length < HeaderSize)
            {
                return;
            }

            int dataOffset = ((segment[12] >> 4) & 0xF) * 4;

            if (dataOffset < HeaderSize || dataOffset > segment.Length)
            {
                return;
            }

            uint sequence = BinaryPrimitives.ReadUInt32BigEndian(segment[4..]);
            uint acknowledgement = BinaryPrimitives.ReadUInt32BigEndian(segment[8..]);
            byte flags = segment[13];
            ReadOnlySpan<byte> data = segment[dataOffset..];

            if ((flags & FlagRst) != 0)
            {
                _networkInterface.Diagnostics.TcpReset();

                Logger.Debug?.Print(LogClass.ServiceLdn, $"LAN Play: TCP connection to {RemoteEndPoint} was reset by the peer in state {State}.");

                Abort();

                return;
            }

            byte[] pushedData = null;
            bool sendAck = false;
            bool finished = false;
            bool established = false;

            lock (_lock)
            {
                if (State == LanPlayTcpState.SynSent)
                {
                    if ((flags & FlagSyn) == 0 || (flags & FlagAck) == 0 || acknowledgement != _sndNxt + 1)
                    {
                        return;
                    }

                    _sndUna = _sndNxt = acknowledgement;
                    _rcvNxt = sequence + 1;
                    State = LanPlayTcpState.Established;
                    established = true;

                    ResetRetransmitTimer();
                    SendAckLocked();

                    _stateChanged.Set();
                }
                else if (State == LanPlayTcpState.SynReceived)
                {
                    if ((flags & FlagAck) == 0 || acknowledgement != _sndNxt + 1)
                    {
                        return;
                    }

                    _sndUna = _sndNxt = acknowledgement;
                    State = LanPlayTcpState.Established;
                    established = true;

                    ResetRetransmitTimer();

                    _stateChanged.Set();
                }
                else if ((flags & FlagAck) != 0)
                {
                    HandleAcknowledgementLocked(acknowledgement);
                }

                if (data.Length > 0)
                {
                    if (sequence != _rcvNxt)
                    {
                        // Out of order: dropped on purpose, the peer will send it again.
                        _networkInterface.Diagnostics.Dropped(LanPlayDropReason.Malformed, $"out of order tcp segment from {RemoteEndPoint}");
                    }

                    if (sequence == _rcvNxt)
                    {
                        if (DataReceived != null)
                        {
                            pushedData = data.ToArray();
                            _rcvNxt += (uint)data.Length;
                        }
                        else
                        {
                            int accepted = Math.Min(data.Length, ReceiveBufferSize - _receiveLength);

                            if (accepted < data.Length)
                            {
                                _networkInterface.Diagnostics.Dropped(LanPlayDropReason.QueueFull, $"tcp receive buffer from {RemoteEndPoint}");
                            }

                            for (int i = 0; i < accepted; i++)
                            {
                                _receiveBuffer[(_receiveHead + _receiveLength + i) % ReceiveBufferSize] = data[i];
                            }

                            _receiveLength += accepted;
                            _rcvNxt += (uint)accepted;

                            _dataAvailable.Set();
                        }
                    }

                    // Anything out of order (or that did not fit) is dropped: the peer will resend it.
                    sendAck = true;
                }

                if ((flags & FlagFin) != 0 && sequence + (uint)data.Length == _rcvNxt)
                {
                    _rcvNxt++;
                    RemoteClosed = true;
                    sendAck = true;

                    _dataAvailable.Set();

                    switch (State)
                    {
                        case LanPlayTcpState.Established:
                            State = LanPlayTcpState.CloseWait;
                            break;

                        case LanPlayTcpState.FinWait1:
                        case LanPlayTcpState.FinWait2:
                            State = LanPlayTcpState.TimeWait;
                            _timeWaitDeadline = Environment.TickCount64 + TimeWaitMs;
                            break;
                    }
                }

                if (_finAcked)
                {
                    switch (State)
                    {
                        case LanPlayTcpState.FinWait1:
                            State = LanPlayTcpState.FinWait2;
                            break;

                        case LanPlayTcpState.LastAck:
                            State = LanPlayTcpState.Closed;
                            finished = true;
                            break;
                    }
                }
            }

            if (established)
            {
                Logger.Debug?.Print(LogClass.ServiceLdn, $"LAN Play: TCP connection established with {RemoteEndPoint} (local port {LocalPort}).");

                Established?.Invoke();
            }

            if (pushedData != null)
            {
                DataReceived?.Invoke(pushedData);
            }

            if (sendAck)
            {
                SendSegment(FlagAck, ReadOnlySpan<byte>.Empty);
            }

            if (finished)
            {
                Cleanup();
            }
        }

        private void HandleAcknowledgementLocked(uint acknowledgement)
        {
            if (SeqLeq(acknowledgement, _sndUna) || SeqGt(acknowledgement, _sndNxt + (uint)(_finSent ? 1 : 0)))
            {
                return;
            }

            uint acked = acknowledgement - _sndUna;

            if (_finSent && acknowledgement == _sndNxt + 1)
            {
                _finAcked = true;
                _sndNxt = acknowledgement;
                acked--;
            }

            if (acked > 0)
            {
                int count = (int)Math.Min(acked, (uint)_sendBuffer.Count);

                _sendBuffer.RemoveRange(0, count);
                _sndUna += (uint)count;
            }
            else if (_finAcked)
            {
                _sndUna = acknowledgement;
            }

            ResetRetransmitTimer();

            TransmitPending();
        }

        private void ResetRetransmitTimer()
        {
            _retransmitCount = 0;
            _retransmitTimeout = InitialRetransmitTimeoutMs;
            _retransmitDeadline = _sendBuffer.Count > 0 || _finSent
                ? Environment.TickCount64 + _retransmitTimeout
                : 0;
        }

        /// <summary>
        /// Sends everything that is queued but not yet on the wire. Must be called with the lock held.
        /// The peer's window is not used for pacing: a segment the peer had no room for is simply
        /// retransmitted, which keeps the send side (and therefore the FIN sequence number) trivial.
        /// </summary>
        private void TransmitPending()
        {
            if (_sendBuffer.Count == 0)
            {
                return;
            }

            int inFlight = (int)(_sndNxt - _sndUna);

            while (inFlight < _sendBuffer.Count)
            {
                int length = Math.Min(_sendBuffer.Count - inFlight, MaxSegmentSize);
                byte[] payload = new byte[length];

                _sendBuffer.CopyTo(inFlight, payload, 0, length);

                SendSegmentLocked((byte)(FlagAck | FlagPsh), payload, _sndNxt);

                _sndNxt += (uint)length;
                inFlight += length;
            }

            if (_sendBuffer.Count > 0 && _retransmitDeadline == 0)
            {
                _retransmitDeadline = Environment.TickCount64 + _retransmitTimeout;
            }
        }

        internal void Tick()
        {
            bool retransmitSyn = false;
            bool retransmitFin = false;
            byte[] retransmitData = null;
            uint retransmitSequence = 0;
            bool giveUp = false;
            bool finished = false;

            lock (_lock)
            {
                if (State == LanPlayTcpState.TimeWait && Environment.TickCount64 >= _timeWaitDeadline)
                {
                    State = LanPlayTcpState.Closed;
                    finished = true;
                }
                else if (_retransmitDeadline != 0 && Environment.TickCount64 >= _retransmitDeadline)
                {
                    if (++_retransmitCount > MaxRetransmits)
                    {
                        giveUp = true;
                    }
                    else
                    {
                        _retransmitTimeout = Math.Min(_retransmitTimeout * 2, MaxRetransmitTimeoutMs);
                        _retransmitDeadline = Environment.TickCount64 + _retransmitTimeout;

                        switch (State)
                        {
                            case LanPlayTcpState.SynSent:
                                retransmitSyn = true;
                                break;

                            case LanPlayTcpState.SynReceived:
                                retransmitSyn = true;
                                break;

                            default:
                                if (_sendBuffer.Count > 0)
                                {
                                    int length = Math.Min(_sendBuffer.Count, MaxSegmentSize);

                                    retransmitData = new byte[length];
                                    _sendBuffer.CopyTo(0, retransmitData, 0, length);
                                    retransmitSequence = _sndUna;
                                }
                                else if (_finSent && !_finAcked)
                                {
                                    retransmitFin = true;
                                }
                                break;
                        }
                    }
                }
            }

            if (giveUp)
            {
                Logger.Warning?.Print(LogClass.ServiceLdn,
                    $"LAN Play: TCP connection to {RemoteEndPoint} timed out after {MaxRetransmits} retransmissions in state {State}.");

                Abort();

                return;
            }

            if (retransmitSyn || retransmitData != null || retransmitFin)
            {
                _networkInterface.Diagnostics.TcpRetransmit(_retransmitCount);
            }

            if (retransmitSyn)
            {
                SendSegment(State == LanPlayTcpState.SynSent ? FlagSyn : (byte)(FlagSyn | FlagAck), ReadOnlySpan<byte>.Empty);
            }
            else if (retransmitData != null)
            {
                lock (_lock)
                {
                    SendSegmentLocked((byte)(FlagAck | FlagPsh), retransmitData, retransmitSequence);
                }
            }
            else if (retransmitFin)
            {
                SendSegment((byte)(FlagFin | FlagAck), ReadOnlySpan<byte>.Empty);
            }

            if (finished)
            {
                Cleanup();
            }
        }

        private void SendAckLocked()
        {
            SendSegmentLocked(FlagAck, ReadOnlySpan<byte>.Empty, _sndNxt);
        }

        private void SendSegment(byte flags, ReadOnlySpan<byte> payload)
        {
            lock (_lock)
            {
                SendSegmentLocked(flags, payload, _sndNxt);

                // SYN and FIN each consume a sequence number and have to be retransmitted until acked.
                if ((flags & (FlagSyn | FlagFin)) != 0)
                {
                    _retransmitDeadline = Environment.TickCount64 + _retransmitTimeout;
                }
            }
        }

        private void SendSegmentLocked(byte flags, ReadOnlySpan<byte> payload, uint sequence)
        {
            byte[] segment = new byte[HeaderSize + payload.Length];

            BinaryPrimitives.WriteUInt16BigEndian(segment, LocalPort);
            BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), RemotePort);
            BinaryPrimitives.WriteUInt32BigEndian(segment.AsSpan(4), sequence);
            BinaryPrimitives.WriteUInt32BigEndian(segment.AsSpan(8), _rcvNxt);

            segment[12] = 5 << 4;
            segment[13] = flags;

            BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(14), (ushort)Math.Min(ReceiveBufferSize - _receiveLength, ushort.MaxValue));

            payload.CopyTo(segment.AsSpan(HeaderSize));

            BinaryPrimitives.WriteUInt16BigEndian(
                segment.AsSpan(16),
                Ipv4Packet.TransportChecksum(_networkInterface.AddressV4, RemoteAddress, Ipv4Packet.ProtocolTcp, segment));

            _networkInterface.SendIpv4(RemoteAddress, Ipv4Packet.ProtocolTcp, segment);
        }

        /// <summary>
        /// Answers a segment that belongs to no connection, the way a real stack refuses a connection.
        /// </summary>
        internal static void SendReset(LanPlayNetworkInterface networkInterface, ushort localPort, uint remoteAddress, ushort remotePort, ReadOnlySpan<byte> incoming)
        {
            byte incomingFlags = incoming[13];

            if ((incomingFlags & FlagRst) != 0)
            {
                return;
            }

            uint incomingSequence = BinaryPrimitives.ReadUInt32BigEndian(incoming[4..]);
            uint incomingAck = BinaryPrimitives.ReadUInt32BigEndian(incoming[8..]);
            int dataOffset = ((incoming[12] >> 4) & 0xF) * 4;
            int dataLength = Math.Max(incoming.Length - dataOffset, 0);

            byte[] segment = new byte[HeaderSize];

            BinaryPrimitives.WriteUInt16BigEndian(segment, localPort);
            BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), remotePort);

            if ((incomingFlags & FlagAck) != 0)
            {
                BinaryPrimitives.WriteUInt32BigEndian(segment.AsSpan(4), incomingAck);
                segment[13] = FlagRst;
            }
            else
            {
                BinaryPrimitives.WriteUInt32BigEndian(segment.AsSpan(8), incomingSequence + (uint)dataLength + (uint)((incomingFlags & FlagSyn) != 0 ? 1 : 0));
                segment[13] = FlagRst | FlagAck;
            }

            segment[12] = 5 << 4;

            BinaryPrimitives.WriteUInt16BigEndian(
                segment.AsSpan(16),
                Ipv4Packet.TransportChecksum(networkInterface.AddressV4, remoteAddress, Ipv4Packet.ProtocolTcp, segment));

            networkInterface.SendIpv4(remoteAddress, Ipv4Packet.ProtocolTcp, segment);
        }
    }
}
