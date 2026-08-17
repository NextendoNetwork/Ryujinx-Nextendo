using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm.Proxy;
using System;
using System.Collections.Generic;
using System.Net;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay.Proxy
{
    /// <summary>
    /// LDN session host over the LAN Play relay: accepts the stations that connect to the virtual
    /// network interface on the ldn_mitm TCP port.
    /// </summary>
    internal class LanPlayLdnTcpServer : ILdnTcpSocket
    {
        private readonly LanProtocol _protocol;
        private readonly LanPlayNetworkInterface _networkInterface;
        private readonly List<LanPlayLdnTcpSession> _sessions = [];
        private readonly int _port;

        private LanPlayTcpListener _listener;

        public bool IsServer => true;

        public LanPlayLdnTcpServer(LanProtocol protocol, LanPlayNetworkInterface networkInterface, int port)
        {
            _protocol = protocol;
            _networkInterface = networkInterface;
            _port = port;
        }

        public bool Start()
        {
            if (_listener != null)
            {
                return true;
            }

            _listener = _networkInterface.ListenTcp((ushort)_port);
            _listener.ConnectionAccepted += OnConnectionAccepted;

            Logger.Info?.PrintMsg(LogClass.ServiceLdn, $"LAN Play: hosting an LDN session on {_networkInterface.Address}:{_port}.");

            return true;
        }

        private void OnConnectionAccepted(LanPlayTcpConnection connection)
        {
            LanPlayLdnTcpSession session = new(_protocol, connection);

            lock (_sessions)
            {
                _sessions.Add(session);
            }

            connection.Closed += () =>
            {
                lock (_sessions)
                {
                    _sessions.Remove(session);
                }
            };
        }

        public bool Stop()
        {
            LanPlayLdnTcpSession[] sessions;

            lock (_sessions)
            {
                sessions = [.. _sessions];

                _sessions.Clear();
            }

            foreach (LanPlayLdnTcpSession session in sessions)
            {
                session.Dispose();
            }

            _listener?.Close();
            _listener = null;

            return true;
        }

        public bool Connect()
        {
            throw new InvalidOperationException("Connect was called on the LDN session host.");
        }

        public void DisconnectAndStop()
        {
            Stop();
        }

        public bool SendPacketAsync(EndPoint endpoint, byte[] buffer)
        {
            throw new InvalidOperationException("SendPacketAsync was called on the LDN session host.");
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
