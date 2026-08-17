using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Ldn.Types;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm.Types;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm.Proxy
{
    internal class LdnProxyUdpServer : NetCoreServer.UdpServer, ILdnUdpSocket
    {
        private readonly LanProtocol _protocol;
        private readonly LdnScanResults _scanResults = new();
        private byte[] _buffer;
        private int _bufferEnd;

        public LdnProxyUdpServer(LanProtocol protocol, IPAddress address, int port) : base(address, port)
        {
            _protocol = protocol;
            _protocol.Scan += HandleScan;
            _protocol.ScanResponse += HandleScanResponse;
            _buffer = new byte[LanProtocol.BufferSize];
            OptionReuseAddress = true;
            OptionReceiveBufferSize = LanProtocol.RxBufferSizeMax;
            OptionSendBufferSize = LanProtocol.TxBufferSizeMax;

            Start();
        }

        protected override Socket CreateSocket()
        {
            return new Socket(Endpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp)
            {
                EnableBroadcast = true,
            };
        }

        protected override void OnStarted()
        {
            ReceiveAsync();
        }

        protected override void OnReceived(EndPoint endpoint, byte[] buffer, long offset, long size)
        {
            _protocol.Read(ref _buffer, ref _bufferEnd, buffer, (int)offset, (int)size, endpoint);
            ReceiveAsync();
        }

        protected override void OnError(SocketError error)
        {
            Logger.Error?.PrintMsg(LogClass.ServiceLdn, $"LdnProxyUdpServer caught an error with code {error}");
        }

        protected override void Dispose(bool disposingManagedResources)
        {
            _protocol.Scan -= HandleScan;
            _protocol.ScanResponse -= HandleScanResponse;

            _scanResults.Dispose();

            base.Dispose(disposingManagedResources);
        }

        public bool SendPacketAsync(EndPoint endpoint, byte[] data)
        {
            return SendAsync(endpoint, data);
        }

        private void HandleScan(EndPoint endpoint, LanPacketType type, byte[] data)
        {
            _protocol.SendPacket(this, type, data, endpoint);
        }

        private void HandleScanResponse(NetworkInfo info)
        {
            _scanResults.Add(info);
        }

        public void ClearScanResults()
        {
            _scanResults.Clear();
        }

        public Dictionary<ulong, NetworkInfo> GetScanResults()
        {
            return _scanResults.Get();
        }
    }
}
