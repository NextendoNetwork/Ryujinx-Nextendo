using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnRyu.Proxy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.HLE.HOS.Services.Sockets.Bsd.Proxy
{
    static class SocketHelpers
    {
        private static LdnProxy _proxy;
        private static LanPlayStack _lanPlay;
        private static string _lanPlayServer;
        private static string _lanPlayVirtualAddress;
        private static bool _lanPlayFailed;

        private static readonly Lock _lanPlayLock = new();

        /// <summary>
        /// The LAN Play stack of the running session, or null when LAN Play is not in use. It is created on
        /// first use, so that selecting LAN Play does not connect to a relay until the game does something
        /// with the network.
        /// </summary>
        public static LanPlayStack CurrentLanPlayStack
        {
            get
            {
                if (_lanPlay != null)
                {
                    return _lanPlay;
                }

                if (_lanPlayServer == null || _lanPlayFailed)
                {
                    return null;
                }

                lock (_lanPlayLock)
                {
                    if (_lanPlay != null || _lanPlayFailed)
                    {
                        return _lanPlay;
                    }

                    try
                    {
                        if (!LanPlayConfiguration.TryParse(_lanPlayServer, _lanPlayVirtualAddress, out LanPlayConfiguration configuration))
                        {
                            throw new InvalidOperationException("The LAN Play relay server is not configured correctly.");
                        }

                        _lanPlay = LanPlayStack.Create(configuration);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error?.Print(LogClass.ServiceLdn, $"Could not join the LAN Play relay: {ex.Message}");

                        _lanPlayFailed = true;
                    }

                    return _lanPlay;
                }
            }
        }

        /// <summary>
        /// Enables LAN Play for this emulation session. The relay is only contacted on first use.
        /// </summary>
        public static void ConfigureLanPlay(string server, string virtualAddress)
        {
            lock (_lanPlayLock)
            {
                _lanPlayServer = server;
                _lanPlayVirtualAddress = virtualAddress;
                _lanPlayFailed = false;
            }

            // Joining involves a name lookup and probing for a free virtual address, so it is done ahead of
            // time on a background thread: the guest thread that opens the first socket should not wait for it.
            Task.Run(() => _ = CurrentLanPlayStack);
        }

        /// <summary>
        /// Disconnects from the LAN Play relay and forgets its configuration.
        /// </summary>
        public static void ShutdownLanPlay()
        {
            lock (_lanPlayLock)
            {
                _lanPlay?.Dispose();
                _lanPlay = null;
                _lanPlayServer = null;
                _lanPlayVirtualAddress = null;
                _lanPlayFailed = false;
            }
        }

        public static void Select(List<ISocketImpl> readEvents, List<ISocketImpl> writeEvents, List<ISocketImpl> errorEvents, int timeout)
        {
            List<Socket> readDefault = readEvents.Select(x => (x as DefaultSocket)?.BaseSocket).Where(x => x != null).ToList();
            List<Socket> writeDefault = writeEvents.Select(x => (x as DefaultSocket)?.BaseSocket).Where(x => x != null).ToList();
            List<Socket> errorDefault = errorEvents.Select(x => (x as DefaultSocket)?.BaseSocket).Where(x => x != null).ToList();

            if (readDefault.Count != 0 || writeDefault.Count != 0 || errorDefault.Count != 0)
            {
                Socket.Select(readDefault, writeDefault, errorDefault, timeout);
            }

            void FilterSockets(List<ISocketImpl> removeFrom, List<Socket> selectedSockets, Func<IPollableSocket, bool> pollableCheck)
            {
                removeFrom.RemoveAll(socket =>
                {
                    switch (socket)
                    {
                        case DefaultSocket dsocket:
                            return !selectedSockets.Contains(dsocket.BaseSocket);
                        case IPollableSocket psocket:
                            return !pollableCheck(psocket);
                        default:
                            throw new NotImplementedException();
                    }
                });
            }

            FilterSockets(readEvents, readDefault, (socket) => socket.Readable);
            FilterSockets(writeEvents, writeDefault, (socket) => socket.Writable);
            FilterSockets(errorEvents, errorDefault, (socket) => socket.Error);
        }

        public static void RegisterProxy(LdnProxy proxy)
        {
            if (_proxy != null)
            {
                UnregisterProxy();
            }

            _proxy = proxy;
        }

        public static void UnregisterProxy()
        {
            _proxy?.Dispose();
            _proxy = null;
        }

        public static ISocketImpl CreateSocket(AddressFamily domain, SocketType type, ProtocolType protocol, string lanInterfaceId)
        {
            if (_proxy != null)
            {
                if (_proxy.Supported(domain, type, protocol))
                {
                    Logger.Info?.PrintMsg(LogClass.ServiceBsd, $"Socket is using LDN proxy");
                    return new LdnProxySocket(domain, type, protocol, _proxy);
                }
                else
                {
                    Logger.Warning?.PrintMsg(LogClass.ServiceBsd, $"LDN proxy does not support socket {domain}, {type}, {protocol}");
                }
            }
            else if (_lanPlayServer != null && CurrentLanPlayStack is { } lanPlay)
            {
                if (lanPlay.Supported(domain, type, protocol))
                {
                    Logger.Info?.PrintMsg(LogClass.ServiceBsd, $"Socket is using the LAN Play network interface");

                    return lanPlay.CreateSocket(domain, type, protocol, lanInterfaceId);
                }

                Logger.Warning?.PrintMsg(LogClass.ServiceBsd,
                    $"LAN Play cannot carry socket {domain}, {type}, {protocol}; it will use the host networking stack instead");
            }
            else
            {
                Logger.Info?.PrintMsg(LogClass.ServiceBsd, $"Opening socket using host networking stack");
            }

            return new DefaultSocket(domain, type, protocol, lanInterfaceId);
        }
    }
}
