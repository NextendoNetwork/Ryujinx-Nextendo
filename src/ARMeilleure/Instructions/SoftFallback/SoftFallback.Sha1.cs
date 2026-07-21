using ARMeilleure.State;
using System.Runtime.InteropServices;

namespace ARMeilleure.Instructions
{
    static partial class SoftFallback
    {
        [UnmanagedCallersOnly]
        public static V128 HashChoose(V128 hash_abcd, uint hash_e, V128 wk)
        {
            for (int e = 0; e <= 3; e++)
            {
                uint t = ShaChoose(hash_abcd.Extract<uint>(1),
                                   hash_abcd.Extract<uint>(2),
                                   hash_abcd.Extract<uint>(3));

                hash_e += Rol(hash_abcd.Extract<uint>(0), 5) + t + wk.Extract<uint>(e);

                t = Rol(hash_abcd.Extract<uint>(1), 30);

                hash_abcd.Insert(1, t);

                Rol32_160(ref hash_e, ref hash_abcd);
            }

            return hash_abcd;
        }

        [UnmanagedCallersOnly]
        public static uint FixedRotate(uint hash_e)
        {
            return hash_e.Rol(30);
        }

        [UnmanagedCallersOnly]
        public static V128 HashMajority(V128 hash_abcd, uint hash_e, V128 wk)
        {
            for (int e = 0; e <= 3; e++)
            {
                uint t = ShaMajority(hash_abcd.Extract<uint>(1),
                                     hash_abcd.Extract<uint>(2),
                                     hash_abcd.Extract<uint>(3));

                hash_e += Rol(hash_abcd.Extract<uint>(0), 5) + t + wk.Extract<uint>(e);

                t = Rol(hash_abcd.Extract<uint>(1), 30);

                hash_abcd.Insert(1, t);

                Rol32_160(ref hash_e, ref hash_abcd);
            }

            return hash_abcd;
        }

        [UnmanagedCallersOnly]
        public static V128 HashParity(V128 hash_abcd, uint hash_e, V128 wk)
        {
            for (int e = 0; e <= 3; e++)
            {
                uint t = ShaParity(hash_abcd.Extract<uint>(1),
                                   hash_abcd.Extract<uint>(2),
                                   hash_abcd.Extract<uint>(3));

                hash_e += Rol(hash_abcd.Extract<uint>(0), 5) + t + wk.Extract<uint>(e);

                t = Rol(hash_abcd.Extract<uint>(1), 30);

                hash_abcd.Insert(1, t);

                Rol32_160(ref hash_e, ref hash_abcd);
            }

            return hash_abcd;
        }

        [UnmanagedCallersOnly]
        public static V128 Sha1SchedulePart1(V128 w0_3, V128 w4_7, V128 w8_11)
        {
            ulong t2 = w4_7.Extract<ulong>(0);
            ulong t1 = w0_3.Extract<ulong>(1);

            V128 result = new(t1, t2);

            return result ^ (w0_3 ^ w8_11);
        }

        [UnmanagedCallersOnly]
        public static V128 Sha1SchedulePart2(V128 tw0_3, V128 w12_15)
        {
            V128 t = tw0_3 ^ (w12_15 >> 32);

            uint tE0 = t.Extract<uint>(0);
            uint tE1 = t.Extract<uint>(1);
            uint tE2 = t.Extract<uint>(2);
            uint tE3 = t.Extract<uint>(3);

            return new V128(tE0.Rol(1), tE1.Rol(1), tE2.Rol(1), tE3.Rol(1) ^ tE0.Rol(2));
        }

        private static void Rol32_160(ref uint y, ref V128 x)
        {
            uint xE3 = x.Extract<uint>(3);

            x <<= 32;
            x.Insert(0, y);

            y = xE3;
        }

        private static uint ShaChoose(uint x, uint y, uint z)
        {
            return ((y ^ z) & x) ^ z;
        }

        private static uint ShaMajority(uint x, uint y, uint z)
        {
            return (x & y) | ((x | y) & z);
        }

        private static uint ShaParity(uint x, uint y, uint z)
        {
            return x ^ y ^ z;
        }

        private static uint Rol(this uint value, int count)
        {
            return (value << count) | (value >> (32 - count));
        }
    }
}
