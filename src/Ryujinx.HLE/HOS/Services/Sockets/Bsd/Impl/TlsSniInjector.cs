using System;
using System.Text;

namespace Ryujinx.HLE.HOS.Services.Sockets.Bsd.Impl
{
    // [Nextendo] Splices a Server Name Indication (SNI) extension into a TLS
    // ClientHello that lacks one. Some games' bundled TLS client opens its connection by IP
    // WITHOUT an SNI under emulation, so our reverse-proxy (which routes by
    // SNI) can't reach the right backend and drops the connection. We recover the original
    // hostname (from the DNS redirect) and inject it so the connection routes cleanly via TLS
    // passthrough — exactly like a real Switch.
    static class TlsSniInjector
    {
        public static bool TryInject(ReadOnlySpan<byte> input, string hostName, out byte[] output)
        {
            output = null;

            // TLS record header: content_type=0x16 (handshake), version(2), length(2).
            if (input.Length < 43 || input[0] != 0x16)
            {
                return false;
            }

            int recordLen = (input[3] << 8) | input[4];
            if (5 + recordLen > input.Length)
            {
                return false; // ClientHello spans more than this buffer — don't touch it
            }

            // Handshake header: msg_type=0x01 (ClientHello), length(3).
            if (input[5] != 0x01)
            {
                return false;
            }

            int p = 5 + 4;   // record header (5) + handshake header (4)
            p += 2;          // client_version
            p += 32;         // random
            if (p >= input.Length) return false;
            int sidLen = input[p]; p += 1 + sidLen;                       // legacy_session_id
            if (p + 2 > input.Length) return false;
            int csLen = (input[p] << 8) | input[p + 1]; p += 2 + csLen;   // cipher_suites
            if (p + 1 > input.Length) return false;
            int cmLen = input[p]; p += 1 + cmLen;                         // compression_methods
            if (p + 2 > input.Length) return false;

            int extTotalLen = (input[p] << 8) | input[p + 1];
            int extLenPos = p;
            int extStart = p + 2;
            int extEnd = extStart + extTotalLen;
            if (extEnd > input.Length) return false;

            // Already has a server_name (0x0000) extension? Leave it alone.
            int q = extStart;
            while (q + 4 <= extEnd)
            {
                int etype = (input[q] << 8) | input[q + 1];
                int elen = (input[q + 2] << 8) | input[q + 3];
                if (etype == 0x0000)
                {
                    return false;
                }
                q += 4 + elen;
            }

            byte[] host = Encoding.ASCII.GetBytes(hostName);
            int nameLen = host.Length;
            int listLen = 1 + 2 + nameLen;   // name_type(1) + name_len(2) + host
            int extDataLen = 2 + listLen;    // server_name_list length(2) + list
            int sniExtLen = 4 + extDataLen;  // ext_type(2) + ext_len(2) + data

            byte[] sni = new byte[sniExtLen];
            int i = 0;
            sni[i++] = 0x00; sni[i++] = 0x00;                            // extension_type = server_name
            sni[i++] = (byte)(extDataLen >> 8); sni[i++] = (byte)extDataLen;
            sni[i++] = (byte)(listLen >> 8); sni[i++] = (byte)listLen;
            sni[i++] = 0x00;                                             // name_type = host_name
            sni[i++] = (byte)(nameLen >> 8); sni[i++] = (byte)nameLen;
            Array.Copy(host, 0, sni, i, nameLen);

            // Splice the SNI extension in at the start of the extensions block.
            output = new byte[input.Length + sniExtLen];
            input.Slice(0, extStart).CopyTo(output);
            Array.Copy(sni, 0, output, extStart, sniExtLen);
            input.Slice(extStart).CopyTo(output.AsSpan(extStart + sniExtLen));

            // Patch the three length fields that now cover sniExtLen more bytes.
            int newExtLen = extTotalLen + sniExtLen;
            output[extLenPos] = (byte)(newExtLen >> 8);
            output[extLenPos + 1] = (byte)newExtLen;

            int hsLen = ((input[6] << 16) | (input[7] << 8) | input[8]) + sniExtLen;
            output[6] = (byte)(hsLen >> 16); output[7] = (byte)(hsLen >> 8); output[8] = (byte)hsLen;

            int newRecLen = recordLen + sniExtLen;
            output[3] = (byte)(newRecLen >> 8); output[4] = (byte)newRecLen;

            return true;
        }
    }
}
