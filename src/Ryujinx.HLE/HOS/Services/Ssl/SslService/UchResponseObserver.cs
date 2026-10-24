using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Ryujinx.HLE.HOS.Services.Ssl.SslService
{
    /// <summary>
    /// Reads the HTTP responses a UCH TLS connection receives and hands each decoded JSON body to a callback
    /// (the NXU1 ticket extension of the login response). It only observes: it never changes what the game reads,
    /// never logs or stores any header or body, and gives up silently on anything it cannot parse.
    /// </summary>
    internal sealed class UchResponseObserver
    {
        private const int Limit = 131072;

        private readonly MemoryStream _pending = new();
        private readonly Action<JsonElement> _body;

        public UchResponseObserver(Action<JsonElement> body)
        {
            _body = body;
        }

        private void Reset() => _pending.SetLength(0);

        public void Observe(ReadOnlySpan<byte> bytes)
        {
            try
            {
                if (_pending.Length + bytes.Length > Limit)
                {
                    Reset();

                    return;
                }

                _pending.Write(bytes);

                byte[] all = _pending.ToArray();
                string text = Encoding.Latin1.GetString(all);
                int split = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);

                if (split < 0)
                {
                    return;
                }

                string[] headers = text[..split].Split("\r\n");
                int length = -1;
                bool chunked = false;
                bool gzip = false;
                string[] firstLine = headers[0].Split(' ');
                bool isResponse = firstLine.Length >= 2 && firstLine[0].StartsWith("HTTP/", StringComparison.Ordinal) && int.TryParse(firstLine[1], out _);
                int responseStatus = isResponse ? int.Parse(firstLine[1], CultureInfo.InvariantCulture) : 0;

                foreach (string header in headers)
                {
                    if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        length = int.TryParse(header[15..].Trim(), out int parsed) && parsed >= 0 ? parsed : -1;
                    }

                    if (header.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase))
                    {
                        chunked = header.Contains("chunked", StringComparison.OrdinalIgnoreCase);
                    }

                    if (header.StartsWith("Content-Encoding:", StringComparison.OrdinalIgnoreCase))
                    {
                        gzip = header.Contains("gzip", StringComparison.OrdinalIgnoreCase);
                    }
                }

                int bodyStart = split + 4;

                // Interim responses have no body and precede the final response.
                if (isResponse && (responseStatus >= 100 && responseStatus < 200 || responseStatus == 204 || responseStatus == 304))
                {
                    Reset();

                    if (bodyStart < all.Length)
                    {
                        Observe(all.AsSpan(bodyStart));
                    }

                    return;
                }

                byte[] body;
                int consumed;

                if (chunked)
                {
                    using MemoryStream decoded = new();
                    int pos = bodyStart;

                    while (true)
                    {
                        int end = text.IndexOf("\r\n", pos, StringComparison.Ordinal);

                        if (end < 0)
                        {
                            return;
                        }

                        string size = text[pos..end].Split(';')[0];

                        if (!int.TryParse(size, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int count) || count < 0 || count > Limit)
                        {
                            return;
                        }

                        pos = end + 2;

                        if (count == 0)
                        {
                            if (all.Length < pos + 2)
                            {
                                return;
                            }

                            if (text.Substring(pos, 2) == "\r\n")
                            {
                                consumed = pos + 2;
                            }
                            else
                            {
                                int trailerEnd = text.IndexOf("\r\n\r\n", pos, StringComparison.Ordinal);

                                if (trailerEnd < 0)
                                {
                                    return;
                                }

                                consumed = trailerEnd + 4;
                            }

                            body = decoded.ToArray();

                            break;
                        }

                        if (all.Length - pos < count + 2 || decoded.Length + count > Limit)
                        {
                            return;
                        }

                        decoded.Write(all, pos, count);
                        pos += count + 2;
                    }
                }
                else
                {
                    if (length < 0 || length > Limit || all.Length - bodyStart < length)
                    {
                        return;
                    }

                    body = all.AsSpan(bodyStart, length).ToArray();
                    consumed = bodyStart + length;
                }

                if (gzip)
                {
                    using GZipStream zip = new(new MemoryStream(body), CompressionMode.Decompress);
                    using MemoryStream decoded = new();
                    byte[] block = new byte[4096];
                    int count;

                    while ((count = zip.Read(block)) > 0)
                    {
                        if (decoded.Length + count > Limit)
                        {
                            throw new InvalidDataException();
                        }

                        decoded.Write(block, 0, count);
                    }

                    body = decoded.ToArray();
                }

                try
                {
                    using JsonDocument document = JsonDocument.Parse(body);
                    _body?.Invoke(document.RootElement);
                }
                catch (JsonException)
                {
                    // Not a JSON body: nothing to observe.
                }

                Reset();

                if (consumed < all.Length)
                {
                    Observe(all.AsSpan(consumed));
                }
            }
            catch
            {
                // Observing must never change SSL behavior or leak exception payloads.
                Reset();
            }
        }
    }
}
