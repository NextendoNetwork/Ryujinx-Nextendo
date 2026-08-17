using Ryujinx.Common.Logging;
using Ryujinx.Common.Utilities;
using Ryujinx.HLE.HOS.Services.Ldn.Types;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay.Proxy;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.Types;
using System;

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
            NetworkChange?.Invoke(this, new NetworkChangeEventArgs(info, connected: connected, disconnectReason: reason));
        }

        public NetworkError Connect(ConnectRequest request)
        {
            return _lanDiscovery.Connect(request.NetworkInfo, request.UserConfig, request.LocalCommunicationVersion);
        }

        public NetworkError ConnectPrivate(ConnectPrivateRequest request)
        {
            // NOTE: As in ldn_mitm, private networks are not implemented.
            Logger.Stub?.PrintMsg(LogClass.ServiceLdn, "LanPlayLdnClient ConnectPrivate");

            return NetworkError.None;
        }

        public bool CreateNetwork(CreateAccessPointRequest request, byte[] advertiseData)
        {
            return _lanDiscovery.CreateNetwork(request.SecurityConfig, request.UserConfig, request.NetworkConfig);
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
            return _lanDiscovery.Scan(channel, scanFilter);
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
