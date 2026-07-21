using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Ryujinx.Horizon.Bcat.Ipc
{
    // Serves BCAT delivery-cache content from a plain host folder next to the emulator:
    //   <exe dir>/bcat-seed/<directoryName>/<fileName>
    // This lets us inject the schedule data (e.g. Splatoon 2 vsdata/VSSetting_0.byaml) that a
    // live BCAT server would normally deliver, which Ryujinx never populates. Used as a fallback
    // by DeliveryCacheDirectoryService / DeliveryCacheFileService when the real (empty) cache misses.
    internal static class BcatSeed
    {
        public static string Root => System.IO.Path.Combine(AppContext.BaseDirectory, "bcat-seed");

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
