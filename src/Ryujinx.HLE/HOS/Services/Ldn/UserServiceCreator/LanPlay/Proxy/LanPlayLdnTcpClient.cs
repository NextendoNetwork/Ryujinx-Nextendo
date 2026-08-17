using Ryujinx.Common.Logging;
using Ryujinx.Common.Utilities;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm.Proxy;
using System;
using System.Net;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay.Proxy
{
    /// <summary>
    /// Joins an LDN session hosted by another console (or emulator) on the LAN Play network.
    /// </summary>
    internal class LanPlayLdnTcpClient : ILdnTcpSocket
    {
        private const int ConnectTimeoutMs = 4000;

        private readonly LanProtocol _protocol;
        private readonly LanPlayNetworkInterface _networkInterface;
        private readonly IPAddress _address;
        private readonly int _port;

        private LanPlayTcpConnection _connection;
        private byte[] _buffer = new byte[LanProtocol.BufferSize];
        private int _bufferEnd;

        public bool IsServer => false;

        public LanPlayLdnTcpClient(LanProtocol protocol, LanPlayNetworkInterface networkInterface, IPAddress address, int port)
        {
            _protocol = protocol;
            _networkInterface = networkInterface;
            _address = address;
            _port = port;
        }

        public bool Connect()
        {
            _connection = new LanPlayTcpConnection(
                _networkInterface,
                _networkInterface.AllocateTcpPort(),
                NetworkHelpers.ConvertIpv4Address(_address),
                (ushort)_port);

            _connection.DataReceived += OnDataReceived;

            if (!_connection.Connect(ConnectTimeoutMs))
            {
                Logger.Error?.PrintMsg(LogClass.ServiceLdn, $"LAN Play: could not reach the LDN session host at {_address}:{_port}.");

                _connection = null;

                return false;
            }

            Logger.Info?.PrintMsg(LogClass.ServiceLdn, $"LAN Play: joined the LDN session hosted by {_address}:{_port}.");

            return true;
        }

        private void OnDataReceived(byte[] data)
        {
            _protocol.Read(ref _buffer, ref _bufferEnd, data, 0, data.Length);
        }

        public bool SendPacketAsync(EndPoint endPoint, byte[] data)
        {
            if (endPoint != null)
            {
                Logger.Warning?.PrintMsg(LogClass.ServiceLdn, "LAN Play: LDN client is sending a packet but endpoint is not null.");
            }

            if (_connection == null)
            {
                return false;
            }

            try
            {
                return _connection.Send(data) == data.Length;
            }
            catch (Exception ex)
            {
                Logger.Warning?.PrintMsg(LogClass.ServiceLdn, $"LAN Play: failed to send to the LDN session host: {ex.Message}");

                return false;
            }
        }

        public bool Start()
        {
            throw new InvalidOperationException("Start was called on the LDN session client.");
        }

        public bool Stop()
        {
            DisconnectAndStop();

            return true;
        }

        public void DisconnectAndStop()
        {
            if (_connection == null)
            {
                return;
            }

            _connection.DataReceived -= OnDataReceived;
            _connection.Close();
            _connection = null;
        }

        public void Dispose()
        {
            DisconnectAndStop();
        }
    }
}
