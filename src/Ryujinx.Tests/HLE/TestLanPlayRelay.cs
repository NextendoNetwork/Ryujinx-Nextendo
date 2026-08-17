using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Ryujinx.Tests.HLE
{
    /// <summary>
    /// In-process stand-in for a LAN Play relay server, implementing the routing behaviour the real relay
    /// has: it learns which client owns which 10.13.x.x address from the source address of the IPv4 packets
    /// it receives, forwards unicast packets to that client, and floods broadcasts to everybody else.
    /// </summary>
    internal sealed class TestLanPlayRelay : IDisposable
    {
        private const byte TypeKeepAlive = 0x00;
        private const byte TypeIpv4 = 0x01;
        private const byte TypePing = 0x02;
        private const byte TypeIpv4Fragment = 0x03;

        private const uint SubnetBroadcast = 0x0A0DFFFF;

        private readonly Socket _socket;
        private readonly Thread _thread;
        private readonly Dictionary<EndPoint, long> _clients = new();
        private readonly Dictionary<uint, EndPoint> _owners = new();

        private bool _running = true;

        public IPEndPoint EndPoint { get; }

        public TestLanPlayRelay(int port = 0)
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.Bind(new IPEndPoint(IPAddress.Loopback, port));

            EndPoint = (IPEndPoint)_socket.LocalEndPoint;

            _thread = new Thread(Run)
            {
                Name = "TestLanPlayRelay",
                IsBackground = true,
            };

            _thread.Start();
        }

        private void Run()
        {
            byte[] buffer = new byte[4096];
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);

            while (_running)
            {
                int size;

                try
                {
                    size = _socket.ReceiveFrom(buffer, ref remote);
                }
                catch (SocketException)
                {
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (size < 1)
                {
                    continue;
                }

                EndPoint sender = remote.Create(remote.Serialize());

                _clients[sender] = Environment.TickCount64;

                byte type = (byte)(buffer[0] & 0x7F);
                Span<byte> payload = buffer.AsSpan(1, size - 1);

                uint source;
                uint destination;

                switch (type)
                {
                    case TypeIpv4 when payload.Length >= 20:
                        source = BinaryPrimitives.ReadUInt32BigEndian(payload[12..]);
                        destination = BinaryPrimitives.ReadUInt32BigEndian(payload[16..]);
                        break;

                    case TypeIpv4Fragment when payload.Length >= 16:
                        source = BinaryPrimitives.ReadUInt32BigEndian(payload);
                        destination = BinaryPrimitives.ReadUInt32BigEndian(payload[4..]);
                        break;

                    case TypeKeepAlive:
                    case TypePing:
                    default:
                        continue;
                }

                _owners[source] = sender;

                List<EndPoint> targets = [];

                if (destination is SubnetBroadcast or uint.MaxValue || !_owners.TryGetValue(destination, out EndPoint owner))
                {
                    foreach (EndPoint client in _clients.Keys)
                    {
                        if (!client.Equals(sender))
                        {
                            targets.Add(client);
                        }
                    }
                }
                else
                {
                    targets.Add(owner);
                }

                foreach (EndPoint target in targets)
                {
                    try
                    {
                        _socket.SendTo(buffer.AsSpan(0, size), SocketFlags.None, target);
                    }
                    catch (SocketException)
                    {
                        // A client went away; it will be forgotten on the next scan.
                    }
                }
            }
        }

        public void Dispose()
        {
            _running = false;

            _socket.Close();
            _socket.Dispose();
        }
    }
}
