using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay.Proxy;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd.Proxy;
using System;
using System.Net.Sockets;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay
{
    /// <summary>
    /// The LAN Play networking stack of one emulation session: the relay client, the virtual network
    /// interface built on top of it, and the sockets the emulated console opens through it.
    /// </summary>
    class LanPlayStack : IDisposable
    {
        public LanPlayClient Client { get; }

        public LanPlayNetworkInterface NetworkInterface { get; }

        public bool IsConnected => Client.IsRunning;

        private LanPlayStack(LanPlayClient client, LanPlayNetworkInterface networkInterface)
        {
            Client = client;
            NetworkInterface = networkInterface;
        }

        /// <summary>
        /// Connects to the relay and picks the virtual address of the emulated console.
        /// </summary>
        public static LanPlayStack Create(LanPlayConfiguration configuration)
        {
            LanPlayClient client = new(configuration);

            try
            {
                client.Start();

                uint address = VirtualAddressAllocator.Allocate(client, configuration.VirtualAddress);

                LanPlayNetworkInterface networkInterface = new(client, address);

                networkInterface.Announce();

                Logger.Info?.Print(LogClass.ServiceLdn,
                    $"LAN Play: the emulated console is {networkInterface.Address} on the {configuration.RelayEndPoint} network. The host's own network configuration is untouched.");

                return new LanPlayStack(client, networkInterface);
            }
            catch (Exception)
            {
                client.Dispose();

                throw;
            }
        }

        /// <summary>
        /// True for the socket kinds the virtual network interface can carry. Everything else is left to
        /// the host networking stack.
        /// </summary>
        public bool Supported(AddressFamily domain, SocketType type, ProtocolType protocol)
        {
            return domain == AddressFamily.InterNetwork && protocol is ProtocolType.Udp or ProtocolType.Tcp;
        }

        public ISocketImpl CreateSocket(AddressFamily domain, SocketType type, ProtocolType protocol, string lanInterfaceId)
        {
            return new LanPlaySocket(domain, type, protocol, NetworkInterface, lanInterfaceId);
        }

        public void Dispose()
        {
            NetworkInterface.Dispose();
            Client.Dispose();
        }
    }
}
