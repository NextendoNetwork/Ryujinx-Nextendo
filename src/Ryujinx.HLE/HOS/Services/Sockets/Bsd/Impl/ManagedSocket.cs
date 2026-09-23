using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd.Proxy;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Ryujinx.HLE.HOS.Services.Sockets.Bsd.Impl
{
    class ManagedSocket : ISocket
    {
        public int Refcount { get; set; }

        public AddressFamily AddressFamily => Socket.AddressFamily;

        public SocketType SocketType => Socket.SocketType;

        public ProtocolType ProtocolType => Socket.ProtocolType;

        public bool Blocking { get => Socket.Blocking; set => Socket.Blocking = value; }

        public nint Handle => nint.Zero;

        public IPEndPoint RemoteEndPoint => Socket.RemoteEndPoint as IPEndPoint;

        public IPEndPoint LocalEndPoint => Socket.LocalEndPoint as IPEndPoint;

        public ISocketImpl Socket { get; private set; }

        // [Nextendo] Le raccrochage du pair a-t-il deja ete signale a l'invite par un POLLHUP synthetise ?
        // Voir ManagedSocketPollManager.Poll : la notification est volontairement A FRONT et non a niveau.
        // POSIX rapporte POLLHUP a chaque appel, mais l'invite peut garder longtemps un descripteur mort dans
        // son ensemble de sondage sans interet en lecture (mesure S3 : 39 s avec req=0) ; un POLLHUP a niveau
        // ferait alors rendre chaque poll instantanement, donc une boucle folle. On previent une fois, ce qui
        // suffit a rompre la cecite, sans risquer la boucle si l'invite ignore l'avertissement.
        public bool HangupReported { get; set; }

        // [Nextendo] getsockopt must return what setsockopt accepted. When SetSocketOption feigns success on
        // an option this emulation can't map (see the tolerant paths below), we remember the value here so
        // GetSocketOption returns it instead of EOPNOTSUPP. grpc-core sets TCP_NODELAY / SO_REUSEADDR / etc.
        // and reads them straight back; a set-ok / get-EOPNOTSUPP mismatch made it close before connect
        // (the socket died before the NPLN handshake even started).
        private readonly Dictionary<(BsdSocketOption Option, SocketOptionLevel Level), byte[]> _feignedSockOpts = new();

        public ManagedSocket(AddressFamily addressFamily, SocketType socketType, ProtocolType protocolType, string lanInterfaceId)
        {
            Socket = SocketHelpers.CreateSocket(addressFamily, socketType, protocolType, lanInterfaceId);
            Refcount = 1;
        }

        private ManagedSocket(ISocketImpl socket)
        {
            Socket = socket;
            Refcount = 1;
        }

        private static SocketFlags ConvertBsdSocketFlags(BsdSocketFlags bsdSocketFlags)
        {
            SocketFlags socketFlags = SocketFlags.None;

            if (bsdSocketFlags.HasFlag(BsdSocketFlags.Oob))
            {
                socketFlags |= SocketFlags.OutOfBand;
            }

            if (bsdSocketFlags.HasFlag(BsdSocketFlags.Peek))
            {
                socketFlags |= SocketFlags.Peek;
            }

            if (bsdSocketFlags.HasFlag(BsdSocketFlags.DontRoute))
            {
                socketFlags |= SocketFlags.DontRoute;
            }

            if (bsdSocketFlags.HasFlag(BsdSocketFlags.Trunc))
            {
                socketFlags |= SocketFlags.Truncated;
            }

            if (bsdSocketFlags.HasFlag(BsdSocketFlags.CTrunc))
            {
                socketFlags |= SocketFlags.ControlDataTruncated;
            }

            bsdSocketFlags &= ~(BsdSocketFlags.Oob |
                BsdSocketFlags.Peek |
                BsdSocketFlags.DontRoute |
                BsdSocketFlags.DontWait |
                BsdSocketFlags.Trunc |
                BsdSocketFlags.CTrunc);

            if (bsdSocketFlags != BsdSocketFlags.None)
            {
                Logger.Warning?.Print(LogClass.ServiceBsd, $"Unsupported socket flags: {bsdSocketFlags}");
            }

            return socketFlags;
        }

        public LinuxError Accept(out ISocket newSocket)
        {
            try
            {
                newSocket = new ManagedSocket(Socket.Accept());

                IPEndPoint remoteEndPoint = newSocket.RemoteEndPoint;
                bool isPrivateIp = remoteEndPoint.Address.ToString().StartsWith("192.168.");
                Logger.Info?.PrintMsg(LogClass.ServiceBsd,
                    isPrivateIp
                        ? $"Accepted connection from {ProtocolType}/{remoteEndPoint.Address}:{remoteEndPoint.Port}"
                        : $"Accepted connection from {ProtocolType}/***:{remoteEndPoint.Port}");

                return LinuxError.SUCCESS;
            }
            catch (SocketException exception)
            {
                newSocket = null;

                if (exception.SocketErrorCode != SocketError.WouldBlock)
                {
                    Logger.Warning?.Print(LogClass.ServiceBsd, $"Socket Exception: {exception}");
                }

                return WinSockHelper.ConvertError((WsaError)exception.ErrorCode);
            }
        }

        public LinuxError Bind(IPEndPoint localEndPoint)
        {
            Logger.Info?.PrintMsg(LogClass.ServiceBsd, $"Socket binding to: {ProtocolType}/{localEndPoint.Port}");

            // [Nextendo] If the guest just closed a udp socket on this port, take that one back
            // instead of binding a new one: rebinding the same port gets a NEW NAT mapping, and
            // the peer is about to probe the OLD one. See NextendoUdpPortKeeper.
            if (SocketType == SocketType.Dgram
                && NextendoUdpPortKeeper.TryAdopt(localEndPoint, out ISocketImpl parked))
            {
                Socket.Dispose();
                Socket = parked;

                return LinuxError.SUCCESS;
            }

            try
            {
                Socket.Bind(localEndPoint);

                return LinuxError.SUCCESS;
            }
            catch (SocketException exception)
            {
                if (exception.SocketErrorCode != SocketError.WouldBlock)
                {
                    Logger.Warning?.Print(LogClass.ServiceBsd, $"Socket Exception: {exception}");
                }

                return WinSockHelper.ConvertError((WsaError)exception.ErrorCode);
            }
        }

        public void Close()
        {
            Socket.Close();
        }

        public LinuxError Connect(IPEndPoint remoteEndPoint)
        {
            // [Nextendo] The NPLN gRPC resolver mis-deserializes our packed addrinfo and hands the socket
            // 0.0.0.0 / :: (address lost, port kept) -> the connect fails, no ClientHello, NPLN 2321-4992.
            // Substitute the last DNS-MITM redirect (our server IP) so the gRPC reaches our server. HTTP
            // uses GetHostByName (a different path) and is never 0.0.0.0, so this only rescues the broken
            // gRPC connects. dualMode IPv6 socket -> map the IPv4 server IP to ::ffff:.
            bool grpcConnect = false;
            {
                IPAddress a = remoteEndPoint.Address;
                bool isAny = a.Equals(IPAddress.Any) || a.Equals(IPAddress.IPv6Any)
                             || (a.IsIPv4MappedToIPv6 && a.MapToIPv4().Equals(IPAddress.Any));
                // Repli choisi PAR PORT : le port designe le service vise sans ambiguite. En prenant
                // « la derniere resolution », toutes destinations confondues, une connexion pouvait
                // partir vers le serveur d'un autre jeu — mesure du 2026-08-15, douze fois sur
                // vingt-deux chez un testeur, ce qui rendait la jonction en partie privee aleatoire.
                IPAddress sub = Ryujinx.HLE.HOS.Services.Sockets.Sfdnsres.Proxy.DnsMitmResolver.RedirectionPour(remoteEndPoint.Port);
                if (isAny && sub != null)
                {
                    if (Socket.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                        && sub.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        sub = sub.MapToIPv6();
                    }
                    Logger.Info?.PrintMsg(LogClass.ServiceBsd, $"[Nextendo] Connect target was {a}:{remoteEndPoint.Port} (lost address) -> substituting {sub}");
                    remoteEndPoint = new IPEndPoint(sub, remoteEndPoint.Port);
                    grpcConnect = true;
                }
                else if (isAny)
                {
                    // Adresse perdue sur un port SANS redirection connue : typiquement un pair du
                    // jeu en ligne. On ne substitue rien — envoyer ce trafic vers nos serveurs
                    // casserait le pair-a-pair — mais on le TRACE, parce que c'est exactement la
                    // mesure qui manquera pour comprendre un echec d'etablissement P2P.
                    Logger.Warning?.Print(LogClass.ServiceBsd,
                        $"[Nextendo] Connect target was {a}:{remoteEndPoint.Port} (lost address) — AUCUNE redirection pour ce port, on ne substitue pas (pair ?)");
                }
            }

            // [Nextendo] La boucle locale est traitee comme le reseau local : une redirection
            // qui atterrit sur 127.0.0.1 signifie que l adresse du serveur n a jamais ete
            // configuree, et la masquer transformait ca en panne inexplicable.
            bool isLDNPrivateIP = remoteEndPoint.Address.ToString().StartsWith("192.168.")
                                  || IPAddress.IsLoopback(remoteEndPoint.Address)
                                  || (remoteEndPoint.Address.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(remoteEndPoint.Address.MapToIPv4()));
            if (isLDNPrivateIP)
            {
                Logger.Info?.PrintMsg(LogClass.ServiceBsd, $"Connecting to: {ProtocolType}/{remoteEndPoint.Address}:{remoteEndPoint.Port}");
            }
            else
            {
                Logger.Info?.PrintMsg(LogClass.ServiceBsd, $"Connecting to: {ProtocolType}/***:{remoteEndPoint.Port}");
            }

            try
            {
                Socket.Connect(remoteEndPoint);

                return LinuxError.SUCCESS;
            }
            catch (SocketException exception)
            {
                // [Nextendo] SocketErrorCode et non ErrorCode : sous Unix, le second est
                // l errno natif (EAGAIN vaut 11) et non la valeur Winsock (10035). Ce test
                // n etait donc vrai que sous Windows. Sur Linux et macOS, une connexion non
                // bloquante sautait toute la branche ci-dessous et rendait EAGAIN au jeu au
                // lieu d EINPROGRESS — et le contournement de completion synchrone du service
                // en ligne ne s executait jamais non plus.
                if (!Blocking && exception.SocketErrorCode == SocketError.WouldBlock)
                {
                    // [Nextendo] For the grpc/NPLN connect, do NOT return EINPROGRESS and rely on grpc to poll
                    // the connecting socket for POLLOUT — that is RACY: grpc adds the socket to its pollset
                    // late, so completion was detected up to ~74s later and the whole online flow stalled on
                    // some runs. The host connect to our VPS completes in ~10ms, so block briefly here and
                    // return SUCCESS as soon as it is writable (connected). grpc then has a connected socket
                    // immediately and proceeds straight to TLS/gRPC — deterministic. Scoped to grpc connects
                    // (the substituted ones); NEX/other sockets keep the original non-blocking EINPROGRESS.
                    // [Nextendo] Kept ON by default on measured evidence. Both branches were compared with the
                    // deferred-poll revents fix in place:
                    //   SUCCESS here  -> grpc takes tcp_client's "connected immediately" branch. The socket is
                    //                    often absent from the pollset (polls carry 1 fd, the wakeup eventfd),
                    //                    but when it is present the flow reaches ClientHello -> ServerHello.
                    //   EINPROGRESS   -> grpc's async path DOES register the socket (94728 polls with 2 fds,
                    //                    rev=Output wr=True conn=True) yet never runs on_writable, so it never
                    //                    sends anything at all (SendMMsg=0). Strictly worse.
                    // Set NEXTENDO_GRPC_CONNECT_SYNC=0 to try the async path again once on_writable dispatches.
                    if (grpcConnect && Environment.GetEnvironmentVariable("NEXTENDO_GRPC_CONNECT_SYNC") != "0")
                    {
                        try
                        {
                            if (Socket.Poll(2_000_000, System.Net.Sockets.SelectMode.SelectWrite)
                                && !Socket.Poll(0, System.Net.Sockets.SelectMode.SelectError))
                            {
                                Logger.Info?.PrintMsg(LogClass.ServiceBsd, "[Nextendo] grpc connect completed synchronously (blocked for host completion) -> SUCCESS");

                                return LinuxError.SUCCESS;
                            }
                        }
                        catch { /* fall through to EINPROGRESS */ }
                    }

                    return LinuxError.EINPROGRESS;
                }
                else
                {
                    if (exception.SocketErrorCode != SocketError.WouldBlock)
                    {
                        Logger.Warning?.Print(LogClass.ServiceBsd, $"Socket Exception: {exception}");
                    }

                    return WinSockHelper.ConvertError((WsaError)exception.ErrorCode);
                }
            }
        }

        public void Disconnect()
        {
            Logger.Info?.Print(LogClass.ServiceBsd, "Socket disconnecting");
            Socket.Disconnect(true);
        }

        public void Dispose()
        {
            Logger.Info?.Print(LogClass.ServiceBsd, "Socket closed");

            // [Nextendo] Hand a udp socket to the keeper instead of closing it: Pia reopens the
            // same local port moments later for its peer-to-peer traffic, and a fresh socket
            // would get a fresh NAT mapping — leaving the peer probing an endpoint that no
            // longer exists. The keeper owns it from here (and closes it if nobody reclaims it).
            if (NextendoUdpPortKeeper.TryPark(Socket))
            {
                // Drop our reference: the keeper owns it now, and may well hand it to another
                // ManagedSocket. Two live objects sharing one socket is exactly the kind of bug
                // that would be blamed on the fix rather than on the aliasing.
                Socket = null;

                return;
            }

            Socket.Close();
            Socket.Dispose();
        }

        public LinuxError Listen(int backlog)
        {
            try
            {
                Socket.Listen(backlog);

                Logger.Info?.PrintMsg(LogClass.ServiceBsd, $"Socket listening: {ProtocolType}/{(Socket.LocalEndPoint as IPEndPoint).Port}");

                return LinuxError.SUCCESS;
            }
            catch (SocketException exception)
            {
                if (exception.SocketErrorCode != SocketError.WouldBlock)
                {
                    Logger.Warning?.Print(LogClass.ServiceBsd, $"Socket Exception: {exception}");
                }

                return WinSockHelper.ConvertError((WsaError)exception.ErrorCode);
            }
        }

        public bool Poll(int microSeconds, SelectMode mode)
        {
            return Socket.Poll(microSeconds, mode);
        }

        public LinuxError Shutdown(BsdSocketShutdownFlags how)
        {
            try
            {
                Socket.Shutdown((SocketShutdown)how);

                return LinuxError.SUCCESS;
            }
            catch (SocketException exception)
            {
                if (exception.SocketErrorCode != SocketError.WouldBlock)
                {
                    Logger.Warning?.Print(LogClass.ServiceBsd, $"Socket Exception: {exception}");
                }

                return WinSockHelper.ConvertError((WsaError)exception.ErrorCode);
            }
        }

        private bool _hasEmittedBlockingWarning;

        public LinuxError Receive(out int receiveSize, Span<byte> buffer, BsdSocketFlags flags)
        {
            LinuxError result;

            bool shouldBlockAfterOperation = false;

            try
            {
                if (Blocking && flags.HasFlag(BsdSocketFlags.DontWait))
                {
                    Blocking = false;
                    shouldBlockAfterOperation = true;
                }

                if (Blocking && !_hasEmittedBlockingWarning)
                {
                    Logger.Warning?.PrintMsg(LogClass.ServiceBsd, "Blocking socket operations are not yet working properly. Expect network errors.");
                    _hasEmittedBlockingWarning = true;
                }

                receiveSize = Socket.Receive(buffer, ConvertBsdSocketFlags(flags));

                result = LinuxError.SUCCESS;
            }
            catch (SocketException exception)
            {
                if (exception.SocketErrorCode != SocketError.WouldBlock)
                {
                    Logger.Warning?.Print(LogClass.ServiceBsd, $"Socket Exception: {exception}");
                }

                receiveSize = -1;

                result = WinSockHelper.ConvertError((WsaError)exception.ErrorCode);
            }

            if (shouldBlockAfterOperation)
            {
                Blocking = true;
            }

            return result;
        }

        public LinuxError ReceiveFrom(out int receiveSize, Span<byte> buffer, int size, BsdSocketFlags flags, out IPEndPoint remoteEndPoint)
        {
            remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

            LinuxError result;

            bool shouldBlockAfterOperation = false;

            try
            {
                EndPoint temp = new IPEndPoint(IPAddress.Any, 0);

                if (Blocking && flags.HasFlag(BsdSocketFlags.DontWait))
                {
                    Blocking = false;
                    shouldBlockAfterOperation = true;
                }

                if (Blocking && !_hasEmittedBlockingWarning)
                {
                    Logger.Warning?.PrintMsg(LogClass.ServiceBsd, "Blocking socket operations are not yet working properly. Expect network errors.");
                    _hasEmittedBlockingWarning = true;
                }

                if (!Socket.IsBound)
                {
                    receiveSize = -1;

                    Logger.Debug?.Print(LogClass.ServiceBsd,
                        $"[Nextendo] UDP ReceiveFrom remote={remoteEndPoint} size={size} result={LinuxError.EOPNOTSUPP} error=NotBound");

                    return LinuxError.EOPNOTSUPP;
                }

                receiveSize = Socket.ReceiveFrom(buffer[..size], ConvertBsdSocketFlags(flags), ref temp);

                remoteEndPoint = (IPEndPoint)temp;
                result = LinuxError.SUCCESS;

                Logger.Debug?.Print(LogClass.ServiceBsd,
                    $"[Nextendo] UDP ReceiveFrom remote={remoteEndPoint} size={size} received={receiveSize} result={result}");
            }
            catch (SocketException exception)
            {
                if (exception.SocketErrorCode != SocketError.WouldBlock)
                {
                    Logger.Warning?.Print(LogClass.ServiceBsd, $"Socket Exception: {exception}");
                }

                receiveSize = -1;

                result = WinSockHelper.ConvertError((WsaError)exception.ErrorCode);

                Logger.Debug?.Print(LogClass.ServiceBsd,
                    $"[Nextendo] UDP ReceiveFrom remote={remoteEndPoint} size={size} received=-1 result={result} socketError={exception.SocketErrorCode} errorCode={exception.ErrorCode}");
            }

            if (shouldBlockAfterOperation)
            {
                Blocking = true;
            }

            return result;
        }

        // [Nextendo] One-shot SNI injection for the first ClientHello on this socket.
        private bool _sniDone;

        public LinuxError Send(out int sendSize, ReadOnlySpan<byte> buffer, BsdSocketFlags flags)
        {
            // [DIAG] Log any TLS handshake record we're asked to send, so we can see whether the
            // game's ClientHello actually flows through this Bsd send path (vs nn::ssl / another method).
            if (buffer.Length > 5 && buffer[0] == 0x16)
            {
                IPEndPoint dr = RemoteEndPoint;
                Ryujinx.HLE.HOS.Services.Sockets.Sfdnsres.Proxy.DnsMitmResolver.LastHostForIp.TryGetValue(dr?.Address?.ToString() ?? "", out string dh);
                Logger.Info?.Print(LogClass.ServiceBsd, $"[DIAG] Bsd.Send TLS hs=0x{buffer[5]:x2} len={buffer.Length} remote={dr} dnsHost={dh}");
            }

            // If this is a TLS ClientHello with no SNI heading to a host we DNS-redirected, splice
            // the original hostname in as the SNI so our SNI-routing reverse-proxy can reach the
            // right backend (some games' bundled TLS client sends no SNI under emulation).
            if (!_sniDone && buffer.Length > 5 && buffer[0] == 0x16 && buffer[5] == 0x01)
            {
                _sniDone = true;
                try
                {
                    IPEndPoint rep = RemoteEndPoint;
                    if (rep != null
                        && Ryujinx.HLE.HOS.Services.Sockets.Sfdnsres.Proxy.DnsMitmResolver.LastHostForIp.TryGetValue(rep.Address.ToString(), out string host)
                        && !string.IsNullOrEmpty(host)
                        && TlsSniInjector.TryInject(buffer, host, out byte[] modified))
                    {
                        bool wasBlocking = Socket.Blocking;
                        Socket.Blocking = true;
                        int total = 0;
                        while (total < modified.Length)
                        {
                            total += Socket.Send(modified.AsSpan(total), ConvertBsdSocketFlags(flags));
                        }
                        Socket.Blocking = wasBlocking;
                        Logger.Info?.Print(LogClass.ServiceBsd, $"Injected SNI '{host}' into ClientHello ({buffer.Length}->{modified.Length}B)");
                        sendSize = buffer.Length;
                        return LinuxError.SUCCESS;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning?.Print(LogClass.ServiceBsd, $"SNI injection failed: {ex.Message}");
                }
            }

            try
            {
                sendSize = Socket.Send(buffer, ConvertBsdSocketFlags(flags));

                return LinuxError.SUCCESS;
            }
            catch (SocketException exception)
            {
                if (exception.SocketErrorCode != SocketError.WouldBlock)
                {
                    Logger.Warning?.Print(LogClass.ServiceBsd, $"Socket Exception: {exception}");
                }

                sendSize = -1;

                return WinSockHelper.ConvertError((WsaError)exception.ErrorCode);
            }
        }

        public LinuxError SendTo(out int sendSize, ReadOnlySpan<byte> buffer, int size, BsdSocketFlags flags, IPEndPoint remoteEndPoint)
        {
            try
            {
                sendSize = Socket.SendTo(buffer[..size], ConvertBsdSocketFlags(flags), remoteEndPoint);

                Logger.Debug?.Print(LogClass.ServiceBsd,
                    $"[Nextendo] UDP SendTo remote={remoteEndPoint} size={size} sent={sendSize} result={LinuxError.SUCCESS}");

                return LinuxError.SUCCESS;
            }
            catch (SocketException exception)
            {
                if (exception.SocketErrorCode != SocketError.WouldBlock)
                {
                    Logger.Warning?.Print(LogClass.ServiceBsd, $"Socket Exception: {exception}");
                }

                sendSize = -1;

                LinuxError result = WinSockHelper.ConvertError((WsaError)exception.ErrorCode);

                Logger.Debug?.Print(LogClass.ServiceBsd,
                    $"[Nextendo] UDP SendTo remote={remoteEndPoint} size={size} sent=-1 result={result} socketError={exception.SocketErrorCode} errorCode={exception.ErrorCode}");

                return result;
            }
        }

        // [Nextendo] Fill optionValue with the value a prior setsockopt feigned success on (or zeros), so
        // getsockopt round-trips what the client set instead of failing.
        private void ReturnFeignedSockOpt(BsdSocketOption option, SocketOptionLevel level, Span<byte> optionValue)
        {
            optionValue.Clear();

            if (_feignedSockOpts.TryGetValue((option, level), out byte[] stored))
            {
                stored.AsSpan(0, Math.Min(stored.Length, optionValue.Length)).CopyTo(optionValue);
            }
        }

        public LinuxError GetSocketOption(BsdSocketOption option, SocketOptionLevel level, Span<byte> optionValue)
        {
            try
            {
                LinuxError result = WinSockHelper.ValidateSocketOption(option, level, write: false);

                if (result != LinuxError.SUCCESS)
                {
                    // [Nextendo] Tolerant, mirroring SetSocketOption: return the value the client set (or zero)
                    // with SUCCESS. A set-ok / get-error mismatch made grpc close before connect.
                    Logger.Warning?.Print(LogClass.ServiceBsd, $"GetSockOpt Option toléré (option non validée): {option} Level: {level}");
                    ReturnFeignedSockOpt(option, level, optionValue);

                    return LinuxError.SUCCESS;
                }

                if (!WinSockHelper.TryConvertSocketOption(option, level, out SocketOptionName optionName))
                {
                    Logger.Warning?.Print(LogClass.ServiceBsd, $"GetSockOpt Option toléré (non convertible): {option} Level: {level}");
                    ReturnFeignedSockOpt(option, level, optionValue);

                    return LinuxError.SUCCESS;
                }

                byte[] tempOptionValue = new byte[optionValue.Length];

                Socket.GetSocketOption(level, optionName, tempOptionValue);

                tempOptionValue.AsSpan().CopyTo(optionValue);

                return LinuxError.SUCCESS;
            }
            catch (SocketException exception)
            {
                if (exception.SocketErrorCode != SocketError.WouldBlock)
                {
                    Logger.Warning?.Print(LogClass.ServiceBsd, $"Socket Exception: {exception}");
                }

                return WinSockHelper.ConvertError((WsaError)exception.ErrorCode);
            }
        }

        public LinuxError SetSocketOption(BsdSocketOption option, SocketOptionLevel level, ReadOnlySpan<byte> optionValue)
        {
            try
            {
                LinuxError result = WinSockHelper.ValidateSocketOption(option, level, write: true);

                if (result != LinuxError.SUCCESS)
                {
                    // [Nextendo] Tolérant : certains clients online posent des options socket IPv6 non
                    // gérées par cette émulation (niveau IPv6 absent de la table) → renvoyer une erreur
                    // faisait échouer l'ouverture de la socket (ENOPROTOOPT) → chargement infini.
                    // On feint le succès : l'option n'est pas critique, le client poursuit et connecte.
                    Logger.Warning?.Print(LogClass.ServiceBsd, $"SetSockOpt Option toléré (option non validée): {option} Level: {level}");
                    _feignedSockOpts[(option, level)] = optionValue.ToArray();

                    return LinuxError.SUCCESS;
                }

                if (!WinSockHelper.TryConvertSocketOption(option, level, out SocketOptionName optionName))
                {
                    // [Nextendo] idem : option non convertible → succès feint plutôt qu'échec.
                    Logger.Warning?.Print(LogClass.ServiceBsd, $"SetSockOpt Option toléré (non convertible): {option} Level: {level}");
                    _feignedSockOpts[(option, level)] = optionValue.ToArray();

                    return LinuxError.SUCCESS;
                }

                int value = optionValue.Length >= 4 ? MemoryMarshal.Read<int>(optionValue) : MemoryMarshal.Read<byte>(optionValue);

                if (level == SocketOptionLevel.Socket && option == BsdSocketOption.SoLinger)
                {
                    int value2 = 0;

                    if (optionValue.Length >= 8)
                    {
                        value2 = MemoryMarshal.Read<int>(optionValue[4..]);
                    }

                    Socket.SetSocketOption(level, SocketOptionName.Linger, new LingerOption(value != 0, value2));
                }
                else
                {
                    Socket.SetSocketOption(level, optionName, value);
                }

                return LinuxError.SUCCESS;
            }
            catch (SocketException exception)
            {
                if (exception.SocketErrorCode != SocketError.WouldBlock)
                {
                    Logger.Warning?.Print(LogClass.ServiceBsd, $"Socket Exception: {exception}");
                }

                return WinSockHelper.ConvertError((WsaError)exception.ErrorCode);
            }
        }

        public LinuxError Read(out int readSize, Span<byte> buffer)
        {
            return Receive(out readSize, buffer, BsdSocketFlags.None);
        }

        public LinuxError Write(out int writeSize, ReadOnlySpan<byte> buffer)
        {
            return Send(out writeSize, buffer, BsdSocketFlags.None);
        }

        private bool CanSupportMMsgHdr(BsdMMsgHdr message)
        {
            for (int i = 0; i < message.Messages.Length; i++)
            {
                if (message.Messages[i].Name != null ||
                    message.Messages[i].Control != null)
                {
                    return false;
                }
            }

            return true;
        }

        private static ArraySegment<byte>[] ConvertMessagesToBuffer(BsdMMsgHdr message)
        {
            int segmentCount = 0;
            int index = 0;

            foreach (BsdMsgHdr msgHeader in message.Messages)
            {
                segmentCount += msgHeader.Iov.Length;
            }

            ArraySegment<byte>[] buffers = new ArraySegment<byte>[segmentCount];

            foreach (BsdMsgHdr msgHeader in message.Messages)
            {
                foreach (byte[] iov in msgHeader.Iov)
                {
                    buffers[index++] = new ArraySegment<byte>(iov);
                }

                // Clear the length
                msgHeader.Length = 0;
            }

            return buffers;
        }

        private static void UpdateMessages(out int vlen, BsdMMsgHdr message, int transferedSize)
        {
            int bytesLeft = transferedSize;
            int index = 0;

            while (bytesLeft > 0)
            {
                // First ensure we haven't finished all buffers
                if (index >= message.Messages.Length)
                {
                    break;
                }

                BsdMsgHdr msgHeader = message.Messages[index];

                int possiblyTransferedBytes = 0;

                foreach (byte[] iov in msgHeader.Iov)
                {
                    possiblyTransferedBytes += iov.Length;
                }

                int storedBytes;

                if (bytesLeft > possiblyTransferedBytes)
                {
                    storedBytes = possiblyTransferedBytes;
                    index++;
                }
                else
                {
                    storedBytes = bytesLeft;
                }

                msgHeader.Length = (uint)storedBytes;
                bytesLeft -= storedBytes;
            }

            Debug.Assert(bytesLeft == 0);

            vlen = index + 1;
        }

        // TODO: Find a way to support passing the timeout somehow without changing the socket ReceiveTimeout.
        /// <summary>
        /// Envoie les segments d un message disperse un par un, pour les implementations de
        /// socket qui n ont pas d envoi vectorise. S arrete au premier segment non entierement
        /// accepte, comme le ferait un socket hote dont le tampon d envoi se remplit.
        /// </summary>
        private int SendSegments(ArraySegment<byte>[] buffers, SocketFlags flags, out SocketError socketError)
        {
            socketError = SocketError.Success;

            int total = 0;

            foreach (ArraySegment<byte> buffer in buffers)
            {
                if (buffer.Count == 0)
                {
                    continue;
                }

                int sent = Socket.Send(buffer.AsSpan(), flags, out socketError);

                if (socketError != SocketError.Success)
                {
                    return total;
                }

                total += sent;

                if (sent < buffer.Count)
                {
                    break;
                }
            }

            return total;
        }

        /// <summary>
        /// Remplit les segments d un message disperse un par un, pour les implementations de
        /// socket qui n ont pas de reception vectorisee. S arrete des qu un segment n est pas
        /// rempli en entier, pour qu un datagramme plus court ne bloque pas en attendant la suite.
        /// </summary>
        private int ReceiveSegments(ArraySegment<byte>[] buffers, SocketFlags flags, out SocketError socketError)
        {
            socketError = SocketError.Success;

            int total = 0;

            foreach (ArraySegment<byte> buffer in buffers)
            {
                if (buffer.Count == 0)
                {
                    continue;
                }

                int read = Socket.Receive(buffer.AsSpan(), flags, out socketError);

                if (socketError != SocketError.Success)
                {
                    return total;
                }

                total += read;

                if (read < buffer.Count)
                {
                    break;
                }
            }

            return total;
        }

        public LinuxError RecvMMsg(out int vlen, BsdMMsgHdr message, BsdSocketFlags flags, TimeVal timeout)
        {
            vlen = 0;

            if (message.Messages.Length == 0)
            {
                return LinuxError.SUCCESS;
            }

            if (!CanSupportMMsgHdr(message))
            {
                Logger.Warning?.Print(LogClass.ServiceBsd, "Unsupported BsdMMsgHdr");

                return LinuxError.EOPNOTSUPP;
            }

            if (message.Messages.Length == 0)
            {
                return LinuxError.SUCCESS;
            }

            try
            {
                SocketError socketError;
                int receiveSize;

                if (Socket is DefaultSocket hostSocket)
                {
                    receiveSize = hostSocket.BaseSocket.Receive(ConvertMessagesToBuffer(message), ConvertBsdSocketFlags(flags), out socketError);
                }
                else
                {
                    // [Nextendo] Le socket n est pas toujours adosse a un socket hote : avec
                    // LAN Play ou RyuLDN c est un socket virtuel, qui n a pas de reception
                    // vectorisee — d ou le remplissage segment par segment au lieu du
                    // transtypage, qui levait ici.
                    receiveSize = ReceiveSegments(ConvertMessagesToBuffer(message), ConvertBsdSocketFlags(flags), out socketError);
                }

                if (receiveSize > 0)
                {
                    UpdateMessages(out vlen, message, receiveSize);
                }

                return WinSockHelper.ConvertError((WsaError)socketError);
            }
            catch (SocketException exception)
            {
                if (exception.SocketErrorCode != SocketError.WouldBlock)
                {
                    Logger.Warning?.Print(LogClass.ServiceBsd, $"Socket Exception: {exception}");
                }

                return WinSockHelper.ConvertError((WsaError)exception.ErrorCode);
            }
        }

        public LinuxError SendMMsg(out int vlen, BsdMMsgHdr message, BsdSocketFlags flags)
        {
            vlen = 0;

            if (message.Messages.Length == 0)
            {
                return LinuxError.SUCCESS;
            }

            if (!CanSupportMMsgHdr(message))
            {
                Logger.Warning?.Print(LogClass.ServiceBsd, "Unsupported BsdMMsgHdr");

                return LinuxError.EOPNOTSUPP;
            }

            if (message.Messages.Length == 0)
            {
                return LinuxError.SUCCESS;
            }

            try
            {
                ArraySegment<byte>[] mmsgBuf = ConvertMessagesToBuffer(message);
                if (mmsgBuf.Length > 0 && mmsgBuf[0].Count > 5 && mmsgBuf[0].Array[mmsgBuf[0].Offset] == 0x16)
                {
                    Logger.Info?.Print(LogClass.ServiceBsd, $"[DIAG] Bsd.SendMMsg TLS hs=0x{mmsgBuf[0].Array[mmsgBuf[0].Offset + 5]:x2} len={mmsgBuf[0].Count} remote={RemoteEndPoint}");
                }
                SocketError socketError;
                int sendSize;

                if (Socket is DefaultSocket hostSocket)
                {
                    sendSize = hostSocket.BaseSocket.Send(mmsgBuf, ConvertBsdSocketFlags(flags), out socketError);
                }
                else
                {
                    // [Nextendo] Comme dans RecvMMsg : un socket virtuel (LAN Play, RyuLDN)
                    // n a pas d envoi vectorise, les segments partent donc l un apres l autre.
                    sendSize = SendSegments(mmsgBuf, ConvertBsdSocketFlags(flags), out socketError);
                }

                if (sendSize > 0)
                {
                    UpdateMessages(out vlen, message, sendSize);
                }

                return WinSockHelper.ConvertError((WsaError)socketError);
            }
            catch (SocketException exception)
            {
                if (exception.SocketErrorCode != SocketError.WouldBlock)
                {
                    Logger.Warning?.Print(LogClass.ServiceBsd, $"Socket Exception: {exception}");
                }

                return WinSockHelper.ConvertError((WsaError)exception.ErrorCode);
            }
        }
    }
}
