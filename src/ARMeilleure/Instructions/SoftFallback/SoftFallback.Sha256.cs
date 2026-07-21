using ARMeilleure.State;
using System.Runtime.InteropServices;

namespace ARMeilleure.Instructions
{
    static partial class SoftFallback
    {
        [UnmanagedCallersOnly]
        public static V128 HashLower(V128 hash_abcd, V128 hash_efgh, V128 wk)
        {
            return Sha256Hash(hash_abcd, hash_efgh, wk, part1: true);
        }

        [UnmanagedCallersOnly]
        public static V128 HashUpper(V128 hash_abcd, V128 hash_efgh, V128 wk)
        {
            return Sha256Hash(hash_abcd, hash_efgh, wk, part1: false);
        }

        [UnmanagedCallersOnly]
        public static V128 Sha256SchedulePart1(V128 w0_3, V128 w4_7)
        {
            V128 result = new();

            for (int e = 0; e <= 3; e++)
            {
                uint elt = (e <= 2 ? w0_3 : w4_7).Extract<uint>(e <= 2 ? e + 1 : 0);

                elt = elt.Ror(7) ^ elt.Ror(18) ^ elt.Lsr(3);

                elt += w0_3.Extract<uint>(e);

                result.Insert(e, elt);
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static V128 Sha256SchedulePart2(V128 w0_3, V128 w8_11, V128 w12_15)
        {
            V128 result = new();

            ulong t1 = w12_15.Extract<ulong>(1);

            for (int e = 0; e <= 1; e++)
            {
                uint elt = t1.ULongPart(e);

                elt = elt.Ror(17) ^ elt.Ror(19) ^ elt.Lsr(10);

                elt += w0_3.Extract<uint>(e) + w8_11.Extract<uint>(e + 1);

                result.Insert(e, elt);
            }

            t1 = result.Extract<ulong>(0);

            for (int e = 2; e <= 3; e++)
            {
                uint elt = t1.ULongPart(e - 2);

                elt = elt.Ror(17) ^ elt.Ror(19) ^ elt.Lsr(10);

                elt += w0_3.Extract<uint>(e) + (e == 2 ? w8_11 : w12_15).Extract<uint>(e == 2 ? 3 : 0);

                result.Insert(e, elt);
            }

            return result;
        }

        private static V128 Sha256Hash(V128 x, V128 y, V128 w, bool part1)
        {
            for (int e = 0; e <= 3; e++)
            {
                uint chs = ShaChoose(y.Extract<uint>(0),
                                     y.Extract<uint>(1),
                                     y.Extract<uint>(2));

                uint maj = ShaMajority(x.Extract<uint>(0),
                                       x.Extract<uint>(1),
                                       x.Extract<uint>(2));

                uint t1 = y.Extract<uint>(3) + ShaHashSigma1(y.Extract<uint>(0)) + chs + w.Extract<uint>(e);

                uint t2 = t1 + x.Extract<uint>(3);

                x.Insert(3, t2);

                t2 = t1 + ShaHashSigma0(x.Extract<uint>(0)) + maj;

                y.Insert(3, t2);

                Rol32_256(ref y, ref x);
            }

            return part1 ? x : y;
        }

        private static void Rol32_256(ref V128 y, ref V128 x)
        {
            uint yE3 = y.Extract<uint>(3);
            uint xE3 = x.Extract<uint>(3);

            y <<= 32;
            x <<= 32;

            y.Insert(0, xE3);
            x.Insert(0, yE3);
        }

        private static uint ShaHashSigma0(uint x)
        {
            return x.Ror(2) ^ x.Ror(13) ^ x.Ror(22);
        }

        private static uint ShaHashSigma1(uint x)
        {
            return x.Ror(6) ^ x.Ror(11) ^ x.Ror(25);
        }

        private static uint Ror(this uint value, int count)
        {
            return (value >> count) | (value << (32 - count));
        }

        private static uint Lsr(this uint value, int count)
        {
            return value >> count;
        }

        private static uint ULongPart(this ulong value, int part)
        {
            return part == 0
                ? (uint)(value & 0xFFFFFFFFUL)
                : (uint)(value >> 32);
        }
    }
}
