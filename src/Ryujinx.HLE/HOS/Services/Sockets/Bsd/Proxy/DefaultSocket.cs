using Ryujinx.Common.Utilities;
using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Ryujinx.HLE.HOS.Services.Sockets.Bsd.Proxy
{
    class DefaultSocket : ISocketImpl
    {
        public Socket BaseSocket { get; }

        public EndPoint RemoteEndPoint => BaseSocket.RemoteEndPoint;

        public EndPoint LocalEndPoint => BaseSocket.LocalEndPoint;

        public bool Connected => BaseSocket.Connected;

        public bool IsBound => BaseSocket.IsBound;

        public AddressFamily AddressFamily => BaseSocket.AddressFamily;

        public SocketType SocketType => BaseSocket.SocketType;

        public ProtocolType ProtocolType => BaseSocket.ProtocolType;

        public bool Blocking { get => BaseSocket.Blocking; set => BaseSocket.Blocking = value; }

        public int Available => BaseSocket.Available;

        private readonly string _lanInterfaceId;

        public DefaultSocket(Socket baseSocket, string lanInterfaceId)
        {
            _lanInterfaceId = lanInterfaceId;

            BaseSocket = baseSocket;

            DisableUdpConnReset(BaseSocket);
        }

        public DefaultSocket(AddressFamily domain, SocketType type, ProtocolType protocol, string lanInterfaceId)
        {
            _lanInterfaceId = lanInterfaceId;

            BaseSocket = new Socket(domain, type, protocol);

            EnableDualStack(domain, BaseSocket);

            DisableUdpConnReset(BaseSocket);
        }

        // [Nextendo] On Windows a UDP socket raises WSAECONNRESET (10054) on the NEXT recv when a
        // prior sendto drew back an ICMP "port unreachable" — behaviour that does NOT exist on a
        // real Switch. Pia's NAT-check / P2P sprays UDP probes (some to ports with no listener),
        // so this spurious reset was surfaced to the game as ECONNRESET, which S2/MK8 read as the
        // network dropping -> it closed the online session -> "A communication error has occurred".
        // SIO_UDP_CONNRESET = false disables it so recv keeps working after an unreachable probe.
        private static void DisableUdpConnReset(Socket socket)
        {
            if (!OperatingSystem.IsWindows() || socket.SocketType != SocketType.Dgram)
            {
                return;
            }

            try
            {
                const int SIO_UDP_CONNRESET = unchecked((int)0x9800000C);

                socket.IOControl(SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
            }
            catch (SocketException)
            {
                // Best-effort: ignore if the platform/socket doesn't support this control code.
            }
        }

        // [Nextendo] IPv6 sockets must be dual-stack so the guest can reach an IPv4 target.
        // Our DNS-MITM redirects Nintendo hostnames to an IPv4 server (e.g. a Nintendo service
        // hostname -> 203.0.113.1), and Sfdnsres only ever hands the guest IPv4 addrinfo. But some
        // games' online client opens an AF_INET6 socket and connects to the IPv4 address mapped to
        // ::ffff:203.0.113.1. A default .NET IPv6 socket is V6ONLY, so that connect fails
        // instantly (surfaced to the game as ETIMEDOUT) and the online handshake never starts.
        // Enabling dual-stack makes the mapped-IPv4 connect succeed, matching real-hardware
        // behaviour where the socket layer bridges v4/v6 transparently.
        private static void EnableDualStack(AddressFamily domain, Socket socket)
        {
            if (domain != AddressFamily.InterNetworkV6)
            {
                return;
            }

            try
            {
                socket.DualMode = true;
            }
            catch (SocketException)
            {
                // Best-effort: platform without dual-stack support.
            }
            catch (NotSupportedException)
            {
                // Best-effort: socket type that doesn't support dual-stack.
            }
        }

        private void EnsureNetworkInterfaceBound()
        {
            if (_lanInterfaceId != "0" && !BaseSocket.IsBound)
            {
                (_, UnicastIPAddressInformation ipInfo) = NetworkHelpers.GetLocalInterface(_lanInterfaceId);

                BaseSocket.Bind(new IPEndPoint(ipInfo.Address, 0));
            }
        }

        public ISocketImpl Accept()
        {
            return new DefaultSocket(BaseSocket.Accept(), _lanInterfaceId);
        }

        public void Bind(EndPoint localEP)
        {
            // NOTE: The guest is able to receive on 0.0.0.0 without it being limited to the chosen network interface.
            // This is because it must get loopback traffic as well. This could allow other network traffic to leak in.

            BaseSocket.Bind(localEP);
        }

        public void Close()
        {
            BaseSocket.Close();
        }

        public void Connect(EndPoint remoteEP)
        {
            EnsureNetworkInterfaceBound();

            BaseSocket.Connect(remoteEP);
        }

        public void Disconnect(bool reuseSocket)
        {
            BaseSocket.Disconnect(reuseSocket);
        }

        public void GetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, byte[] optionValue)
        {
            BaseSocket.GetSocketOption(optionLevel, optionName, optionValue);
        }

        public void Listen(int backlog)
        {
            BaseSocket.Listen(backlog);
        }

        public int Receive(Span<byte> buffer)
        {
            EnsureNetworkInterfaceBound();

            return BaseSocket.Receive(buffer);
        }

        public int Receive(Span<byte> buffer, SocketFlags flags)
        {
            EnsureNetworkInterfaceBound();

            return BaseSocket.Receive(buffer, flags);
        }

        public int Receive(Span<byte> buffer, SocketFlags flags, out SocketError socketError)
        {
            EnsureNetworkInterfaceBound();

            return BaseSocket.Receive(buffer, flags, out socketError);
        }

        public int ReceiveFrom(Span<byte> buffer, SocketFlags flags, ref EndPoint remoteEP)
        {
            EnsureNetworkInterfaceBound();

            return BaseSocket.ReceiveFrom(buffer, flags, ref remoteEP);
        }

        public int Send(ReadOnlySpan<byte> buffer)
        {
            EnsureNetworkInterfaceBound();

            return BaseSocket.Send(buffer);
        }

        public int Send(ReadOnlySpan<byte> buffer, SocketFlags flags)
        {
            EnsureNetworkInterfaceBound();

            return BaseSocket.Send(buffer, flags);
        }

        public int Send(ReadOnlySpan<byte> buffer, SocketFlags flags, out SocketError socketError)
        {
            EnsureNetworkInterfaceBound();

            return BaseSocket.Send(buffer, flags, out socketError);
        }

        public int SendTo(ReadOnlySpan<byte> buffer, SocketFlags flags, EndPoint remoteEP)
        {
            EnsureNetworkInterfaceBound();

            return BaseSocket.SendTo(buffer, flags, remoteEP);
        }

        public bool Poll(int microSeconds, SelectMode mode)
        {
            return BaseSocket.Poll(microSeconds, mode);
        }

        public void SetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, int optionValue)
        {
            BaseSocket.SetSocketOption(optionLevel, optionName, optionValue);
        }

        public void SetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, object optionValue)
        {
            BaseSocket.SetSocketOption(optionLevel, optionName, optionValue);
        }

        public void Shutdown(SocketShutdown how)
        {
            BaseSocket.Shutdown(how);
        }

        public void Dispose()
        {
            BaseSocket.Dispose();
        }
    }
}
