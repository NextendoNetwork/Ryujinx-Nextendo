using NUnit.Framework;
using Ryujinx.Common.Memory;
using Ryujinx.Common.Utilities;
using Ryujinx.HLE.HOS.Services.Ldn.Types;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.Types;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd.Proxy;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Ryujinx.Tests.HLE
{
    /// <summary>
    /// Tests for the LAN Play networking stack: the embedded switch-lan-play client and the virtual
    /// network interface it feeds. Everything runs against an in-process relay, so no external server,
    /// no host network configuration and no elevated privileges are involved.
    /// </summary>
    public class LanPlayTests
    {
        private const int Timeout = 3000;
        private const ushort LdnPort = 11452;

        private TestLanPlayRelay _relay;

        [SetUp]
        public void SetUp()
        {
            _relay = new TestLanPlayRelay();
        }

        [TearDown]
        public void TearDown()
        {
            _relay.Dispose();
        }

        private LanPlayStack CreateStack(string virtualAddress)
        {
            Assert.That(
                LanPlayConfiguration.TryParse($"{_relay.EndPoint.Address}:{_relay.EndPoint.Port}", virtualAddress, out LanPlayConfiguration configuration),
                Is.True);

            return LanPlayStack.Create(configuration);
        }

        [Test]
        public void RelayCarriesBroadcastAndUnicastDatagrams()
        {
            using LanPlayStack hostStack = CreateStack("10.13.1.2");
            using LanPlayStack stationStack = CreateStack("10.13.1.3");

            LanPlayUdpEndpoint host = hostStack.NetworkInterface.BindUdp(LdnPort);
            LanPlayUdpEndpoint station = stationStack.NetworkInterface.BindUdp(LdnPort);

            // A scan request is a broadcast, and every client of the relay has to receive it.
            station.SendTo(new IPEndPoint(stationStack.NetworkInterface.BroadcastAddress, LdnPort), Encoding.ASCII.GetBytes("scan"));

            Assert.That(host.WaitForData(Timeout), Is.True, "the broadcast never arrived");
            Assert.That(host.TryDequeue(out LanPlayUdpEndpoint.Datagram broadcast), Is.True);
            Assert.That(Encoding.ASCII.GetString(broadcast.Data), Is.EqualTo("scan"));
            Assert.That(broadcast.WasBroadcast, Is.True);
            Assert.That(broadcast.Source.Address.ToString(), Is.EqualTo("10.13.1.3"));

            // The scan response is unicast back to the station.
            host.SendTo(broadcast.Source, Encoding.ASCII.GetBytes("scan reply"));

            Assert.That(station.WaitForData(Timeout), Is.True, "the unicast reply never arrived");
            Assert.That(station.TryDequeue(out LanPlayUdpEndpoint.Datagram reply), Is.True);
            Assert.That(Encoding.ASCII.GetString(reply.Data), Is.EqualTo("scan reply"));
            Assert.That(reply.WasBroadcast, Is.False);
        }

        [Test]
        public void DatagramLargerThanTheMtuIsFragmentedAndReassembled()
        {
            using LanPlayStack senderStack = CreateStack("10.13.2.2");
            using LanPlayStack receiverStack = CreateStack("10.13.2.3");

            LanPlayUdpEndpoint sender = senderStack.NetworkInterface.BindUdp(LdnPort);
            LanPlayUdpEndpoint receiver = receiverStack.NetworkInterface.BindUdp(LdnPort);

            byte[] payload = new byte[3000];
            new Random(0x1234).NextBytes(payload);

            sender.SendTo(new IPEndPoint(receiverStack.NetworkInterface.Address, LdnPort), payload);

            Assert.That(receiver.WaitForData(Timeout), Is.True, "the fragmented datagram never arrived");
            Assert.That(receiver.TryDequeue(out LanPlayUdpEndpoint.Datagram received), Is.True);
            Assert.That(received.Data, Is.EqualTo(payload));
        }

        [Test]
        public void TcpConnectionsCarryDataInBothDirectionsAndCloseCleanly()
        {
            using LanPlayStack hostStack = CreateStack("10.13.3.2");
            using LanPlayStack stationStack = CreateStack("10.13.3.3");

            LanPlayTcpListener listener = hostStack.NetworkInterface.ListenTcp(LdnPort);

            LanPlayTcpConnection station = new(
                stationStack.NetworkInterface,
                stationStack.NetworkInterface.AllocateTcpPort(),
                NetworkHelpers.ConvertIpv4Address(hostStack.NetworkInterface.Address),
                LdnPort);

            Assert.That(station.Connect(Timeout), Is.True, "the handshake did not complete");

            LanPlayTcpConnection accepted = listener.Accept(Timeout);

            Assert.That(accepted, Is.Not.Null, "the host did not accept the connection");

            byte[] request = Encoding.ASCII.GetBytes("Connect");
            station.Send(request);

            byte[] buffer = new byte[64];
            int read = accepted.Receive(buffer, Timeout, false, true);

            Assert.That(Encoding.ASCII.GetString(buffer, 0, read), Is.EqualTo("Connect"));

            // A network info sync is bigger than one segment, so this also covers segmentation.
            byte[] response = new byte[8000];
            new Random(0x99).NextBytes(response);

            accepted.Send(response);

            byte[] responseBuffer = new byte[response.Length];
            int total = 0;

            while (total < response.Length)
            {
                int chunk = station.Receive(responseBuffer.AsSpan(total), Timeout, false, true);

                if (chunk <= 0)
                {
                    break;
                }

                total += chunk;
            }

            Assert.That(total, Is.EqualTo(response.Length));
            Assert.That(responseBuffer, Is.EqualTo(response));

            accepted.Close();

            Assert.That(WaitFor(() => station.RemoteClosed), Is.True, "the close was not seen by the peer");

            station.Close();
            listener.Close();
        }

        [Test]
        public void VirtualAddressesAreProbedBeforeBeingUsed()
        {
            using LanPlayStack stack = CreateStack("10.13.4.2");

            using LanPlayStack probeStack = CreateStack(null);

            Assert.That(
                VirtualAddressAllocator.IsAddressTaken(probeStack.Client, NetworkHelpers.ConvertIpv4Address(IPAddress.Parse("10.13.4.2"))),
                Is.True,
                "an address in use was reported as free");

            Assert.That(
                VirtualAddressAllocator.IsAddressTaken(probeStack.Client, NetworkHelpers.ConvertIpv4Address(IPAddress.Parse("10.13.222.111"))),
                Is.False,
                "a free address was reported as taken");

            Assert.That(probeStack.NetworkInterface.Address.ToString(), Does.StartWith("10.13."));
            Assert.That(probeStack.NetworkInterface.Address, Is.Not.EqualTo(stack.NetworkInterface.Address));
        }

        [Test]
        public void UnusableVirtualAddressesAreRejected()
        {
            Assert.That(VirtualAddressAllocator.IsUsableAddress(NetworkHelpers.ConvertIpv4Address(IPAddress.Parse("10.13.1.2"))), Is.True);

            // Network, broadcast and the address the switch-lan-play client itself answers on.
            Assert.That(VirtualAddressAllocator.IsUsableAddress(NetworkHelpers.ConvertIpv4Address(IPAddress.Parse("10.13.1.0"))), Is.False);
            Assert.That(VirtualAddressAllocator.IsUsableAddress(NetworkHelpers.ConvertIpv4Address(IPAddress.Parse("10.13.1.255"))), Is.False);
            Assert.That(VirtualAddressAllocator.IsUsableAddress(NetworkHelpers.ConvertIpv4Address(IPAddress.Parse("10.13.37.1"))), Is.False);
            Assert.That(VirtualAddressAllocator.IsUsableAddress(NetworkHelpers.ConvertIpv4Address(IPAddress.Parse("192.168.1.50"))), Is.False);
        }

        [Test]
        public void ServerStringWithCredentialsIsParsed()
        {
            Assert.That(LanPlayConfiguration.TryParse("player:secret@127.0.0.1:12345", string.Empty, out LanPlayConfiguration configuration), Is.True);

            Assert.That(configuration.RelayEndPoint.ToString(), Is.EqualTo("127.0.0.1:12345"));
            Assert.That(configuration.UserName, Is.EqualTo("player"));
            Assert.That(configuration.PasswordHash, Is.Not.Null);
            Assert.That(configuration.VirtualAddress, Is.Null);

            // Without a port, the default LAN Play port is used.
            Assert.That(LanPlayConfiguration.TryParse("127.0.0.1", "10.13.9.9", out configuration), Is.True);
            Assert.That(configuration.RelayEndPoint.Port, Is.EqualTo(11451));
            Assert.That(configuration.VirtualAddress.ToString(), Is.EqualTo("10.13.9.9"));
            Assert.That(configuration.UserName, Is.Null);

            // An address outside the LAN Play network is ignored in favour of an automatic one.
            Assert.That(LanPlayConfiguration.TryParse("127.0.0.1", "192.168.1.50", out configuration), Is.True);
            Assert.That(configuration.VirtualAddress, Is.Null);

            Assert.That(LanPlayConfiguration.TryParse(string.Empty, string.Empty, out _), Is.False);
        }

        [Test]
        public void StackReconnectsAfterBeingTornDown()
        {
            using LanPlayStack peerStack = CreateStack("10.13.5.3");

            LanPlayUdpEndpoint peer = peerStack.NetworkInterface.BindUdp(LdnPort);

            using (LanPlayStack stack = CreateStack("10.13.5.2"))
            {
                LanPlayUdpEndpoint endpoint = stack.NetworkInterface.BindUdp(LdnPort);

                peer.SendTo(new IPEndPoint(stack.NetworkInterface.Address, LdnPort), Encoding.ASCII.GetBytes("first"));

                Assert.That(endpoint.WaitForData(Timeout), Is.True);
                Assert.That(endpoint.TryDequeue(out LanPlayUdpEndpoint.Datagram first), Is.True);
                Assert.That(Encoding.ASCII.GetString(first.Data), Is.EqualTo("first"));
            }

            using LanPlayStack reconnectedStack = CreateStack("10.13.5.2");

            LanPlayUdpEndpoint reconnected = reconnectedStack.NetworkInterface.BindUdp(LdnPort);

            peer.SendTo(new IPEndPoint(reconnectedStack.NetworkInterface.Address, LdnPort), Encoding.ASCII.GetBytes("second"));

            Assert.That(reconnected.WaitForData(Timeout), Is.True, "the relay did not route to the reconnected client");
            Assert.That(reconnected.TryDequeue(out LanPlayUdpEndpoint.Datagram second), Is.True);
            Assert.That(Encoding.ASCII.GetString(second.Data), Is.EqualTo("second"));
        }

        [Test]
        public void GuestSocketsUseTheVirtualInterfaceForLanPlayTrafficOnly()
        {
            using LanPlayStack senderStack = CreateStack("10.13.7.2");
            using LanPlayStack receiverStack = CreateStack("10.13.7.3");

            // A socket of the emulated console, as created through SocketHelpers when LAN Play is active.
            ISocketImpl sender = senderStack.CreateSocket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp, "0");
            ISocketImpl receiver = receiverStack.CreateSocket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp, "0");

            receiver.Bind(new IPEndPoint(receiverStack.NetworkInterface.Address, 50000));

            byte[] payload = Encoding.ASCII.GetBytes("game data");

            sender.SendTo(payload, SocketFlags.None, new IPEndPoint(receiverStack.NetworkInterface.Address, 50000));

            byte[] buffer = new byte[64];
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);

            Assert.That(receiver.Poll(Timeout * 1000, SelectMode.SelectRead), Is.True, "the guest socket received nothing");

            // The guest polls its sockets through SocketHelpers.Select, which has to understand a socket
            // that is not backed by a host socket.
            List<ISocketImpl> readEvents = [receiver];
            List<ISocketImpl> writeEvents = [];
            List<ISocketImpl> errorEvents = [];

            SocketHelpers.Select(readEvents, writeEvents, errorEvents, 0);

            Assert.That(readEvents, Has.Count.EqualTo(1), "Select did not report the readable guest socket");

            int read = receiver.ReceiveFrom(buffer, SocketFlags.None, ref from);

            Assert.That(Encoding.ASCII.GetString(buffer, 0, read), Is.EqualTo("game data"));
            Assert.That(((IPEndPoint)from).Address.ToString(), Is.EqualTo("10.13.7.2"));

            // Traffic that is not for the LAN Play network still goes out through the host stack.
            using Socket hostSocket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            hostSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            hostSocket.ReceiveTimeout = Timeout;

            sender.SendTo(Encoding.ASCII.GetBytes("internet"), SocketFlags.None, (IPEndPoint)hostSocket.LocalEndPoint);

            byte[] hostBuffer = new byte[64];
            int hostRead = hostSocket.Receive(hostBuffer);

            Assert.That(Encoding.ASCII.GetString(hostBuffer, 0, hostRead), Is.EqualTo("internet"));

            sender.Close();
            receiver.Close();
        }

        [Test]
        public void LdnSessionIsDiscoveredAndJoinedOverTheRelay()
        {
            using LanPlayLdnClient host = new(CreateStack("10.13.6.2"));
            using LanPlayLdnClient station = new(CreateStack("10.13.6.3"));

            NetworkInfo hostNetworkInfo = default;

            host.NetworkChange += (_, args) => hostNetworkInfo = args.Info;

            Assert.That(host.CreateNetwork(CreateAccessPointRequest("Host"), []), Is.True, "the session was not created");

            NetworkInfo[] found = station.Scan(6, new ScanFilter { Flag = ScanFilterFlag.LocalCommunicationId, NetworkId = new NetworkId { IntentId = Intent } });

            Assert.That(found.Length, Is.EqualTo(1), "the station did not find the session");
            Assert.That(NetworkHelpers.ConvertUint(found[0].Ldn.Nodes[0].Ipv4Address).ToString(), Is.EqualTo("10.13.6.2"));
            Assert.That(Encoding.ASCII.GetString(found[0].Ldn.Nodes[0].UserName.AsSpan()[..4]), Is.EqualTo("Host"));

            NetworkError error = station.Connect(new ConnectRequest
            {
                SecurityConfig = new SecurityConfig { SecurityMode = SecurityMode.All },
                UserConfig = UserConfig("Station"),
                LocalCommunicationVersion = 1,
                NetworkInfo = found[0],
            });

            Assert.That(error, Is.EqualTo(NetworkError.None), "the station could not join the session");
            Assert.That(WaitFor(() => hostNetworkInfo.Ldn.NodeCount == 2), Is.True, $"the host still reports {hostNetworkInfo.Ldn.NodeCount} node(s)");

            Span<NodeInfo> nodes = hostNetworkInfo.Ldn.Nodes.AsSpan();

            Assert.That(NetworkHelpers.ConvertUint(nodes[1].Ipv4Address).ToString(), Is.EqualTo("10.13.6.3"));
            Assert.That(Encoding.ASCII.GetString(nodes[1].UserName.AsSpan()[..7]), Is.EqualTo("Station"));

            station.DisconnectNetwork();
            host.DisconnectNetwork();
        }

        private static IntentId Intent => new() { LocalCommunicationId = 0x0100000000010000 };

        private static CreateAccessPointRequest CreateAccessPointRequest(string userName) =>
            new()
            {
                SecurityConfig = new SecurityConfig { SecurityMode = SecurityMode.All },
                UserConfig = UserConfig(userName),
                NetworkConfig = new NetworkConfig
                {
                    IntentId = Intent,
                    Channel = 6,
                    NodeCountMax = 8,
                    LocalCommunicationVersion = 1,
                },
            };

        private static UserConfig UserConfig(string userName)
        {
            UserConfig config = new() { UserName = new Array33<byte>() };

            Encoding.ASCII.GetBytes(userName).CopyTo(config.UserName.AsSpan());

            return config;
        }

        private static bool WaitFor(Func<bool> condition, int timeoutMilliseconds = Timeout)
        {
            long deadline = Environment.TickCount64 + timeoutMilliseconds;

            while (Environment.TickCount64 < deadline)
            {
                if (condition())
                {
                    return true;
                }

                Thread.Sleep(20);
            }

            return condition();
        }
    }
}
