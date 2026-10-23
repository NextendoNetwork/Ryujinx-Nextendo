using NUnit.Framework;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd.Impl;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd.Types;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Ryujinx.Tests.HLE
{
    public class DeferredDatagramReceiveTests
    {
        [Test]
        public void BlockingDatagramYieldsThenReturnsRealPayload()
        {
            using ManagedSocket receiver = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp, "0");
            Assert.That(receiver.Bind(new IPEndPoint(IPAddress.Loopback, 0)), Is.EqualTo(LinuxError.SUCCESS));
            byte[] bytes = new byte[64];
            Stopwatch clock = Stopwatch.StartNew();
            LinuxError error = DeferredDatagramReceive.Receive(receiver, bytes, BsdSocketFlags.None, out _, out _, out bool pending);
            Assert.Multiple(() =>
            {
                Assert.That(error, Is.EqualTo(LinuxError.EWOULDBLOCK));
                Assert.That(pending, Is.True);
                Assert.That(receiver.Blocking, Is.True);
                Assert.That(clock.ElapsedMilliseconds, Is.LessThan(1000));
                Assert.That(DeferredDatagramReceive.Deadline(receiver, 100), Is.EqualTo(long.MaxValue));
            });
            using Socket sender = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            sender.SendTo(new byte[] { 1, 7, 9 }, receiver.LocalEndPoint);
            Assert.That(receiver.Poll(1000000, SelectMode.SelectRead), Is.True);
            error = DeferredDatagramReceive.Receive(receiver, bytes, BsdSocketFlags.None, out int size, out _, out pending);
            Assert.Multiple(() =>
            {
                Assert.That(error, Is.EqualTo(LinuxError.SUCCESS));
                Assert.That(pending, Is.False);
                Assert.That(size, Is.EqualTo(3));
                Assert.That(bytes.AsSpan(0, size).ToArray(), Is.EqualTo(new byte[] { 1, 7, 9 }));
                Assert.That(receiver.Blocking, Is.True);
            });
            sender.SendTo(Array.Empty<byte>(), receiver.LocalEndPoint);
            Assert.That(receiver.Poll(1000000, SelectMode.SelectRead), Is.True);
            error = DeferredDatagramReceive.Receive(receiver, bytes, BsdSocketFlags.None, out size, out _, out pending);
            Assert.That(error, Is.EqualTo(LinuxError.SUCCESS));
            Assert.That(size, Is.Zero);
            Assert.That(pending, Is.False);
        }

        [Test]
        public void ExplicitNonblockingReceiveIsNotDeferred()
        {
            using ManagedSocket receiver = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp, "0");
            receiver.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            LinuxError error = DeferredDatagramReceive.Receive(receiver, new byte[64], BsdSocketFlags.DontWait, out _, out _, out bool pending);
            Assert.That(error, Is.EqualTo(LinuxError.EWOULDBLOCK));
            Assert.That(pending, Is.False);
            Assert.That(receiver.Blocking, Is.True);
        }

        [Test]
        public void NonblockingSocketAndReceiveTimeoutArePreserved()
        {
            using ManagedSocket receiver = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp, "0");
            receiver.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            Assert.That(receiver.SetSocketOption(BsdSocketOption.SoRcvTimeo, SocketOptionLevel.Socket,
                BitConverter.GetBytes(250)), Is.EqualTo(LinuxError.SUCCESS));
            Assert.That(DeferredDatagramReceive.Deadline(receiver, 100), Is.EqualTo(350));
            receiver.Blocking = false;
            LinuxError error = DeferredDatagramReceive.Receive(receiver, new byte[64], BsdSocketFlags.None, out _, out _, out bool pending);
            Assert.That(error, Is.EqualTo(LinuxError.EWOULDBLOCK));
            Assert.That(pending, Is.False);
            Assert.That(receiver.Blocking, Is.False);
        }
    }
}
