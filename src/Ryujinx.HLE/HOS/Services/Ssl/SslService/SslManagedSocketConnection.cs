using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd.Impl;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd.Proxy;
using Ryujinx.HLE.HOS.Services.Ssl.Types;
using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace Ryujinx.HLE.HOS.Services.Ssl.SslService
{
    class SslManagedSocketConnection : ISslConnectionBase
    {
        public int SocketFd { get; }

        public ISocket Socket { get; }

        private readonly BsdContext _bsdContext;
        private readonly SslVersion _sslVersion;
        private SslStream _stream;
        private bool _isBlockingSocket;
        private int _previousReadTimeout;

        public SslManagedSocketConnection(BsdContext bsdContext, SslVersion sslVersion, int socketFd, ISocket socket)
        {
            _bsdContext = bsdContext;
            _sslVersion = sslVersion;

            SocketFd = socketFd;
            Socket = socket;
        }

        private void StartSslOperation()
        {
            // Save blocking state
            _isBlockingSocket = Socket.Blocking;

            // Force blocking for SslStream
            Socket.Blocking = true;
        }

        private void EndSslOperation()
        {
            // Restore blocking state
            Socket.Blocking = _isBlockingSocket;
        }

        private void StartSslReadOperation()
        {
            StartSslOperation();

            if (!_isBlockingSocket)
            {
                _previousReadTimeout = _stream.ReadTimeout;

                _stream.ReadTimeout = 1;
            }
        }

        private void EndSslReadOperation()
        {
            if (!_isBlockingSocket)
            {
                _stream.ReadTimeout = _previousReadTimeout;
            }

            EndSslOperation();
        }

        // NOTE: We silence warnings about TLS 1.0 and 1.1 as games will likely use it.
#pragma warning disable SYSLIB0039
        private SslProtocols TranslateSslVersion(SslVersion version)
        {
            return (version & SslVersion.VersionMask) switch
            {
                SslVersion.Auto => SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12 | SslProtocols.Tls13,
                SslVersion.TlsV10 => SslProtocols.Tls,
                SslVersion.TlsV11 => SslProtocols.Tls11,
                SslVersion.TlsV12 => SslProtocols.Tls12,
                SslVersion.TlsV13 => SslProtocols.Tls13,
                _ => throw new NotImplementedException(version.ToString()),
            };
        }
#pragma warning restore SYSLIB0039

        /// <summary>
        /// Retrieve the hostname of the current remote in case the provided hostname is null or empty.
        /// </summary>
        /// <param name="hostName">The current hostname</param>
        /// <returns>Either the resolved or provided hostname</returns>
        /// <remarks>
        /// This is done to avoid getting an <see cref="System.Security.Authentication.AuthenticationException"/>
        /// as the remote certificate will be rejected with <c>RemoteCertificateNameMismatch</c> due to an empty hostname.
        /// This is not what the switch does!
        /// It might just skip remote hostname verification if the hostname wasn't set with <see cref="ISslConnection.SetHostName"/> before.
        /// TODO: Remove this as soon as we know how the switch deals with empty hostnames
        /// </remarks>
        private string RetrieveHostName(string hostName)
        {
            if (!string.IsNullOrEmpty(hostName))
            {
                return hostName;
            }

            // [Nextendo] The game opened this TLS connection by IP without setting a
            // hostname (some games do this). Recover the original hostname we DNS-redirected to this
            // IP so we send the correct SNI — otherwise our reverse-proxy (routes by SNI)
            // can't reach the right backend and drops the connection.
            try
            {
                string ip = ((System.Net.IPEndPoint)Socket.RemoteEndPoint).Address.ToString();
                if (Ryujinx.HLE.HOS.Services.Sockets.Sfdnsres.Proxy.DnsMitmResolver.LastHostForIp.TryGetValue(ip, out string original)
                    && !string.IsNullOrEmpty(original))
                {
                    Logger.Info?.PrintMsg(LogClass.ServiceSsl, $"SNI recovered from DNS redirect: {ip} -> {original}");
                    return original;
                }
            }
            catch { /* fall through to reverse-DNS */ }

            try
            {
                return Dns.GetHostEntry(Socket.RemoteEndPoint.Address).HostName;
            }
            catch (SocketException)
            {
                return hostName;
            }
        }

        // [Nextendo] Parse an ALPN wire-format buffer (a sequence of [len][name] entries, exactly as
        // the game passes it to nn::ssl::Connection::SetNextAlpnProto) into a .NET protocol list.
        // Null/empty -> empty list, and the caller keeps its default policy.
        private static System.Collections.Generic.List<SslApplicationProtocol> ParseAlpnWire(byte[] wire)
        {
            var list = new System.Collections.Generic.List<SslApplicationProtocol>();

            if (wire == null)
            {
                return list;
            }

            int i = 0;
            while (i < wire.Length)
            {
                int len = wire[i++];
                if (len <= 0 || i + len > wire.Length)
                {
                    break;
                }

                string name = System.Text.Encoding.ASCII.GetString(wire, i, len);
                i += len;

                // Only the two protocols NPLN/NEX ever ask for. Unknown names are skipped on purpose:
                // we never request others, and this avoids depending on the SslApplicationProtocol(byte[])
                // constructor, which is missing from the packaged runtime (MissingMethodException at JIT
                // time, i.e. a crash the moment a game negotiates TLS).
                if (name == "h2")
                {
                    list.Add(SslApplicationProtocol.Http2);
                }
                else if (name == "http/1.1")
                {
                    list.Add(SslApplicationProtocol.Http11);
                }
            }

            return list;
        }

        public ResultCode Handshake(string hostName, byte[] alpnWire = null)
        {
            StartSslOperation();
            // [Nextendo] Accept ANY server certificate so the emulated game can
            // TLS-handshake against our private/self-hosted servers (which present a self-signed
            // cert for Nintendo's hostnames). This is a dedicated private-online fork whose entire
            // purpose is to redirect Nintendo traffic to our own localhost/remote servers, so we always
            // bypass cert validation. (Was once gated behind an environment variable, but env-var
            // propagation to GUI-launched processes proved unreliable -> made unconditional.)
            System.Net.Security.RemoteCertificateValidationCallback certCallback =
                (sender, cert, chain, errors) => true;
            // [Nextendo] The socket is not always backed by a host socket: with LAN Play or RyuLDN
            // selected it is a virtual one, and casting it to DefaultSocket used to throw an
            // InvalidCastException here, which surfaced as the game failing to reach the online service.
            ISocketImpl socketImpl = ((ManagedSocket)Socket).Socket;

            Stream socketStream = socketImpl is DefaultSocket hostSocket
                ? new NetworkStream(hostSocket.BaseSocket, false)
                : new SocketImplStream(socketImpl);

            _stream = new SslStream(socketStream, false, certCallback, null);
            string origHost = hostName;
            hostName = RetrieveHostName(hostName);

            // [Nextendo] ALPN policy. DEFAULT = http/1.1 ONLY: NEX (MK8/Splatoon 2) is PRUDP over
            // WebSocket, which REQUIRES the HTTP/1.1 Upgrade handshake. When we advertised h2 globally,
            // an HTTP/2-capable server selected h2 (server-side preference wins), the WS upgrade could
            // never happen, and the console never reached the online hall (regression, 2026-07-11).
            // So http/1.1 stays the default for everyone.
            var appProtocols = new System.Collections.Generic.List<SslApplicationProtocol>
            {
                SslApplicationProtocol.Http11,
            };

            // [Nextendo] EXCEPTION, scoped by host: NPLN (Splatoon 3) is gRPC, which MANDATES HTTP/2.
            // On those hosts we HONOR the ALPN list the game itself requested through SetNextAlpnProto
            // (h2) instead of forcing http/1.1. Without it the front end reads our HTTP/2 preface as an
            // HTTP/1.1 request and answers 404.
            //
            // The match is a substring, on purpose, because the NPLN *session* transport is a second
            // gRPC endpoint that does NOT carry an "npln" host: after matchmaking the game connects
            // straight to the session address with SNI "gs.nintendo.net". That one fell outside an
            // exact-host test, so it got http/1.1 and the gRPC server never saw a single RPC. Symptom,
            // measured: the TLS handshake completed (certificate and Finished exchanged) then nothing
            // moved, the game rebuilt the connection every ~3 s, the private match died on
            // KeepUserSession (2321-4992) and the session server's log stayed completely empty.
            //
            // Scoped by host so NEX is never affected.
            bool isNplnHost = origHost != null &&
                (origHost.Contains("npln", StringComparison.OrdinalIgnoreCase) ||
                 origHost.Contains("gs.nintendo.net", StringComparison.OrdinalIgnoreCase));

            if (isNplnHost)
            {
                var requested = ParseAlpnWire(alpnWire);
                if (requested.Count > 0)
                {
                    appProtocols = requested;
                }
            }

            var sslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                TargetHost = hostName,
                EnabledSslProtocols = TranslateSslVersion(_sslVersion),
                CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck,
                RemoteCertificateValidationCallback = certCallback,
                ApplicationProtocols = appProtocols,
            };
            _stream.AuthenticateAsClient(sslOptions);
            EndSslOperation();

            return ResultCode.Success;
        }

        public ResultCode Peek(out int peekCount, Memory<byte> buffer)
        {
            // NOTE: We cannot support that on .NET SSL API.
            // As Nintendo's curl implementation detail check if a connection is alive via Peek, we just return that it would block to let it know that it's alive.
            peekCount = -1;

            return ResultCode.WouldBlock;
        }

        public int Pending()
        {
            // Unsupported
            return 0;
        }

        private bool TryTranslateWinSockError(bool isBlocking, WsaError error, out ResultCode resultCode)
        {
            switch (error)
            {
                case WsaError.WSAETIMEDOUT:
                    resultCode = isBlocking ? ResultCode.Timeout : ResultCode.WouldBlock;
                    return true;
                case WsaError.WSAECONNABORTED:
                    resultCode = ResultCode.ConnectionAbort;
                    return true;
                case WsaError.WSAECONNRESET:
                    resultCode = ResultCode.ConnectionReset;
                    return true;
                default:
                    resultCode = ResultCode.Success;
                    return false;
            }
        }

        public ResultCode Read(out int readCount, Memory<byte> buffer)
        {
            if (!Socket.Poll(0, SelectMode.SelectRead))
            {
                readCount = -1;

                return ResultCode.WouldBlock;
            }

            StartSslReadOperation();

            try
            {
                readCount = _stream.Read(buffer.Span);
            }
            catch (IOException exception)
            {
                readCount = -1;

                if (exception.InnerException is SocketException socketException)
                {
                    WsaError socketErrorCode = (WsaError)socketException.SocketErrorCode;

                    if (TryTranslateWinSockError(_isBlockingSocket, socketErrorCode, out ResultCode result))
                    {
                        return result;
                    }
                    else
                    {
                        throw socketException;
                    }
                }
                else
                {
                    throw;
                }
            }
            finally
            {
                EndSslReadOperation();
            }

            return ResultCode.Success;
        }

        public ResultCode Write(out int writtenCount, ReadOnlyMemory<byte> buffer)
        {
            if (!Socket.Poll(0, SelectMode.SelectWrite))
            {
                writtenCount = 0;

                return ResultCode.WouldBlock;
            }

            StartSslOperation();

            try
            {
                _stream.Write(buffer.Span);
            }
            catch (IOException exception)
            {
                writtenCount = -1;

                if (exception.InnerException is SocketException socketException)
                {
                    WsaError socketErrorCode = (WsaError)socketException.SocketErrorCode;

                    if (TryTranslateWinSockError(_isBlockingSocket, socketErrorCode, out ResultCode result))
                    {
                        return result;
                    }
                    else
                    {
                        throw socketException;
                    }
                }
                else
                {
                    throw;
                }
            }
            finally
            {
                EndSslOperation();
            }

            // .NET API doesn't provide the size written, assume all written.
            writtenCount = buffer.Length;

            return ResultCode.Success;
        }

        public ResultCode GetServerCertificate(string hostname, Span<byte> certificates, out uint storageSize, out uint certificateCount)
        {
            byte[] rawCertData = _stream.RemoteCertificate.GetRawCertData();

            storageSize = (uint)rawCertData.Length;
            certificateCount = 1;

            if (rawCertData.Length > certificates.Length)
            {
                return ResultCode.CertBufferTooSmall;
            }

            rawCertData.CopyTo(certificates);

            return ResultCode.Success;
        }

        public void Dispose()
        {
            _bsdContext.CloseFileDescriptor(SocketFd);
        }
    }
}
