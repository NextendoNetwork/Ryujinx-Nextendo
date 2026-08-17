using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Ldn.Types;
using System.Net;
using System.Net.Sockets;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm.Proxy
{
    internal class LdnProxyTcpSession : NetCoreServer.TcpSession, ILdnTcpSession
    {
        private readonly LanProtocol _protocol;

        public int NodeId { get; set; }

        public NodeInfo NodeInfo { get; set; }

        public EndPoint RemoteEndPoint => Socket.RemoteEndPoint;

        private byte[] _buffer;
        private int _bufferEnd;

        public LdnProxyTcpSession(LdnProxyTcpServer server, LanProtocol protocol) : base(server)
        {
            _protocol = protocol;
            _protocol.Connect += OnConnect;
            _buffer = new byte[LanProtocol.BufferSize];
            OptionSendBufferSize = LanProtocol.TcpTxBufferSize;
            OptionReceiveBufferSize = LanProtocol.TcpRxBufferSize;
            OptionSendBufferLimit = LanProtocol.TxBufferSizeMax;
            OptionReceiveBufferLimit = LanProtocol.RxBufferSizeMax;
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
            return SendAsync(data);
        }

        protected override void OnConnected()
        {
            Logger.Info?.PrintMsg(LogClass.ServiceLdn, "LdnProxyTCPSession connected!");
        }

        protected override void OnDisconnected()
        {
            Logger.Info?.PrintMsg(LogClass.ServiceLdn, "LdnProxyTCPSession disconnected!");

            _protocol.InvokeDisconnectStation(this);
        }

        protected override void OnReceived(byte[] buffer, long offset, long size)
        {
            _protocol.Read(ref _buffer, ref _bufferEnd, buffer, (int)offset, (int)size, this.Socket.RemoteEndPoint);
        }

        protected override void OnError(SocketError error)
        {
            Logger.Error?.PrintMsg(LogClass.ServiceLdn, $"LdnProxyTCPSession caught an error with code {error}");

            Dispose();
        }

        protected override void Dispose(bool disposingManagedResources)
        {
            _protocol.Connect -= OnConnect;
            base.Dispose(disposingManagedResources);
        }

        private void OnConnect(NodeInfo info, EndPoint endPoint)
        {
            try
            {
                if (endPoint.Equals(this.Socket.RemoteEndPoint))
                {
                    NodeInfo = info;
                    _protocol.InvokeAccept(this);
                }
            }
            catch (System.ObjectDisposedException)
            {
                Logger.Error?.PrintMsg(LogClass.ServiceLdn, $"LdnProxyTCPSession was disposed. [IP: {NodeInfo.Ipv4Address}]");

                _protocol.InvokeDisconnectStation(this);
            }
        }
    }
}
