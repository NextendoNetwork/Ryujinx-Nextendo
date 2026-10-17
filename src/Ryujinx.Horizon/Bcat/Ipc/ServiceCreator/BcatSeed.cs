using Ryujinx.Common.Configuration;
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Ryujinx.Horizon.Bcat.Ipc
{
    // Serves BCAT delivery-cache content from a plain host folder:
    //   <bcat-seed root>/<directoryName>/<fileName>
    // This lets us inject the schedule data (e.g. Splatoon 2 vsdata/VSSetting_0.byaml) that a
    // live BCAT server would normally deliver, which Ryujinx never populates. Used as a fallback
    // by DeliveryCacheDirectoryService / DeliveryCacheFileService when the real (empty) cache misses.
    internal static class BcatSeed
    {
        // The seed lives ONLY in the WRITABLE app-data dir (portable/bcat-seed). It MUST be writable:
        // on macOS the .app bundle (AppContext.BaseDirectory) is read-only, so the download that
        // populates it failed there; same under Program Files on Windows.
        //
        // [Nextendo] The legacy next-to-exe fallback was REMOVED: shipping a static seed there let a
        // STALE copy be served whenever the writable one was absent, and users copied that stale
        // folder to other emulators (wrong Splatoon 2 rotation). The on-launch sync (NextendoByamlSync)
        // keeps this single writable location current from the server, so it is the only source read.
        public static string Root => System.IO.Path.Combine(AppDataManager.BaseDirPath, "bcat-seed");

        public static string RootForTitle(ulong titleId) => titleId switch
        {
            0x0100C2500FC20000 => System.IO.Path.Combine(Root, "0100c2500fc20000"),
            0x01008F6008C5E000 => System.IO.Path.Combine(Root, "01008f6008c5e000"),
            0x0100A3D008C5C000 => System.IO.Path.Combine(Root, "0100a3d008c5c000"),
            0x0100F43008C44000 => System.IO.Path.Combine(Root, "0100f43008c44000"),
            0x0100D2F00D5C0000 => System.IO.Path.Combine(Root, "0100d2f00d5c0000"),
            0x0100ABF008968000 => System.IO.Path.Combine(Root, "0100abf008968000"),
            0x01008DB008C2C000 => System.IO.Path.Combine(Root, "01008db008c2c000"),
            // [Nextendo] Super Smash Bros. Ultimate: the main menu news ticker (line_news folder)
            // comes from its delivery cache. Own folder, otherwise it would see the Splatoon 2
            // folders laid at the shared root.
            0x01006A800016E000 => System.IO.Path.Combine(Root, "01006a800016e000"),
            _ => Root,
        };

        public static bool IsTitleRoot(string name) =>
            name.Equals("0100c2500fc20000", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("01008f6008c5e000", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("0100a3d008c5c000", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("0100f43008c44000", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("0100d2f00d5c0000", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("0100abf008968000", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("01008db008c2c000", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("01006a800016e000", StringComparison.OrdinalIgnoreCase);

        public static bool PreferServerSeed(string root) =>
            root == System.IO.Path.Combine(Root, Ryujinx.Common.NextendoSplatoon3Bcat.TitleId) &&
            Ryujinx.Common.NextendoSplatoon3Bcat.IsInstalled(root);

        public static string DirPath(string dir) => System.IO.Path.Combine(Root, dir);

        public static string FilePath(string dir, string file) => System.IO.Path.Combine(Root, dir, file);

        // Decode a LibHac fixed-size name struct (Array32<byte>, NUL-terminated) to a string.
        public static string ToName<T>(ref T name) where T : unmanaged
        {
            ReadOnlySpan<byte> b = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref name, 1));
            return Encoding.ASCII.GetString(b).Split('\0')[0];
        }

        // Write an ASCII string into a fixed-size name struct (NUL-padded).
        public static void FillName<T>(ref T name, string s) where T : unmanaged
        {
            Span<byte> b = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref name, 1));
            b.Clear();
            byte[] src = Encoding.ASCII.GetBytes(s);
            src.AsSpan(0, Math.Min(src.Length, b.Length - 1)).CopyTo(b);
        }

        // Copy raw bytes into a fixed-size struct (e.g. a 16-byte Digest).
        public static void FillRaw<T>(ref T dst, ReadOnlySpan<byte> src) where T : unmanaged
        {
            Span<byte> b = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref dst, 1));
            b.Clear();
            src.Slice(0, Math.Min(src.Length, b.Length)).CopyTo(b);
        }
    }
}
