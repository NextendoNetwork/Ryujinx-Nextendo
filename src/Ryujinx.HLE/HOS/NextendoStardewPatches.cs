using Ryujinx.Common.Logging;
using Ryujinx.HLE.Loaders.Mods;
using System.Collections.Generic;
using System.IO;

namespace Ryujinx.HLE.HOS
{
    /// <summary>
    /// Built-in Stardew Valley patches (1.6.15.13, main E7F845093E8CBC68DACF011CCB620D6667B5A20B),
    /// same rationale and IPS32 format as NextendoS3Patches. The game does TLS in-game (static
    /// OpenSSL): X509_verify_cert is forced to 1, then the NPLN SDK's "certificate accepted" byte
    /// (LDRB W10,[X21,#0x38]) is forced to 1, otherwise the auth call is cancelled before HEADERS
    /// (2321-4992).
    /// </summary>
    internal static class NextendoStardewPatches
    {
        private static readonly byte[] _certificateChainBypass =
        [
            0x49, 0x50, 0x53, 0x33, 0x32,             // "IPS32"
            0x07, 0x9B, 0x4D, 0x10, 0x00, 0x08,       // offset fichier 0x079B4D10, 8 bytes
            0x20, 0x00, 0x80, 0x52,                   // MOV W0, #1
            0xC0, 0x03, 0x5F, 0xD6,                   // RET
            0x45, 0x45, 0x4F, 0x46,                   // "EEOF"
        ];

        private static readonly byte[] _certificateAcceptedBypass =
        [
            0x49, 0x50, 0x53, 0x33, 0x32,             // "IPS32"
            0x07, 0x82, 0xF6, 0xD0, 0x00, 0x04,       // offset fichier 0x0782F6D0, 4 bytes
            0x2A, 0x00, 0x80, 0x52,                   // MOV W10, #1
            0x45, 0x45, 0x4F, 0x46,                   // "EEOF"
        ];

        private static readonly Dictionary<string, byte[][]> _byBuildId = new()
        {
            ["E7F845093E8CBC68DACF011CCB620D6667B5A20B"] = [_certificateChainBypass, _certificateAcceptedBypass],
        };

        public static int Verser(string buildId, MemPatch target)
        {
            if (string.IsNullOrEmpty(buildId) ||
                !_byBuildId.TryGetValue(buildId, out byte[][] patches))
            {
                return 0;
            }

            foreach (byte[] bytes in patches)
            {
                using MemoryStream stream = new(bytes);
                using BinaryReader reader = new(stream);

                new IpsPatcher(reader).AddPatches(target);
            }

            Logger.Info?.Print(LogClass.ModLoader,
                $"[Nextendo] Stardew: {patches.Length} built-in patch(es) applied (build {buildId})");

            return patches.Length;
        }
    }
}
