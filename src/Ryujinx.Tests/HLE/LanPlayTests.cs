using NUnit.Framework;
using Ryujinx.Common.Configuration.Multiplayer;
using Ryujinx.Common.Memory;
using Ryujinx.Common.Utilities;
using Ryujinx.HLE.HOS.Services.Ldn.Types;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay.Proxy;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnRyu;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnRyu.Proxy;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.Types;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd.Impl;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd.Proxy;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd.Types;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
            // The LAN Play stack of a session lives in a static, so a test that switches modes must not
            // leak it into the next one.
            SocketHelpers.ShutdownLanPlay();
            SocketHelpers.UnregisterProxy();

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

        [Test]
        public void SessionShutsDownPromptlyOnEveryPlatform()
        {
            // Closing a socket does not reliably abort a blocking receive on every platform, so teardown
            // must not depend on it. A slow Dispose here would mean a leaked thread per session.
            LanPlayStack stack = CreateStack("10.13.11.2");

            Stopwatch stopwatch = Stopwatch.StartNew();

            stack.Dispose();

            stopwatch.Stop();

            Assert.That(stack.Client.IsRunning, Is.False);
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(2000), "tearing down the LAN Play session took too long");

            // Disposing twice must be harmless, whichever thread got there first.
            Assert.DoesNotThrow(stack.Dispose);
        }

        [Test]
        public void SessionSurvivesAnUnreachableRelay()
        {
            // A relay that is down makes the host queue an ICMP "port unreachable" against our socket.
            // Windows then fails the *next* receive with WSAECONNRESET, Linux and macOS do not; either way
            // the session must stay alive and pick up again once the relay answers.
            int deadPort;

            using (Socket probe = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));

                deadPort = ((IPEndPoint)probe.LocalEndPoint).Port;
            }

            Assert.That(LanPlayConfiguration.TryParse($"127.0.0.1:{deadPort}", "10.13.13.2", out LanPlayConfiguration configuration), Is.True);

            using LanPlayStack stack = LanPlayStack.Create(configuration);

            LanPlayUdpEndpoint endpoint = stack.NetworkInterface.BindUdp(LdnPort);

            for (int i = 0; i < 4; i++)
            {
                endpoint.SendTo(new IPEndPoint(IPAddress.Parse("10.13.13.3"), LdnPort), Encoding.ASCII.GetBytes("anybody there"));

                Thread.Sleep(100);
            }

            Assert.That(stack.Client.IsRunning, Is.True, "the relay client stopped after an ICMP error");

            // Now bring a relay up on that port and check the session recovers by itself.
            using TestLanPlayRelay relay = new(deadPort);
            using LanPlayStack peerStack = LanPlayStack.Create(Parse($"127.0.0.1:{deadPort}", "10.13.13.3"));

            LanPlayUdpEndpoint peer = peerStack.NetworkInterface.BindUdp(LdnPort);

            endpoint.SendTo(new IPEndPoint(peerStack.NetworkInterface.Address, LdnPort), Encoding.ASCII.GetBytes("still here"));

            Assert.That(peer.WaitForData(Timeout), Is.True, "traffic did not flow after the relay came back");
            Assert.That(peer.TryDequeue(out LanPlayUdpEndpoint.Datagram datagram), Is.True);
            Assert.That(Encoding.ASCII.GetString(datagram.Data), Is.EqualTo("still here"));
        }

        private static LanPlayConfiguration Parse(string server, string virtualAddress)
        {
            Assert.That(LanPlayConfiguration.TryParse(server, virtualAddress, out LanPlayConfiguration configuration), Is.True);

            return configuration;
        }

        [Test]
        public void SwitchingAwayFromLanPlayPutsTheConsoleBackOnTheHostNetwork()
        {
            string relay = $"{_relay.EndPoint.Address}:{_relay.EndPoint.Port}";

            SocketHelpers.ApplyMultiplayerMode(MultiplayerMode.LanPlay, relay, "10.13.20.2");

            Assert.That(SocketHelpers.CurrentLanPlayStack, Is.Not.Null, "LAN Play did not join the relay");

            ISocketImpl duringLanPlay = SocketHelpers.CreateSocket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp, "0");

            Assert.That(duringLanPlay, Is.InstanceOf<LanPlaySocket>(), "the guest socket did not use the virtual interface");

            // Something outside the LAN Play network, standing in for the online service: it has to work
            // while LAN Play is on, and keep working after it is switched off.
            using Socket online = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            online.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            online.ReceiveTimeout = Timeout;

            IPEndPoint onlineEndPoint = (IPEndPoint)online.LocalEndPoint;
            byte[] buffer = new byte[64];

            duringLanPlay.SendTo(Encoding.ASCII.GetBytes("hello online"), SocketFlags.None, onlineEndPoint);

            Assert.That(Encoding.ASCII.GetString(buffer, 0, online.Receive(buffer)), Is.EqualTo("hello online"));

            // Now the user picks another multiplayer mode while the game is running.
            SocketHelpers.ApplyMultiplayerMode(MultiplayerMode.Disabled, relay, "10.13.20.2");

            Assert.That(SocketHelpers.ActiveLanPlayStack, Is.Null, "LAN Play was still joined after being switched off");

            ISocketImpl afterLanPlay = SocketHelpers.CreateSocket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp, "0");

            Assert.That(afterLanPlay, Is.InstanceOf<DefaultSocket>(), "new guest sockets did not go back to the host stack");

            afterLanPlay.SendTo(Encoding.ASCII.GetBytes("after switch"), SocketFlags.None, onlineEndPoint);

            Assert.That(Encoding.ASCII.GetString(buffer, 0, online.Receive(buffer)), Is.EqualTo("after switch"));

            // A socket the game opened *before* the switch must not be collateral damage: its traffic to
            // anything outside the LAN Play network keeps flowing.
            duringLanPlay.SendTo(Encoding.ASCII.GetBytes("still connected"), SocketFlags.None, onlineEndPoint);

            Assert.That(Encoding.ASCII.GetString(buffer, 0, online.Receive(buffer)), Is.EqualTo("still connected"));

            duringLanPlay.Close();
            afterLanPlay.Close();
        }

        [Test]
        public void LanPlayStaysOutOfTheWayUntilTheGameUsesIt()
        {
            // Having LAN Play selected must not change anything for online play: the console keeps
            // reporting its host address (nifm keys off IsGuestActive) until the game actually uses the
            // LAN Play network, which is what entering a game's LAN mode or a local session does.
            string relay = $"{_relay.EndPoint.Address}:{_relay.EndPoint.Port}";

            SocketHelpers.ApplyMultiplayerMode(MultiplayerMode.LanPlay, relay, "10.13.24.2");

            LanPlayStack stack = SocketHelpers.CurrentLanPlayStack;

            Assert.That(stack, Is.Not.Null);
            Assert.That(stack.IsGuestActive, Is.False, "joining the relay must not count as the game using it");

            ISocketImpl guest = SocketHelpers.CreateSocket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp, "0");

            // Talking to something outside the LAN Play network (the online service) leaves it inactive.
            using Socket online = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            online.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            online.ReceiveTimeout = Timeout;

            guest.SendTo(Encoding.ASCII.GetBytes("online"), SocketFlags.None, (IPEndPoint)online.LocalEndPoint);

            Assert.That(online.Receive(new byte[16]), Is.EqualTo(6));
            Assert.That(stack.IsGuestActive, Is.False, "online traffic must not mark the LAN Play network as in use");

            // Entering a LAN mode does: the game broadcasts on the local network.
            guest.SendTo(Encoding.ASCII.GetBytes("lan mode"), SocketFlags.None, new IPEndPoint(IPAddress.Broadcast, 11452));

            Assert.That(stack.IsGuestActive, Is.True, "LAN traffic did not mark the LAN Play network as in use");

            guest.Close();
        }

        [Test]
        public void AGameOwnLanModeBroadcastReachesTheRelay()
        {
            // A game with its own LAN mode (a Splatoon private battle over LAN, for instance) asks nifm for
            // the console's address and broadcasts on that network. Until the console presents its LAN Play
            // address that is the *host* network's broadcast address, so it used to leave through the host
            // network — where no relay peer could ever see it, and where Linux and macOS reject it with
            // EACCES unless the game asked for SO_BROADCAST, which the game reads as the search failing.
            uint hostBroadcast = FindHostBroadcastAddress();

            if (hostBroadcast == 0)
            {
                Assert.Ignore("this machine has no IPv4 network with a broadcast address");
            }

            string relay = $"{_relay.EndPoint.Address}:{_relay.EndPoint.Port}";

            SocketHelpers.ApplyMultiplayerMode(MultiplayerMode.LanPlay, relay, "10.13.26.2");

            LanPlayStack stack = SocketHelpers.CurrentLanPlayStack;

            Assert.That(stack, Is.Not.Null);

            using LanPlayStack peerStack = LanPlayStack.Create(Parse(relay, "10.13.26.3"));

            LanPlayUdpEndpoint peer = peerStack.NetworkInterface.BindUdp(LdnPort);

            ISocketImpl guest = SocketHelpers.CreateSocket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp, "0");

            guest.Bind(new IPEndPoint(IPAddress.Any, 35000));
            guest.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);
            guest.SendTo(Encoding.ASCII.GetBytes("browse"), SocketFlags.None, new IPEndPoint(NetworkHelpers.ConvertUint(hostBroadcast), LdnPort));

            Assert.That(peer.WaitForData(Timeout), Is.True, "the LAN discovery broadcast never reached the relay");
            Assert.That(peer.TryDequeue(out LanPlayUdpEndpoint.Datagram browse), Is.True);
            Assert.That(Encoding.ASCII.GetString(browse.Data), Is.EqualTo("browse"));

            // Re-addressed to the LAN Play network, otherwise the relay would not flood it and a peer would
            // drop it as addressed to somebody else.
            Assert.That(browse.WasBroadcast, Is.True, "the broadcast was not re-addressed to the LAN Play network");
            Assert.That(browse.Source.Address.ToString(), Is.EqualTo("10.13.26.2"));

            // The game entering its LAN mode is exactly what makes the console present its LAN Play address.
            Assert.That(stack.IsGuestActive, Is.True, "a LAN discovery broadcast did not mark the LAN Play network as in use");

            guest.Close();
            peer.Close();
        }

        [Test]
        public void TheHostSocketKeepsTheOptionsSetBeforeItExisted()
        {
            // The host socket is only created when the guest first sends somewhere outside the LAN Play
            // network, so everything the guest set on the socket before that has to be replayed on it.
            string relay = $"{_relay.EndPoint.Address}:{_relay.EndPoint.Port}";

            SocketHelpers.ApplyMultiplayerMode(MultiplayerMode.LanPlay, relay, "10.13.27.2");

            ISocketImpl guest = SocketHelpers.CreateSocket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp, "0");

            Assert.That(guest, Is.InstanceOf<LanPlaySocket>());

            guest.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 4);

            // Talking to something outside the LAN Play network is what creates the host socket.
            using Socket online = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            online.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            online.ReceiveTimeout = Timeout;

            guest.SendTo(Encoding.ASCII.GetBytes("online"), SocketFlags.None, (IPEndPoint)online.LocalEndPoint);

            Assert.That(online.Receive(new byte[16]), Is.EqualTo(6));

            byte[] readBack = new byte[4];

            guest.GetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, readBack);

            Assert.That(BitConverter.ToInt32(readBack), Is.EqualTo(4), "the host socket did not get the options the guest had set");

            guest.Close();
        }

        /// <summary>
        /// The broadcast address of one of the host's IPv4 networks, or zero when it has none.
        /// </summary>
        private static uint FindHostBroadcastAddress()
        {
            foreach (System.Net.NetworkInformation.NetworkInterface adapter in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (System.Net.NetworkInformation.UnicastIPAddressInformation unicastAddress in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (unicastAddress.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(unicastAddress.Address) || unicastAddress.IPv4Mask == null)
                    {
                        continue;
                    }

                    uint mask = NetworkHelpers.ConvertIpv4Address(unicastAddress.IPv4Mask);

                    if (mask is 0 or uint.MaxValue)
                    {
                        continue;
                    }

                    return NetworkHelpers.ConvertIpv4Address(unicastAddress.Address) | ~mask;
                }
            }

            return 0;
        }

        [Test]
        public void HostingALocalSessionMarksLanPlayAsInUse()
        {
            string relay = $"{_relay.EndPoint.Address}:{_relay.EndPoint.Port}";

            SocketHelpers.ApplyMultiplayerMode(MultiplayerMode.LanPlay, relay, "10.13.25.2");

            LanPlayStack stack = SocketHelpers.CurrentLanPlayStack;

            Assert.That(stack.IsGuestActive, Is.False);

            using LanPlayLdnClient client = new(stack);

            Assert.That(client.CreateNetwork(CreateAccessPointRequest("Host"), []), Is.True);
            Assert.That(stack.IsGuestActive, Is.True, "hosting a local session did not mark the LAN Play network as in use");

            client.DisconnectNetwork();
        }

        [Test]
        public void ScatterGatherAndStreamPathsWorkOverLanPlay()
        {
            // These are the paths a game's online code uses: SendMMsg/RecvMMsg for vectored I/O and a
            // stream for TLS. Both used to assume a host socket and would throw (NullReference /
            // InvalidCast) as soon as LAN Play or RyuLDN gave the guest a virtual socket instead, which
            // surfaced as the game being unable to reach the online service.
            string relay = $"{_relay.EndPoint.Address}:{_relay.EndPoint.Port}";

            SocketHelpers.ApplyMultiplayerMode(MultiplayerMode.LanPlay, relay, "10.13.23.2");

            Assert.That(SocketHelpers.CurrentLanPlayStack, Is.Not.Null);

            using LanPlayStack peerStack = LanPlayStack.Create(Parse(relay, "10.13.23.3"));

            LanPlayTcpListener listener = peerStack.NetworkInterface.ListenTcp(5000);

            ManagedSocket guest = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp, "0");

            Assert.That(guest.Socket, Is.InstanceOf<LanPlaySocket>(), "the guest socket did not use the virtual interface");
            Assert.That(guest.Connect(new IPEndPoint(peerStack.NetworkInterface.Address, 5000)), Is.EqualTo(LinuxError.SUCCESS));

            LanPlayTcpConnection peer = listener.Accept(Timeout);

            Assert.That(peer, Is.Not.Null);

            // Vectored send, as a TLS record would be written.
            Assert.That(
                guest.SendMMsg(out int sent, BuildMessage([Encoding.ASCII.GetBytes("hello "), Encoding.ASCII.GetBytes("world")]), BsdSocketFlags.None),
                Is.EqualTo(LinuxError.SUCCESS));
            Assert.That(sent, Is.GreaterThan(0));

            byte[] received = new byte[32];
            int read = peer.Receive(received, Timeout, false, true);

            while (read < 11)
            {
                read += peer.Receive(received.AsSpan(read), Timeout, false, true);
            }

            Assert.That(Encoding.ASCII.GetString(received, 0, read), Is.EqualTo("hello world"));

            // Vectored receive.
            peer.Send(Encoding.ASCII.GetBytes("reply!"));

            Assert.That(WaitFor(() => guest.Socket.Available >= 6), Is.True, "the guest never saw the answer");

            BsdMMsgHdr incoming = BuildMessage([new byte[3], new byte[3]]);

            Assert.That(guest.RecvMMsg(out _, incoming, BsdSocketFlags.None, default), Is.EqualTo(LinuxError.SUCCESS));
            Assert.That(Encoding.ASCII.GetString(incoming.Messages[0].Iov[0]) + Encoding.ASCII.GetString(incoming.Messages[0].Iov[1]), Is.EqualTo("reply!"));

            // The stream the SSL service wraps.
            SocketImplStream stream = new(guest.Socket);

            stream.Write(Encoding.ASCII.GetBytes("over tls"));

            byte[] streamed = new byte[8];
            int streamRead = 0;

            while (streamRead < streamed.Length)
            {
                streamRead += peer.Receive(streamed.AsSpan(streamRead), Timeout, false, true);
            }

            Assert.That(Encoding.ASCII.GetString(streamed), Is.EqualTo("over tls"));

            peer.Send(Encoding.ASCII.GetBytes("from tls"));

            byte[] streamBuffer = new byte[8];
            int fromStream = 0;

            while (fromStream < streamBuffer.Length)
            {
                fromStream += stream.Read(streamBuffer.AsSpan(fromStream));
            }

            Assert.That(Encoding.ASCII.GetString(streamBuffer), Is.EqualTo("from tls"));

            peer.Close();
            listener.Close();
            guest.Socket.Close();
        }

        /// <summary>
        /// Builds a single scatter/gather message with the given segments, the way the guest's raw buffer
        /// would deserialize into one.
        /// </summary>
        private static BsdMMsgHdr BuildMessage(byte[][] segments)
        {
            using MemoryStream raw = new();
            using BinaryWriter writer = new(raw);

            writer.Write((byte)8);      // Header byte, ignored.
            writer.Write(0u);           // Name length.
            writer.Write((uint)segments.Length);

            foreach (byte[] segment in segments)
            {
                writer.Write((ulong)segment.Length);
                writer.Write(segment);
            }

            writer.Write(0u);           // Control length.
            writer.Write((uint)BsdSocketFlags.None);
            writer.Write(0u);           // Length.

            writer.Flush();

            Assert.That(BsdMMsgHdr.Deserialize(out BsdMMsgHdr message, raw.ToArray(), 1), Is.EqualTo(LinuxError.SUCCESS));

            return message;
        }

        [Test]
        public void SwitchingAwayFromRyuLdnReleasesItsSocketProxy()
        {
            // A RyuLDN session registers a socket proxy that takes priority over everything else. Leaving
            // that mode has to release it, or the emulated console would keep sending its traffic into the
            // LDN network instead of out to the host network.
            SocketHelpers.RegisterProxy(new LdnProxy(new ProxyConfig { ProxyIp = 0x0A0A0A0A, ProxySubnetMask = 0xFFFFFF00 }, new StubProxyClient(), new RyuLdnProtocol()));

            ISocketImpl proxied = SocketHelpers.CreateSocket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp, "0");

            Assert.That(proxied, Is.InstanceOf<LdnProxySocket>(), "the RyuLDN proxy was not in use");

            proxied.Close();

            SocketHelpers.ApplyMultiplayerMode(MultiplayerMode.Disabled, string.Empty, string.Empty);

            ISocketImpl afterSwitch = SocketHelpers.CreateSocket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp, "0");

            Assert.That(afterSwitch, Is.InstanceOf<DefaultSocket>(), "guest sockets did not go back to the host stack");

            afterSwitch.Close();
        }

        private sealed class StubProxyClient : IProxyClient
        {
            public bool SendAsync(byte[] buffer) => true;
        }

        [Test]
        public void ReapplyingTheSameSettingsKeepsTheSessionAndChangingThemRejoins()
        {
            string relay = $"{_relay.EndPoint.Address}:{_relay.EndPoint.Port}";

            SocketHelpers.ApplyMultiplayerMode(MultiplayerMode.LanPlay, relay, "10.13.21.2");

            LanPlayStack first = SocketHelpers.CurrentLanPlayStack;

            Assert.That(first, Is.Not.Null);

            // Saving the settings window re-applies everything, even when nothing changed; that must not
            // drop a working session.
            SocketHelpers.ApplyMultiplayerMode(MultiplayerMode.LanPlay, relay, "10.13.21.2");

            Assert.That(SocketHelpers.CurrentLanPlayStack, Is.SameAs(first), "an unchanged configuration rejoined the relay");

            // Actually changing something does rejoin.
            SocketHelpers.ApplyMultiplayerMode(MultiplayerMode.LanPlay, relay, "10.13.21.3");

            LanPlayStack second = SocketHelpers.CurrentLanPlayStack;

            Assert.That(second, Is.Not.Null.And.Not.SameAs(first), "changing the settings did not rejoin");
            Assert.That(second.NetworkInterface.Address.ToString(), Is.EqualTo("10.13.21.3"));
        }

        [Test]
        public void DroppingLanPlayDuringAnLdnSessionFailsGracefully()
        {
            string relay = $"{_relay.EndPoint.Address}:{_relay.EndPoint.Port}";

            SocketHelpers.ApplyMultiplayerMode(MultiplayerMode.LanPlay, relay, "10.13.22.2");

            using LanPlayLdnClient client = new(SocketHelpers.CurrentLanPlayStack);

            Assert.That(client.CreateNetwork(CreateAccessPointRequest("Host"), []), Is.True);

            // The user switches the multiplayer mode off in the middle of a hosted session.
            SocketHelpers.ApplyMultiplayerMode(MultiplayerMode.Disabled, relay, "10.13.22.2");

            // The session is gone, but the guest must get errors and empty results, not exceptions.
            Assert.DoesNotThrow(() =>
            {
                NetworkInfo[] networks = client.Scan(6, new ScanFilter { Flag = ScanFilterFlag.LocalCommunicationId, NetworkId = new NetworkId { IntentId = Intent } });

                Assert.That(networks, Is.Empty);

                client.DisconnectNetwork();
            });
        }

        [Test]
        public void ConnectionTestReportsHostedSessions()
        {
            using LanPlayLdnClient host = new(CreateStack("10.13.10.2"));

            Assert.That(host.CreateNetwork(CreateAccessPointRequest("Host"), []), Is.True);

            LanPlayConnectionTest.Result result = LanPlayConnectionTest.Run($"{_relay.EndPoint.Address}:{_relay.EndPoint.Port}", string.Empty);

            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(result.Message, Does.Contain("10.13.10.2"), result.Message);
            Assert.That(result.Message, Does.Contain("answered the local session scan"), result.Message);

            host.DisconnectNetwork();
        }

        [Test]
        public void ConnectionTestRejectsAnUnusableServer()
        {
            LanPlayConnectionTest.Result result = LanPlayConnectionTest.Run(string.Empty, string.Empty);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("host:port"));
        }

        [Test]
        public void CorruptedPacketsAreDroppedAndCounted()
        {
            using LanPlayStack receiverStack = CreateStack("10.13.8.2");
            using LanPlayStack senderStack = CreateStack("10.13.8.3");

            LanPlayUdpEndpoint receiver = receiverStack.NetworkInterface.BindUdp(LdnPort);

            uint source = NetworkHelpers.ConvertIpv4Address(senderStack.NetworkInterface.Address);
            uint destination = NetworkHelpers.ConvertIpv4Address(receiverStack.NetworkInterface.Address);

            // Built and checksummed here rather than by the code under test, so that a mistake in the
            // stack's own checksum implementation cannot hide.
            byte[] good = BuildUdpPacket(source, destination, LdnPort, LdnPort, Encoding.ASCII.GetBytes("valid"));

            senderStack.Client.SendIpv4(good);

            Assert.That(receiver.WaitForData(Timeout), Is.True, "an externally built packet was not accepted");
            Assert.That(receiver.TryDequeue(out LanPlayUdpEndpoint.Datagram datagram), Is.True);
            Assert.That(Encoding.ASCII.GetString(datagram.Data), Is.EqualTo("valid"));

            // A corrupted IPv4 header checksum, a corrupted UDP checksum, and a packet for somebody else.
            byte[] badHeader = BuildUdpPacket(source, destination, LdnPort, LdnPort, Encoding.ASCII.GetBytes("bad header"));
            badHeader[10] ^= 0xFF;

            byte[] badUdp = BuildUdpPacket(source, destination, LdnPort, LdnPort, Encoding.ASCII.GetBytes("bad udp"));
            badUdp[Ipv4Packet.MinHeaderSize + 7] ^= 0xFF;

            byte[] elsewhere = BuildUdpPacket(source, NetworkHelpers.ConvertIpv4Address(IPAddress.Parse("10.13.8.9")), LdnPort, LdnPort, Encoding.ASCII.GetBytes("not ours"));

            senderStack.Client.SendIpv4(badHeader);
            senderStack.Client.SendIpv4(badUdp);
            senderStack.Client.SendIpv4(elsewhere);

            Assert.That(receiver.WaitForData(500), Is.False, "a corrupted packet was delivered to the guest");

            string report = receiverStack.Client.Diagnostics.GetReport();

            Assert.That(report, Does.Contain("BadHeaderChecksum=1"), report);
            Assert.That(report, Does.Contain("BadTransportChecksum=1"), report);
            Assert.That(report, Does.Contain("NotForUs=1"), report);
        }

        [Test]
        public void DiagnosticsReportCountsTraffic()
        {
            using LanPlayStack stack = CreateStack("10.13.9.2");
            using LanPlayStack peerStack = CreateStack("10.13.9.3");

            LanPlayUdpEndpoint peer = peerStack.NetworkInterface.BindUdp(LdnPort);
            LanPlayUdpEndpoint endpoint = stack.NetworkInterface.BindUdp(LdnPort);

            endpoint.SendTo(new IPEndPoint(peerStack.NetworkInterface.Address, LdnPort), Encoding.ASCII.GetBytes("hello"));

            Assert.That(peer.WaitForData(Timeout), Is.True);

            string sender = stack.Client.Diagnostics.GetReport();
            string receiver = peerStack.Client.Diagnostics.GetReport();

            // The announce ping plus the datagram, and the same seen from the other side.
            Assert.That(sender, Does.Contain("udp 1/"), sender);
            Assert.That(sender, Does.Contain("icmp 1/"), sender);
            Assert.That(receiver, Does.Contain("/1, icmp").Or.Contain("udp 0/1"), receiver);
            Assert.That(receiver, Does.Not.Contain("never heard from the relay"), receiver);
        }

        /// <summary>
        /// Builds a UDP over IPv4 packet with checksums computed independently of the stack under test.
        /// </summary>
        private static byte[] BuildUdpPacket(uint source, uint destination, ushort sourcePort, ushort destinationPort, byte[] payload)
        {
            byte[] packet = new byte[Ipv4Packet.MinHeaderSize + 8 + payload.Length];

            packet[0] = 0x45;
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), (ushort)packet.Length);
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), 0x1234);
            packet[8] = 64;
            packet[9] = 17;
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12), source);
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(16), destination);
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(10), OnesComplementSum(packet.AsSpan(0, Ipv4Packet.MinHeaderSize)));

            Span<byte> datagram = packet.AsSpan(Ipv4Packet.MinHeaderSize);

            BinaryPrimitives.WriteUInt16BigEndian(datagram, sourcePort);
            BinaryPrimitives.WriteUInt16BigEndian(datagram[2..], destinationPort);
            BinaryPrimitives.WriteUInt16BigEndian(datagram[4..], (ushort)(8 + payload.Length));
            payload.CopyTo(datagram[8..]);

            byte[] pseudo = new byte[12 + datagram.Length];

            BinaryPrimitives.WriteUInt32BigEndian(pseudo, source);
            BinaryPrimitives.WriteUInt32BigEndian(pseudo.AsSpan(4), destination);
            pseudo[9] = 17;
            BinaryPrimitives.WriteUInt16BigEndian(pseudo.AsSpan(10), (ushort)datagram.Length);
            datagram.CopyTo(pseudo.AsSpan(12));

            ushort checksum = OnesComplementSum(pseudo);

            BinaryPrimitives.WriteUInt16BigEndian(datagram[6..], checksum == 0 ? (ushort)0xFFFF : checksum);

            return packet;
        }

        private static ushort OnesComplementSum(ReadOnlySpan<byte> data)
        {
            uint sum = 0;

            for (int i = 0; i < data.Length; i += 2)
            {
                sum += (uint)(data[i] << 8) | (i + 1 < data.Length ? data[i + 1] : 0u);

                while (sum > 0xFFFF)
                {
                    sum = (sum & 0xFFFF) + (sum >> 16);
                }
            }

            return (ushort)~sum;
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
