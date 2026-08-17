using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Ldn.Types;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm.Proxy;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm.Types;
using System.Collections.Generic;
using System.Net;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay.Proxy
{
    /// <summary>
    /// LDN discovery socket carried by the LAN Play relay: the same ldn_mitm packets a real console sends
    /// on UDP port 11452, but sent through the virtual network interface instead of a host socket.
    /// </summary>
    internal class LanPlayLdnUdpSocket : ILdnUdpSocket
    {
        private readonly LanProtocol _protocol;
        private readonly LanPlayNetworkInterface _networkInterface;
        private readonly LdnScanResults _scanResults = new();
        private LanPlayUdpEndpoint _endpoint;

        private byte[] _buffer = new byte[LanProtocol.BufferSize];
        private int _bufferEnd;

        public LanPlayLdnUdpSocket(LanProtocol protocol, LanPlayNetworkInterface networkInterface, int port)
        {
            _protocol = protocol;
            _networkInterface = networkInterface;

            _endpoint = networkInterface.BindUdp((ushort)port);
            _endpoint.PacketReceived += OnPacketReceived;

            _protocol.Scan += HandleScan;
            _protocol.ScanResponse += HandleScanResponse;
        }

        private void OnPacketReceived(IPEndPoint source, byte[] data)
        {
            _protocol.Read(ref _buffer, ref _bufferEnd, data, 0, data.Length, source);
        }

        private void HandleScan(EndPoint endpoint, LanPacketType type, byte[] data)
        {
            _protocol.SendPacket(this, type, data, endpoint);
        }

        private void HandleScanResponse(NetworkInfo info)
        {
            _scanResults.Add(info);
        }

        public bool SendPacketAsync(EndPoint endpoint, byte[] buffer)
        {
            if (endpoint is not IPEndPoint ipEndPoint)
            {
                Logger.Warning?.PrintMsg(LogClass.ServiceLdn, "LAN Play: LDN UDP packet without a destination.");

                return false;
            }

            return _endpoint?.SendTo(ipEndPoint, buffer) > 0;
        }

        public bool Start() => true;

        public bool Stop()
        {
            _endpoint?.Close();
            _endpoint = null;

            return true;
        }

        public void ClearScanResults()
        {
            _scanResults.Clear();
        }

        public Dictionary<ulong, NetworkInfo> GetScanResults()
        {
            return _scanResults.Get();
        }

        public void Dispose()
        {
            _protocol.Scan -= HandleScan;
            _protocol.ScanResponse -= HandleScanResponse;

            Stop();

            _scanResults.Dispose();
        }
    }
}
