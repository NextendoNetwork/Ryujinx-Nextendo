using Ryujinx.Common.Logging;
using Ryujinx.Common.Utilities;
using Ryujinx.HLE.HOS.Services.Ldn.Types;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay.Proxy;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.Types;
using System;
using System.Text;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay
{
    /// <summary>
    /// Network client for <see cref="Common.Configuration.Multiplayer.MultiplayerMode.LanPlay"/>.
    /// <para>
    /// It speaks the same ldn_mitm protocol as <see cref="LdnMitmClient"/>, so a real console running
    /// ldn_mitm sees Ryujinx as just another console, but the packets travel over an embedded
    /// switch-lan-play client instead of the host network interface.
    /// </para>
    /// </summary>
    internal class LanPlayLdnClient : INetworkClient, ILdnDiscoveryClient
    {
        private readonly LanPlayStack _stack;
        private readonly LanDiscovery _lanDiscovery;

        public ProxyConfig Config { get; }

        public bool NeedsRealId => false;

        public event EventHandler<NetworkChangeEventArgs> NetworkChange;

        public LanPlayLdnClient(LanPlayStack stack)
        {
            _stack = stack;

            Config = new ProxyConfig
            {
                ProxyIp = _stack.NetworkInterface.AddressV4,
                ProxySubnetMask = NetworkHelpers.ConvertIpv4Address(_stack.NetworkInterface.SubnetMask),
            };

            _lanDiscovery = new LanDiscovery(this, new LanPlayLdnNetworkProvider(_stack.NetworkInterface));
        }

        public void InvokeNetworkChange(NetworkInfo info, bool connected, DisconnectReason reason = DisconnectReason.None)
        {
            Logger.Info?.Print(LogClass.ServiceLdn,
                $"LAN Play LDN: network {(connected ? "update" : $"lost ({reason})")}, {info.Ldn.NodeCount} node(s): {DescribeNodes(info)}");

            NetworkChange?.Invoke(this, new NetworkChangeEventArgs(info, connected: connected, disconnectReason: reason));
        }

        private static string DescribeNodes(NetworkInfo info)
        {
            StringBuilder description = new();

            foreach (NodeInfo node in info.Ldn.Nodes.AsSpan())
            {
                if (node.IsConnected == 0)
                {
                    continue;
                }

                if (description.Length != 0)
                {
                    description.Append(", ");
                }

                description.Append($"#{node.NodeId} {NetworkHelpers.ConvertUint(node.Ipv4Address)}");
            }

            return description.Length == 0 ? "none" : description.ToString();
        }

        public NetworkError Connect(ConnectRequest request)
        {
            _stack.MarkGuestActive("joining a local session");

            NetworkError error = _lanDiscovery.Connect(request.NetworkInfo, request.UserConfig, request.LocalCommunicationVersion);

            if (error != NetworkError.None)
            {
                Logger.Warning?.Print(LogClass.ServiceLdn,
                    $"LAN Play LDN: could not join the session hosted by {NetworkHelpers.ConvertUint(request.NetworkInfo.Ldn.Nodes[0].Ipv4Address)}: {error}. " +
                    $"Relay state: {_stack.Client.Diagnostics.GetReport()}");
            }

            return error;
        }

        public NetworkError ConnectPrivate(ConnectPrivateRequest request)
        {
            // NOTE: As in ldn_mitm, private networks are not implemented.
            Logger.Stub?.PrintMsg(LogClass.ServiceLdn, "LanPlayLdnClient ConnectPrivate");

            return NetworkError.None;
        }

        public bool CreateNetwork(CreateAccessPointRequest request, byte[] advertiseData)
        {
            _stack.MarkGuestActive("hosting a local session");

            bool created = _lanDiscovery.CreateNetwork(request.SecurityConfig, request.UserConfig, request.NetworkConfig);

            if (!created)
            {
                Logger.Error?.Print(LogClass.ServiceLdn, "LAN Play LDN: could not host a session on the virtual network interface.");
            }

            return created;
        }

        public bool CreateNetworkPrivate(CreateAccessPointPrivateRequest request, byte[] advertiseData)
        {
            Logger.Stub?.PrintMsg(LogClass.ServiceLdn, "LanPlayLdnClient CreateNetworkPrivate");

            return true;
        }

        public void DisconnectAndStop()
        {
            _lanDiscovery.DisconnectAndStop();
        }

        public void DisconnectNetwork()
        {
            _lanDiscovery.DestroyNetwork();
        }

        public ResultCode Reject(DisconnectReason disconnectReason, uint nodeId)
        {
            Logger.Stub?.PrintMsg(LogClass.ServiceLdn, "LanPlayLdnClient Reject");

            return ResultCode.Success;
        }

        public NetworkInfo[] Scan(ushort channel, ScanFilter scanFilter)
        {
            NetworkInfo[] networks = _lanDiscovery.Scan(channel, scanFilter);

            Logger.Info?.Print(LogClass.ServiceLdn,
                networks.Length == 0
                    ? "LAN Play LDN: scan found no sessions on the relay. Other players must use the same relay and the same game version."
                    : $"LAN Play LDN: scan found {networks.Length} session(s) on the relay.");

            return networks;
        }

        public void SetAdvertiseData(byte[] data)
        {
            _lanDiscovery.SetAdvertiseData(data);
        }

        public void SetGameVersion(ReadOnlySpan<byte> versionString)
        {
            Logger.Stub?.PrintMsg(LogClass.ServiceLdn, "LanPlayLdnClient SetGameVersion");
        }

        public void SetStationAcceptPolicy(AcceptPolicy acceptPolicy)
        {
            Logger.Stub?.PrintMsg(LogClass.ServiceLdn, "LanPlayLdnClient SetStationAcceptPolicy");
        }

        public void Dispose()
        {
            // The stack itself belongs to the emulation session, not to this client, because the game may
            // keep using the LAN Play network through its own sockets after LDN is torn down.
            _lanDiscovery.Dispose();
        }
    }
}
