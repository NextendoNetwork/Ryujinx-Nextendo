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
        // son ensemble de sondage sans interet en lecture (mesure : 39 s avec req=0) ; un POLLHUP a niveau
        // ferait alors rendre chaque poll instantanement, donc une boucle folle. On previent une fois, ce qui
        // suffit a rompre la cecite, sans risquer la boucle si l'invite ignore l'avertissement.
        public bool HangupReported { get; set; }

        // [Nextendo] getsockopt doit rendre ce que setsockopt a accepte. Quand SetSocketOption feint le succes
        // sur une option que cette emulation ne sait pas traduire (voir les branches tolerantes plus bas), on
        // retient la valeur ici pour que GetSocketOption la rende au lieu d'un EOPNOTSUPP. grpc-core pose
        // TCP_NODELAY / SO_REUSEADDR et les relit aussitot : un « set OK / get erreur » le faisait fermer le
        // socket avant meme le connect, donc avant le moindre echange.
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
            // [Nextendo] Le resolveur gRPC de certains clients en ligne desassemble mal l'addrinfo qu'on lui
            // rend et tend au socket 0.0.0.0 / :: — l'adresse est perdue, le port survit. Le connect echoue
            // alors sans qu'aucun ClientHello ne parte. On substitue la redirection DNS notee POUR CE PORT,
            // afin que la connexion atteigne le service reellement vise. Le HTTP passe par GetHostByName (un
            // autre chemin) et n'est jamais a 0.0.0.0 : seul le connect gRPC casse est rattrape ici. Socket
            // IPv6 a double pile -> l'adresse IPv4 est mappee en ::ffff:.
            bool grpcConnect = false;
            {
                IPAddress a = remoteEndPoint.Address;
                bool isAny = a.Equals(IPAddress.Any) || a.Equals(IPAddress.IPv6Any)
                             || (a.IsIPv4MappedToIPv6 && a.MapToIPv4().Equals(IPAddress.Any));
                // Repli choisi PAR PORT : le port designe le service vise sans ambiguite. En prenant « la
                // derniere resolution », toutes destinations confondues, une connexion pouvait partir vers le
                // serveur d'un autre jeu — mesure du 2026-08-15, douze fois sur vingt-deux chez un testeur,
                // ce qui rendait la jonction en partie privee aleatoire.
                IPAddress sub = Ryujinx.HLE.HOS.Services.Sockets.Sfdnsres.Proxy.DnsMitmResolver.RedirectionPour(remoteEndPoint.Port);
                if (isAny && sub != null)
                {
                    if (Socket.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                        && sub.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        sub = sub.MapToIPv6();
                    }

                    Logger.Info?.PrintMsg(LogClass.ServiceBsd, $"[Nextendo] Connect target was {a}:{remoteEndPoint.Port} (lost address) -> substituting the redirect noted for this port");
                    remoteEndPoint = new IPEndPoint(sub, remoteEndPoint.Port);
                    grpcConnect = true;
                }
                else if (isAny)
                {
                    // Adresse perdue sur un port SANS redirection connue : typiquement un pair du jeu en
                    // ligne. On ne substitue rien — envoyer ce trafic ailleurs casserait le pair-a-pair — mais
                    // on le trace, parce que c'est exactement la mesure qui manquera pour comprendre un echec
                    // d'etablissement P2P.
                    Logger.Debug?.PrintMsg(LogClass.ServiceBsd,
                        $"[Nextendo] Connect target was {a}:{remoteEndPoint.Port} (lost address) — aucune redirection pour ce port, on ne substitue pas (pair ?)");
                }
            }

            // [Nextendo] Loopback is shown as well as the LAN: a redirect that lands on 127.0.0.1 means the
            // server address was never configured, and hiding it turned that into an unexplainable failure.
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
                // [Nextendo] SocketErrorCode, not ErrorCode: on Unix the latter is the native errno (EAGAIN
                // is 11), not the Winsock value (10035), so this test was only ever true on Windows. On Linux
                // and macOS a non-blocking connect therefore skipped the whole branch below and reported
                // EAGAIN to the guest instead of EINPROGRESS, which is not a connect() error at all — and the
                // synchronous-completion workaround for the online service never ran there either.
                if (!Blocking && exception.SocketErrorCode == SocketError.WouldBlock)
                {
                    // [Nextendo] Pour le connect gRPC, ne PAS rendre EINPROGRESS en comptant sur le client
                    // pour sonder le socket en POLLOUT : c'est RACE. Il n'ajoute le socket a son ensemble de
                    // sondage que tardivement, si bien que l'achevement etait detecte jusqu'a ~74 s plus tard
                    // et que tout le parcours en ligne calait sur certaines executions. Cote hote le connect
                    // aboutit en une dizaine de millisecondes : on bloque donc brievement ici et on rend
                    // SUCCESS des que le socket est inscriptible (donc connecte). Le client repart aussitot
                    // sur TLS/gRPC — deterministe. Limite aux connexions gRPC (celles dont l'adresse a ete
                    // substituee) ; les autres sockets gardent l'EINPROGRESS non bloquant d'origine.
                    // Les deux branches ont ete comparees, le correctif de revents en place :
                    //   SUCCESS ici  -> le client prend sa branche « deja connecte » et la poignee de main TLS
                    //                   part vraiment.
                    //   EINPROGRESS  -> il enregistre bien le socket (94 728 sondages, rev=Output) mais
                    //                   n'execute jamais son on_writable : il n'emet STRICTEMENT RIEN.
                    // NEXTENDO_GRPC_CONNECT_SYNC=0 permet de retenter la voie asynchrone.
                    if (grpcConnect && Environment.GetEnvironmentVariable("NEXTENDO_GRPC_CONNECT_SYNC") != "0")
                    {
                        try
                        {
                            if (Socket.Poll(2_000_000, SelectMode.SelectWrite)
                                && !Socket.Poll(0, SelectMode.SelectError))
                            {
                                Logger.Info?.PrintMsg(LogClass.ServiceBsd, "[Nextendo] grpc connect completed synchronously (blocked for host completion) -> SUCCESS");

                                return LinuxError.SUCCESS;
                            }

                            // [Nextendo] The connect did not complete: the client sends its ClientHello anyway
                            // and the game reports a network error (2321-4992 for NPLN), so the reason has to
                            // be in the log rather than left to be guessed.
                            byte[] error = new byte[4];

                            Socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error, error);

                            SocketError reason = (SocketError)BitConverter.ToInt32(error);

                            Logger.Error?.PrintMsg(LogClass.ServiceBsd,
                                $"[Nextendo] the connection to the online service at port {remoteEndPoint.Port} failed ({reason}). " +
                                (IPAddress.IsLoopback(remoteEndPoint.Address) ||
                                 (remoteEndPoint.Address.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(remoteEndPoint.Address.MapToIPv4()))
                                    ? "It was redirected to loopback, which means the server address is not configured: " +
                                      "set NEXTENDO_SERVER_IP (and NEXTENDO_NAT_IP) before launching, or bake them into the build."
                                    : "The server is not answering on that address from this machine."));
                        }
                        catch { /* on retombe sur EINPROGRESS */ }
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

                    return LinuxError.EOPNOTSUPP;
                }

                receiveSize = Socket.ReceiveFrom(buffer[..size], ConvertBsdSocketFlags(flags), ref temp);

                remoteEndPoint = (IPEndPoint)temp;
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

        // [Nextendo] Remplit optionValue avec la valeur qu'un setsockopt precedent a feint d'accepter (ou des
        // zeros), pour que getsockopt rende ce que le client a pose au lieu d'echouer.
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
                    // [Nextendo] Tolerant, en miroir de SetSocketOption : rendre la valeur posee par le client
                    // (ou zero) avec SUCCESS. Un « set OK / get erreur » faisait fermer le socket avant connect.
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

        /// <summary>
        /// Sends the segments of a scatter/gather message one at a time, for socket implementations that
        /// have no vectored send. Stops at the first segment that is not fully accepted, like a host socket
        /// would when its send buffer fills up.
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
        /// Fills the segments of a scatter/gather message one at a time, for socket implementations that
        /// have no vectored receive. Stops as soon as a segment is not filled completely, so a datagram
        /// shorter than the segments does not block waiting for the rest.
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

        // TODO: Find a way to support passing the timeout somehow without changing the socket ReceiveTimeout.
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
                    // [Nextendo] The socket is not always backed by a host socket: with LAN Play or RyuLDN
                    // selected it is a virtual one, which has no scatter/gather receive, so the segments are
                    // filled one after another instead of casting (which used to throw here).
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
                    // [Nextendo] Same as in RecvMMsg: a virtual socket (LAN Play, RyuLDN) has no
                    // scatter/gather send, so the segments go out one after another.
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
