using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Ldn.Types;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm.Proxy;
using System;
using System.Net;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay.Proxy
{
    /// <summary>
    /// A station connected to our LDN session over the LAN Play relay.
    /// </summary>
    internal class LanPlayLdnTcpSession : ILdnTcpSession
    {
        private readonly LanProtocol _protocol;
        private readonly LanPlayTcpConnection _connection;

        private byte[] _buffer = new byte[LanProtocol.BufferSize];
        private int _bufferEnd;

        public int NodeId { get; set; }

        public NodeInfo NodeInfo { get; set; }

        public bool IsConnected => _connection.Connected;

        public bool IsDisposed { get; private set; }

        public EndPoint RemoteEndPoint => _connection.RemoteEndPoint;

        public LanPlayLdnTcpSession(LanProtocol protocol, LanPlayTcpConnection connection)
        {
            _protocol = protocol;
            _connection = connection;

            _protocol.Connect += OnConnect;

            _connection.DataReceived += OnDataReceived;
            _connection.Closed += OnClosed;

            // Anything that arrived between the handshake completing and this session being created was
            // buffered by the connection, so it has to be handed to the protocol as well.
            if (_connection.Available > 0)
            {
                byte[] buffered = new byte[_connection.Available];
                int read = _connection.Receive(buffered, 0, false, false);

                if (read > 0)
                {
                    OnDataReceived(buffered[..read]);
                }
            }

            Logger.Info?.PrintMsg(LogClass.ServiceLdn, $"LAN Play: LDN station connected from {RemoteEndPoint}.");
        }

        private void OnDataReceived(byte[] data)
        {
            _protocol.Read(ref _buffer, ref _bufferEnd, data, 0, data.Length, RemoteEndPoint);
        }

        private void OnClosed()
        {
            _protocol.InvokeDisconnectStation(this);
        }

        private void OnConnect(NodeInfo info, EndPoint endPoint)
        {
            if (IsDisposed || !Equals(endPoint, RemoteEndPoint))
            {
                return;
            }

            NodeInfo = info;

            _protocol.InvokeAccept(this);
        }

        public void OverrideInfo()
        {
            NodeInfo info = NodeInfo;

            info.NodeId = (byte)NodeId;
            info.IsConnected = (byte)(IsConnected ? 1 : 0);

            NodeInfo = info;
        }

        public bool SendPacket(byte[] data)
        {
            try
            {
                return _connection.Send(data) == data.Length;
            }
            catch (Exception ex)
            {
                Logger.Warning?.PrintMsg(LogClass.ServiceLdn, $"LAN Play: failed to send to the LDN station {RemoteEndPoint}: {ex.Message}");

                return false;
            }
        }

        public bool Disconnect()
        {
            if (!_connection.Connected)
            {
                return false;
            }

            _connection.Close();

            return true;
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;

            _protocol.Connect -= OnConnect;

            _connection.DataReceived -= OnDataReceived;
            _connection.Closed -= OnClosed;

            _connection.Abort();
        }
    }
}
