using System.Runtime.InteropServices;

namespace ARMeilleure.Instructions
{
    static partial class SoftFallback
    {
        private const uint Crc32RevPoly = 0xedb88320;
        private const uint Crc32cRevPoly = 0x82f63b78;

        [UnmanagedCallersOnly]
        public static uint Crc32b(uint crc, byte value) => Crc32(crc, Crc32RevPoly, value);
        [UnmanagedCallersOnly]
        public static uint Crc32h(uint crc, ushort value) => Crc32h(crc, Crc32RevPoly, value);
        [UnmanagedCallersOnly]
        public static uint Crc32w(uint crc, uint value) => Crc32w(crc, Crc32RevPoly, value);
        [UnmanagedCallersOnly]
        public static uint Crc32x(uint crc, ulong value) => Crc32x(crc, Crc32RevPoly, value);

        [UnmanagedCallersOnly]
        public static uint Crc32cb(uint crc, byte value) => Crc32(crc, Crc32cRevPoly, value);
        [UnmanagedCallersOnly]
        public static uint Crc32ch(uint crc, ushort value) => Crc32h(crc, Crc32cRevPoly, value);
        [UnmanagedCallersOnly]
        public static uint Crc32cw(uint crc, uint value) => Crc32w(crc, Crc32cRevPoly, value);
        [UnmanagedCallersOnly]
        public static uint Crc32cx(uint crc, ulong value) => Crc32x(crc, Crc32cRevPoly, value);

        private static uint Crc32h(uint crc, uint poly, ushort val)
        {
            crc = Crc32(crc, poly, (byte)(val >> 0));
            crc = Crc32(crc, poly, (byte)(val >> 8));

            return crc;
        }

        private static uint Crc32w(uint crc, uint poly, uint val)
        {
            crc = Crc32(crc, poly, (byte)(val >> 0));
            crc = Crc32(crc, poly, (byte)(val >> 8));
            crc = Crc32(crc, poly, (byte)(val >> 16));
            crc = Crc32(crc, poly, (byte)(val >> 24));

            return crc;
        }

        private static uint Crc32x(uint crc, uint poly, ulong val)
        {
            crc = Crc32(crc, poly, (byte)(val >> 0));
            crc = Crc32(crc, poly, (byte)(val >> 8));
            crc = Crc32(crc, poly, (byte)(val >> 16));
            crc = Crc32(crc, poly, (byte)(val >> 24));
            crc = Crc32(crc, poly, (byte)(val >> 32));
            crc = Crc32(crc, poly, (byte)(val >> 40));
            crc = Crc32(crc, poly, (byte)(val >> 48));
            crc = Crc32(crc, poly, (byte)(val >> 56));

            return crc;
        }

        private static uint Crc32(uint crc, uint poly, byte val)
        {
            crc ^= val;

            for (int bit = 7; bit >= 0; bit--)
            {
                uint mask = (uint)(-(int)(crc & 1));

                crc = (crc >> 1) ^ (poly & mask);
            }

            return crc;
        }
    }
}
