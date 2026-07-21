using ARMeilleure.State;
using System.Runtime.InteropServices;

namespace ARMeilleure.Instructions
{
    static partial class SoftFallback
    {
        [UnmanagedCallersOnly]
        public static V128 PolynomialMult64_128(ulong op1, ulong op2)
        {
            V128 result = V128.Zero;

            V128 op2_128 = new(op2, 0);

            for (int i = 0; i < 64; i++)
            {
                if (((op1 >> i) & 1) == 1)
                {
                    result ^= op2_128 << i;
                }
            }

            return result;
        }
    }
}
