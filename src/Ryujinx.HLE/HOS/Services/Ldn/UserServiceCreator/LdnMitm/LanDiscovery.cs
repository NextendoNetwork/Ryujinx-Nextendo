using Ryujinx.Common.Logging;
using Ryujinx.Common.Memory;
using Ryujinx.Common.Utilities;
using Ryujinx.HLE.HOS.Services.Ldn.Types;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm.Proxy;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm.Types;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm
{
    internal class LanDiscovery : IDisposable
    {
        private const int DefaultPort = 11452;
        private const ushort CommonChannel = 6;
        private const byte CommonLinkLevel = 3;
        private const byte CommonNetworkType = 2;

        private const int FailureTimeout = 4000;

        private readonly ILdnDiscoveryClient _parent;
        private readonly ILdnNetworkProvider _provider;
        private readonly LanProtocol _protocol;
        private bool _initialized;
        private readonly Ssid _fakeSsid;
        private ILdnTcpSocket _tcp;
        private IReadOnlyList<ILdnUdpSocket> _udpSockets = [];
        private ILdnUdpSocket _udp;
        private readonly List<ILdnTcpSession> _stations = [];
        private readonly Lock _lock = new();

        private readonly AutoResetEvent _apConnected = new(false);

        internal readonly IPAddress LocalAddr;
        internal readonly IPAddress LocalBroadcastAddr;
        internal NetworkInfo NetworkInfo;

        public bool IsHost => _tcp is { IsServer: true };

        private readonly Random _random = new();

        private static NetworkInfo GetEmptyNetworkInfo()
        {
            NetworkInfo networkInfo = new()
            {
                NetworkId = new NetworkId
                {
                    SessionId = new Array16<byte>(),
                },
                Common = new CommonNetworkInfo
                {
                    MacAddress = new Array6<byte>(),
                    Ssid = new Ssid
                    {
                        Name = new Array33<byte>(),
                    },
                },
                Ldn = new LdnNetworkInfo
                {
                    NodeCountMax = LdnConst.NodeCountMax,
                    SecurityParameter = new Array16<byte>(),
                    Nodes = new Array8<NodeInfo>(),
                    AdvertiseData = new Array384<byte>(),
                    Reserved4 = new Array140<byte>(),
                },
            };

            Span<NodeInfo> nodesSpan = networkInfo.Ldn.Nodes.AsSpan();

            for (int i = 0; i < LdnConst.NodeCountMax; i++)
            {
                nodesSpan[i] = new NodeInfo
                {
                    MacAddress = new Array6<byte>(),
                    UserName = new Array33<byte>(),
                    Reserved2 = new Array16<byte>(),
                };
            }

            return networkInfo;
        }

        public LanDiscovery(ILdnDiscoveryClient parent, ILdnNetworkProvider provider)
        {
            Logger.Info?.PrintMsg(LogClass.ServiceLdn, $"Initialize LanDiscovery using IP: {provider.LocalAddress}");

            _parent = parent;
            _provider = provider;
            LocalAddr = provider.LocalAddress;
            LocalBroadcastAddr = provider.BroadcastAddress;

            _fakeSsid = new Ssid
            {
                Length = LdnConst.SsidLengthMax,
            };
            _random.NextBytes(_fakeSsid.Name.AsSpan()[..32]);

            _protocol = new LanProtocol(this);
            _protocol.Accept += OnConnect;
            _protocol.SyncNetwork += OnSyncNetwork;
            _protocol.DisconnectStation += DisconnectStation;

            NetworkInfo = GetEmptyNetworkInfo();

            ResetStations();

            if (!InitUdp())
            {
                Logger.Error?.PrintMsg(LogClass.ServiceLdn, "LanDiscovery Initialize: InitUdp failed.");

                return;
            }

            _initialized = true;
        }

        protected void OnSyncNetwork(NetworkInfo info)
        {
            bool updated = false;

            lock (_lock)
            {
                if (!NetworkInfo.Equals(info))
                {
                    NetworkInfo = info;
                    updated = true;

                    Logger.Debug?.PrintMsg(LogClass.ServiceLdn, $"Host IP: {NetworkHelpers.ConvertUint(info.Ldn.Nodes[0].Ipv4Address)}");
                }
            }

            if (updated)
            {
                _parent.InvokeNetworkChange(info, true);
            }

            _apConnected.Set();
        }

        protected void OnConnect(ILdnTcpSession station)
        {
            lock (_lock)
            {
                station.NodeId = LocateEmptyNode();

                if (_stations.Count > LdnConst.StationCountMax || station.NodeId == -1)
                {
                    station.Disconnect();
                    station.Dispose();

                    return;
                }

                _stations.Add(station);

                UpdateNodes();
            }
        }

        public void DisconnectStation(ILdnTcpSession station)
        {
            if (!station.IsDisposed)
            {
                if (station.IsConnected)
                {
                    station.Disconnect();
                }

                station.Dispose();
            }

            lock (_lock)
            {
                if (_stations.Remove(station))
                {
                    NetworkInfo.Ldn.Nodes[station.NodeId] = new NodeInfo()
                    {
                        MacAddress = new Array6<byte>(),
                        UserName = new Array33<byte>(),
                        Reserved2 = new Array16<byte>(),
                    };

                    UpdateNodes();
                }
            }
        }

        public bool SetAdvertiseData(byte[] data)
        {
            if (data.Length > LdnConst.AdvertiseDataSizeMax)
            {
                Logger.Error?.PrintMsg(LogClass.ServiceLdn, "AdvertiseData exceeds size limit.");

                return false;
            }

            data.CopyTo(NetworkInfo.Ldn.AdvertiseData.AsSpan());
            NetworkInfo.Ldn.AdvertiseDataSize = (ushort)data.Length;

            // NOTE: Otherwise this results in SessionKeepFailed or MasterDisconnected
            lock (_lock)
            {
                if (NetworkInfo.Ldn.Nodes[0].IsConnected == 1)
                {
                    UpdateNodes(true);
                }
            }

            return true;
        }

        public void InitNetworkInfo()
        {
            lock (_lock)
            {
                NetworkInfo.Common.MacAddress = GetFakeMac();
                NetworkInfo.Common.Channel = CommonChannel;
                NetworkInfo.Common.LinkLevel = CommonLinkLevel;
                NetworkInfo.Common.NetworkType = CommonNetworkType;
                NetworkInfo.Common.Ssid = _fakeSsid;

                NetworkInfo.Ldn.Nodes = new Array8<NodeInfo>();
                
                Span<NodeInfo> nodesSpan = NetworkInfo.Ldn.Nodes.AsSpan();

                for (int i = 0; i < LdnConst.NodeCountMax; i++)
                {
                    nodesSpan[i].NodeId = (byte)i;
                    nodesSpan[i].IsConnected = 0;
                }
            }
        }

        protected Array6<byte> GetFakeMac(IPAddress address = null)
        {
            address ??= LocalAddr;

            byte[] ip = address.GetAddressBytes();

            Array6<byte> macAddress = new();
            new byte[] { 0x02, 0x00, ip[0], ip[1], ip[2], ip[3] }.CopyTo(macAddress.AsSpan());

            return macAddress;
        }

        public bool InitTcp(bool listening, IPAddress address = null, int port = DefaultPort)
        {
            Logger.Debug?.PrintMsg(LogClass.ServiceLdn, $"LanDiscovery InitTcp: IP: {address}, listening: {listening}");

            if (_tcp != null)
            {
                _tcp.DisconnectAndStop();
                _tcp.Dispose();
                _tcp = null;
            }

            ILdnTcpSocket tcpSocket;

            if (listening)
            {
                try
                {
                    address ??= LocalAddr;

                    tcpSocket = _provider.CreateTcpServer(_protocol, address, port);
                }
                catch (Exception ex)
                {
                    Logger.Error?.PrintMsg(LogClass.ServiceLdn, $"Failed to create the LDN TCP server: {ex}");

                    return false;
                }

                if (!tcpSocket.Start())
                {
                    return false;
                }
            }
            else
            {
                if (address == null)
                {
                    return false;
                }

                try
                {
                    tcpSocket = _provider.CreateTcpClient(_protocol, address, port);
                }
                catch (Exception ex)
                {
                    Logger.Error?.PrintMsg(LogClass.ServiceLdn, $"Failed to create the LDN TCP client: {ex}");

                    return false;
                }
            }

            _tcp = tcpSocket;

            return true;
        }

        public bool InitUdp()
        {
            foreach (ILdnUdpSocket socket in _udpSockets)
            {
                socket.Stop();
            }

            try
            {
                _udpSockets = _provider.CreateUdpSockets(_protocol, DefaultPort);
                _udp = _udpSockets.Count > 0 ? _udpSockets[0] : null;
            }
            catch (Exception ex)
            {
                Logger.Error?.PrintMsg(LogClass.ServiceLdn, $"Failed to create the LDN UDP server: {ex}");

                return false;
            }

            return _udp != null;
        }

        public NetworkInfo[] Scan(ushort channel, ScanFilter filter)
        {
            _udp.ClearScanResults();

            if (_protocol.SendBroadcast(_udp, LanPacketType.Scan, DefaultPort) < 0)
            {
                return [];
            }

            List<NetworkInfo> outNetworkInfo = [];

            foreach (KeyValuePair<ulong, NetworkInfo> item in _udp.GetScanResults())
            {
                bool copy = true;

                if (filter.Flag.HasFlag(ScanFilterFlag.LocalCommunicationId))
                {
                    copy &= filter.NetworkId.IntentId.LocalCommunicationId == item.Value.NetworkId.IntentId.LocalCommunicationId;
                }

                if (filter.Flag.HasFlag(ScanFilterFlag.SessionId))
                {
                    copy &= filter.NetworkId.SessionId.AsSpan().SequenceEqual(item.Value.NetworkId.SessionId.AsSpan());
                }

                if (filter.Flag.HasFlag(ScanFilterFlag.NetworkType))
                {
                    copy &= filter.NetworkType == (NetworkType)item.Value.Common.NetworkType;
                }

                if (filter.Flag.HasFlag(ScanFilterFlag.Ssid))
                {
                    Span<byte> gameSsid = item.Value.Common.Ssid.Name.AsSpan()[item.Value.Common.Ssid.Length..];
                    Span<byte> scanSsid = filter.Ssid.Name.AsSpan()[filter.Ssid.Length..];
                    copy &= gameSsid.SequenceEqual(scanSsid);
                }

                if (filter.Flag.HasFlag(ScanFilterFlag.SceneId))
                {
                    copy &= filter.NetworkId.IntentId.SceneId == item.Value.NetworkId.IntentId.SceneId;
                }

                if (copy)
                {
                    if (item.Value.Ldn.Nodes[0].UserName[0] != 0)
                    {
                        outNetworkInfo.Add(item.Value);
                    }
                    else
                    {
                        Logger.Warning?.PrintMsg(LogClass.ServiceLdn, "LanDiscovery Scan: Got empty Username. There might be a timing issue somewhere...");
                    }
                }
            }

            return outNetworkInfo.ToArray();
        }

        protected void ResetStations()
        {
            lock (_lock)
            {
                foreach (ILdnTcpSession station in _stations)
                {
                    station.Disconnect();
                    station.Dispose();
                }

                _stations.Clear();
            }
        }

        private int LocateEmptyNode()
        {
            Span<NodeInfo> nodesSpan = NetworkInfo.Ldn.Nodes.AsSpan();

            for (int i = 1; i < nodesSpan.Length; i++)
            {
                if (nodesSpan[i].IsConnected == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        protected void UpdateNodes(bool forceUpdate = false)
        {
            int countConnected = 1;

            foreach (ILdnTcpSession station in _stations.Where(station => station.IsConnected))
            {
                countConnected++;

                station.OverrideInfo();

                // NOTE: This is not part of the original implementation.
                NetworkInfo.Ldn.Nodes[station.NodeId] = station.NodeInfo;
            }

            byte nodeCount = (byte)countConnected;

            bool networkInfoChanged = forceUpdate || NetworkInfo.Ldn.NodeCount != nodeCount;

            NetworkInfo.Ldn.NodeCount = nodeCount;

            foreach (ILdnTcpSession station in _stations)
            {
                if (station.IsConnected)
                {
                    if (_protocol.SendPacket(station, LanPacketType.SyncNetwork, SpanHelpers.AsSpan<NetworkInfo, byte>(ref NetworkInfo).ToArray()) < 0)
                    {
                        Logger.Error?.PrintMsg(LogClass.ServiceLdn, $"Failed to send {LanPacketType.SyncNetwork} to station {station.NodeId}");
                    }
                }
            }

            if (networkInfoChanged)
            {
                _parent.InvokeNetworkChange(NetworkInfo, true);
            }
        }

        protected NodeInfo GetNodeInfo(NodeInfo node, UserConfig userConfig, ushort localCommunicationVersion)
        {
            uint ipAddress = NetworkHelpers.ConvertIpv4Address(LocalAddr);

            node.MacAddress = GetFakeMac();
            node.IsConnected = 1;
            node.UserName = userConfig.UserName;
            node.LocalCommunicationVersion = localCommunicationVersion;
            node.Ipv4Address = ipAddress;

            return node;
        }

        public bool CreateNetwork(SecurityConfig securityConfig, UserConfig userConfig, NetworkConfig networkConfig)
        {
            if (!InitTcp(true))
            {
                return false;
            }

            InitNetworkInfo();

            NetworkInfo.Ldn.NodeCountMax = networkConfig.NodeCountMax;
            NetworkInfo.Ldn.SecurityMode = (ushort)securityConfig.SecurityMode;

            NetworkInfo.Common.Channel = networkConfig.Channel == 0 ? (ushort)6 : networkConfig.Channel;

            NetworkInfo.NetworkId.SessionId = new Array16<byte>();
            _random.NextBytes(NetworkInfo.NetworkId.SessionId.AsSpan());
            NetworkInfo.NetworkId.IntentId = networkConfig.IntentId;

            NetworkInfo.Ldn.Nodes[0] = GetNodeInfo(NetworkInfo.Ldn.Nodes[0], userConfig, networkConfig.LocalCommunicationVersion);
            NetworkInfo.Ldn.Nodes[0].IsConnected = 1;
            NetworkInfo.Ldn.NodeCount++;

            _parent.InvokeNetworkChange(NetworkInfo, true);

            return true;
        }

        public void DestroyNetwork()
        {
            if (_tcp != null)
            {
                try
                {
                    _tcp.DisconnectAndStop();
                }
                finally
                {
                    _tcp.Dispose();
                    _tcp = null;
                }
            }

            ResetStations();
        }

        public NetworkError Connect(NetworkInfo networkInfo, UserConfig userConfig, uint localCommunicationVersion)
        {
            _apConnected.Reset();

            if (networkInfo.Ldn.NodeCount == 0)
            {
                return NetworkError.Unknown;
            }

            IPAddress address = NetworkHelpers.ConvertUint(networkInfo.Ldn.Nodes[0].Ipv4Address);

            Logger.Info?.PrintMsg(LogClass.ServiceLdn, $"Connecting to host: {address}");

            if (!InitTcp(false, address))
            {
                Logger.Error?.PrintMsg(LogClass.ServiceLdn, "Could not initialize TCPClient");

                return NetworkError.ConnectNotFound;
            }

            if (!_tcp.Connect())
            {
                Logger.Error?.PrintMsg(LogClass.ServiceLdn, "Failed to connect.");

                return NetworkError.ConnectFailure;
            }

            NodeInfo myNode = GetNodeInfo(new NodeInfo(), userConfig, (ushort)localCommunicationVersion);
            if (_protocol.SendPacket(_tcp, LanPacketType.Connect, SpanHelpers.AsSpan<NodeInfo, byte>(ref myNode).ToArray()) < 0)
            {
                return NetworkError.Unknown;
            }

            return _apConnected.WaitOne(FailureTimeout) ? NetworkError.None : NetworkError.ConnectTimeout;
        }

        public void Dispose()
        {
            if (_initialized)
            {
                DisconnectAndStop();
                ResetStations();
                _initialized = false;
            }

            _protocol.Accept -= OnConnect;
            _protocol.SyncNetwork -= OnSyncNetwork;
            _protocol.DisconnectStation -= DisconnectStation;

            _provider.Dispose();
        }

        public void DisconnectAndStop()
        {
            foreach (ILdnUdpSocket socket in _udpSockets)
            {
                try
                {
                    socket.Stop();
                }
                finally
                {
                    socket.Dispose();
                }
            }

            _udpSockets = [];
            _udp = null;

            if (_tcp != null)
            {
                try
                {
                    _tcp.DisconnectAndStop();
                }
                finally
                {
                    _tcp.Dispose();
                    _tcp = null;
                }
            }
        }
    }
}
