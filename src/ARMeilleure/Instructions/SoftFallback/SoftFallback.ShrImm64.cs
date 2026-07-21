using System.Runtime.InteropServices;

namespace ARMeilleure.Instructions
{
    static partial class SoftFallback
    {
        [UnmanagedCallersOnly]
        public static long SignedShrImm64(long value, long roundConst, int shift)
        {
            if (roundConst == 0L)
            {
                if (shift <= 63)
                {
                    return value >> shift;
                }
                else /* if (shift == 64) */
                {
                    if (value < 0L)
                    {
                        return -1L;
                    }
                    else /* if (value >= 0L) */
                    {
                        return 0L;
                    }
                }
            }
            else /* if (roundConst == 1L << (shift - 1)) */
            {
                if (shift <= 63)
                {
                    long add = value + roundConst;

                    if ((~value & (value ^ add)) < 0L)
                    {
                        return (long)((ulong)add >> shift);
                    }
                    else
                    {
                        return add >> shift;
                    }
                }
                else /* if (shift == 64) */
                {
                    return 0L;
                }
            }
        }

        [UnmanagedCallersOnly]
        public static ulong UnsignedShrImm64(ulong value, long roundConst, int shift)
        {
            if (roundConst == 0L)
            {
                if (shift <= 63)
                {
                    return value >> shift;
                }
                else /* if (shift == 64) */
                {
                    return 0UL;
                }
            }
            else /* if (roundConst == 1L << (shift - 1)) */
            {
                ulong add = value + (ulong)roundConst;

                if ((add < value) && (add < (ulong)roundConst))
                {
                    if (shift <= 63)
                    {
                        return (add >> shift) | (0x8000000000000000UL >> (shift - 1));
                    }
                    else /* if (shift == 64) */
                    {
                        return 1UL;
                    }
                }
                else
                {
                    if (shift <= 63)
                    {
                        return add >> shift;
                    }
                    else /* if (shift == 64) */
                    {
                        return 0UL;
                    }
                }
            }
        }
    }
}
