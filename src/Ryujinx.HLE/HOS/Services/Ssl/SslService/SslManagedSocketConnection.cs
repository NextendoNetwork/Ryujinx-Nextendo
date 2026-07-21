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

        public ResultCode Handshake(string hostName)
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
            _stream = new SslStream(new NetworkStream(((DefaultSocket)((ManagedSocket)Socket).Socket).BaseSocket, false), false, certCallback, null);
            string origHost = hostName;
            hostName = RetrieveHostName(hostName);
            Logger.Info?.Print(LogClass.ServiceSsl, $"[DIAG] nn::ssl Handshake remote={Socket.RemoteEndPoint} hostIn='{origHost}' hostUsed='{hostName}'");

            // [Nextendo] ALPN policy. We used to advertise BOTH h2 and http/1.1 (h2 first)
            // so HTTP/2 backends would negotiate HTTP/2. But NEX (MK8/Splatoon 2) is PRUDP
            // over WebSocket, which REQUIRES the HTTP/1.1 Upgrade handshake: when a Go server offered h2
            // in its ALPN list it selected h2 (server-side preference wins), the WS upgrade could not
            // happen, and the console never reached the online hall (regression, 2026-07-11). NEX is
            // the active target, so advertise ONLY http/1.1 so no server can
            // ever pick h2. If an HTTP/2 backend is ever needed, re-add h2 SCOPED to those hosts only (e.g.
            // by hostName), never globally.
            var sslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                TargetHost = hostName,
                EnabledSslProtocols = TranslateSslVersion(_sslVersion),
                CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck,
                RemoteCertificateValidationCallback = certCallback,
                ApplicationProtocols = new System.Collections.Generic.List<System.Net.Security.SslApplicationProtocol>
                {
                    System.Net.Security.SslApplicationProtocol.Http11,
                },
            };
            _stream.AuthenticateAsClient(sslOptions);
            Logger.Info?.Print(LogClass.ServiceSsl, $"[DIAG] nn::ssl ALPN négocié = '{_stream.NegotiatedApplicationProtocol}'");
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
