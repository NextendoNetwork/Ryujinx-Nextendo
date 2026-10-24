using NUnit.Framework;
using Ryujinx.HLE;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd.Types;
using Ryujinx.HLE.HOS.Services.Ssl.SslService;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Ryujinx.Tests.HLE
{
    class UchOnlineTests
    {
        private const ulong OtherTitle = 0x0100B6E012EBE000;

        [SetUp]
        public void ForgetAnyTicket() => UchAccountTransport.Clear();

        [Test]
        public void SocketSelectorIsUdpOnlyForUchAndExactlyThatValue()
        {
            Assert.IsTrue(UchOnline.IsUdpSelector(UchOnline.Title, 0x10000000));
            Assert.IsFalse(UchOnline.IsUdpSelector(UchOnline.Title, 0x10000001));
            Assert.IsFalse(UchOnline.IsUdpSelector(UchOnline.Title, 2));
            Assert.IsFalse(UchOnline.IsUdpSelector(OtherTitle, 0x10000000));
        }

        [Test]
        public void ServiceHostsAreExactlyTheFourUchNames()
        {
            foreach (string host in new[]
            {
                "api.braincloudservers.com",
                "na.ultimatechickenhorseserver.com",
                "EU.ultimatechickenhorseserver.com",
                "ap.ultimatechickenhorseserver.com",
            })
            {
                Assert.IsTrue(UchOnline.IsServiceHost(host), host);
            }

            foreach (string host in new[] { null, "", "braincloudservers.com", "example.com", "public-ubiservices.ubi.com", "x.api.braincloudservers.com" })
            {
                Assert.IsFalse(UchOnline.IsServiceHost(host), host);
            }
        }

        [Test]
        public void ConnectionTrackerReleasesEachSlotExactlyOnce()
        {
            SslConnectionTracker tracker = new();

            Action first = tracker.Register();
            Action second = tracker.Register();

            Assert.AreEqual(2u, tracker.Count);

            first();
            first();

            Assert.AreEqual(1u, tracker.Count);

            second();

            Assert.AreEqual(0u, tracker.Count);
        }

        [Test]
        public void ResponseObserverDeliversTheJsonBodyOfASplitChunkedResponse()
        {
            List<string> seen = new();
            UchResponseObserver observer = new(root => seen.Add(root.GetProperty("marker").GetString()));

            string body = "{\"marker\":\"ok\"}";
            byte[] response = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
                body.Length.ToString("x") + "\r\n" + body + "\r\n0\r\n\r\n");

            observer.Observe(response.AsSpan(0, 30));
            Assert.IsEmpty(seen, "a partial response must not be delivered");

            observer.Observe(response.AsSpan(30));
            Assert.AreEqual(new[] { "ok" }, seen);
        }

        [Test]
        public void ResponseObserverIgnoresNonJsonAndGarbage()
        {
            int calls = 0;
            UchResponseObserver observer = new(_ => calls++);

            observer.Observe(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello"));
            observer.Observe(new byte[] { 0, 1, 2, 3, 255 });

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void TicketFrameIsSentFromTheGameplaySocketBeforeTheConnectRequest()
        {
            byte[] ticket = new byte[32];

            for (int i = 0; i < ticket.Length; i++)
            {
                ticket[i] = (byte)(i + 1);
            }

            string encoded = Convert.ToBase64String(ticket).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            long expires = DateTimeOffset.UtcNow.AddSeconds(20).ToUnixTimeMilliseconds();

            using JsonDocument login = JsonDocument.Parse(
                "{\"nextendoTransport\":{\"protocol\":\"NXU1\",\"ticket\":\"" + encoded + "\",\"expiresMs\":" + expires +
                ",\"relayIP\":\"203.0.113.10\",\"relayPort\":18888}}");

            UchAccountTransport.Observe(login.RootElement);

            IPEndPoint relay = new(IPAddress.Parse("203.0.113.10"), 18888);
            byte[] connect = ConnectRequest();
            RecordingSocket socket = new();

            // Another title never gets a ticket frame.
            Assert.IsFalse(UchAccountTransport.Bootstrap(OtherTitle, socket, connect, BsdSocketFlags.None, relay, out _));
            Assert.AreEqual(0, socket.Sent.Count);

            // Not the connect request: untouched.
            Assert.IsFalse(UchAccountTransport.Bootstrap(UchOnline.Title, socket, new byte[19], BsdSocketFlags.None, relay, out _));

            // A connect request to some other address is refused rather than leaking the ticket.
            Assert.IsTrue(UchAccountTransport.Bootstrap(UchOnline.Title, socket, connect, BsdSocketFlags.None,
                new IPEndPoint(IPAddress.Parse("198.51.100.7"), 18888), out LinuxError wrongDestination));
            Assert.AreEqual(LinuxError.EACCES, wrongDestination);
            Assert.AreEqual(0, socket.Sent.Count);

            Assert.IsTrue(UchAccountTransport.Bootstrap(UchOnline.Title, socket, connect, BsdSocketFlags.None, relay, out LinuxError error));
            Assert.AreEqual(LinuxError.SUCCESS, error);
            Assert.AreEqual(1, socket.Sent.Count);

            byte[] frame = socket.Sent[0];

            Assert.AreEqual(36, frame.Length);
            Assert.AreEqual("NXU1", Encoding.ASCII.GetString(frame, 0, 4));
            CollectionAssert.AreEqual(ticket, frame[4..]);
        }

        [Test]
        public void ConnectRequestWithoutATicketIsRefused()
        {
            // An expired ticket is as good as none.
            long expired = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeMilliseconds();
            string encoded = Convert.ToBase64String(new byte[32]).TrimEnd('=').Replace('+', '-').Replace('/', '_');

            using JsonDocument login = JsonDocument.Parse(
                "{\"nextendoTransport\":{\"protocol\":\"NXU1\",\"ticket\":\"" + encoded + "\",\"expiresMs\":" + expired +
                ",\"relayIP\":\"203.0.113.10\",\"relayPort\":18888}}");

            UchAccountTransport.Observe(login.RootElement); // rejected: already expired, so no ticket is stored

            RecordingSocket socket = new();
            bool handled = UchAccountTransport.Bootstrap(UchOnline.Title, socket, ConnectRequest(), BsdSocketFlags.None,
                new IPEndPoint(IPAddress.Parse("203.0.113.10"), 18888), out LinuxError error);

            Assert.IsTrue(handled);
            Assert.AreEqual(LinuxError.EACCES, error);
            Assert.AreEqual(0, socket.Sent.Count, "nothing may be sent for a request that cannot be admitted");
        }

        private static byte[] ConnectRequest()
        {
            byte[] packet = new byte[19];
            packet[2] = 1;
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(11, 4), 16777984);
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(15, 4), 2944057210);

            return packet;
        }

        private sealed class RecordingSocket : ISocket
        {
            public List<byte[]> Sent { get; } = new();

            public IPEndPoint RemoteEndPoint => null;
            public IPEndPoint LocalEndPoint => null;
            public AddressFamily AddressFamily => AddressFamily.InterNetwork;
            public SocketType SocketType => SocketType.Dgram;
            public ProtocolType ProtocolType => ProtocolType.Udp;
            public nint Handle => 0;
            public bool Blocking { get; set; }
            public int Refcount { get; set; }

            public LinuxError SendTo(out int sendSize, ReadOnlySpan<byte> buffer, int size, BsdSocketFlags flags, IPEndPoint remoteEndPoint)
            {
                Sent.Add(buffer.ToArray());
                sendSize = buffer.Length;

                return LinuxError.SUCCESS;
            }

            public LinuxError Read(out int readSize, Span<byte> buffer) => throw new NotSupportedException();
            public LinuxError Write(out int writeSize, ReadOnlySpan<byte> buffer) => throw new NotSupportedException();
            public LinuxError Receive(out int receiveSize, Span<byte> buffer, BsdSocketFlags flags) => throw new NotSupportedException();
            public LinuxError ReceiveFrom(out int receiveSize, Span<byte> buffer, int size, BsdSocketFlags flags, out IPEndPoint remoteEndPoint) => throw new NotSupportedException();
            public LinuxError Send(out int sendSize, ReadOnlySpan<byte> buffer, BsdSocketFlags flags) => throw new NotSupportedException();
            public LinuxError RecvMMsg(out int vlen, BsdMMsgHdr message, BsdSocketFlags flags, TimeVal timeout) => throw new NotSupportedException();
            public LinuxError SendMMsg(out int vlen, BsdMMsgHdr message, BsdSocketFlags flags) => throw new NotSupportedException();
            public LinuxError GetSocketOption(BsdSocketOption option, SocketOptionLevel level, Span<byte> optionValue) => throw new NotSupportedException();
            public LinuxError SetSocketOption(BsdSocketOption option, SocketOptionLevel level, ReadOnlySpan<byte> optionValue) => throw new NotSupportedException();
            public bool Poll(int microSeconds, SelectMode mode) => throw new NotSupportedException();
            public LinuxError Bind(IPEndPoint localEndPoint) => throw new NotSupportedException();
            public LinuxError Connect(IPEndPoint remoteEndPoint) => throw new NotSupportedException();
            public LinuxError Listen(int backlog) => throw new NotSupportedException();
            public LinuxError Accept(out ISocket newSocket) => throw new NotSupportedException();
            public void Disconnect() => throw new NotSupportedException();
            public LinuxError Shutdown(BsdSocketShutdownFlags how) => throw new NotSupportedException();
            public void Close() { }
            public void Dispose() { }
        }
    }
}
