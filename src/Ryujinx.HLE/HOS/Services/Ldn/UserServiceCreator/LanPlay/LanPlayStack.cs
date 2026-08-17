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

        /// <summary>
        /// True once the emulated console has actually used the LAN Play network: it joined or hosted an LDN
        /// session, or one of its sockets exchanged traffic on it (which is what entering a game's own LAN
        /// mode does).
        /// <para>
        /// Until then LAN Play stays completely out of the way — in particular the console keeps reporting
        /// its host address — so that having LAN Play selected does not change anything for online play.
        /// </para>
        /// </summary>
        public bool IsGuestActive { get; private set; }

        /// <summary>
        /// Called by the sockets and by the LDN client the first time the guest uses the LAN Play network.
        /// </summary>
        public void MarkGuestActive(string reason)
        {
            if (IsGuestActive)
            {
                return;
            }

            IsGuestActive = true;

            Logger.Info?.Print(LogClass.ServiceLdn,
                $"LAN Play: the game started using the LAN Play network ({reason}). " +
                $"The console now presents {NetworkInterface.Address} to it; its other traffic keeps using the host network.");
        }

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

                Logger.Info?.Print(LogClass.ServiceLdn,
                    "LAN Play: online features are unaffected. Everything that is not addressed to the LAN Play " +
                    "network keeps using the host network, and the console only presents its LAN Play address once " +
                    "the game enters a local or LAN mode.");

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
            return new LanPlaySocket(domain, type, protocol, this, lanInterfaceId);
        }

        public void Dispose()
        {
            NetworkInterface.Dispose();
            Client.Dispose();
        }
    }
}
