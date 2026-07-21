using ARMeilleure.State;
using System;
using System.Runtime.InteropServices;

namespace ARMeilleure.Instructions
{
    static class SoftFloat64_16
    {
        [UnmanagedCallersOnly]
        public static ushort FPConvert(double value)
        {
            ExecutionContext context = NativeInterface.GetContext();

            double real = value.FPUnpackCv(out FPType type, out bool sign, out ulong valueBits, context);

            bool altHp = (context.Fpcr & FPCR.Ahp) != 0;

            ushort resultBits;

            if (type is FPType.SNaN or FPType.QNaN)
            {
                if (altHp)
                {
                    resultBits = SoftFloat16.FPZero(sign);
                }
                else if ((context.Fpcr & FPCR.Dn) != 0)
                {
                    resultBits = SoftFloat16.FPDefaultNaN();
                }
                else
                {
                    resultBits = FPConvertNaN(valueBits);
                }

                if (type == FPType.SNaN || altHp)
                {
                    SoftFloat.FPProcessException(FPException.InvalidOp, context);
                }
            }
            else if (type == FPType.Infinity)
            {
                if (altHp)
                {
                    resultBits = (ushort)((sign ? 1u : 0u) << 15 | 0x7FFFu);

                    SoftFloat.FPProcessException(FPException.InvalidOp, context);
                }
                else
                {
                    resultBits = SoftFloat16.FPInfinity(sign);
                }
            }
            else if (type == FPType.Zero)
            {
                resultBits = SoftFloat16.FPZero(sign);
            }
            else
            {
                resultBits = SoftFloat16.FPRoundCv(real, context);
            }

            return resultBits;
        }

        private static double FPUnpackCv(
            this double value,
            out FPType type,
            out bool sign,
            out ulong valueBits,
            ExecutionContext context)
        {
            valueBits = (ulong)BitConverter.DoubleToInt64Bits(value);

            sign = (~valueBits & 0x8000000000000000ul) == 0u;

            ulong exp64 = (valueBits & 0x7FF0000000000000ul) >> 52;
            ulong frac64 = valueBits & 0x000FFFFFFFFFFFFFul;

            double real;

            if (exp64 == 0u)
            {
                if (frac64 == 0u || (context.Fpcr & FPCR.Fz) != 0)
                {
                    type = FPType.Zero;
                    real = 0d;

                    if (frac64 != 0u)
                    {
                        SoftFloat.FPProcessException(FPException.InputDenorm, context);
                    }
                }
                else
                {
                    type = FPType.Nonzero; // Subnormal.
                    real = Math.Pow(2d, -1022) * ((double)frac64 * Math.Pow(2d, -52));
                }
            }
            else if (exp64 == 0x7FFul)
            {
                if (frac64 == 0u)
                {
                    type = FPType.Infinity;
                    real = Math.Pow(2d, 1000000);
                }
                else
                {
                    type = (~frac64 & 0x0008000000000000ul) == 0u ? FPType.QNaN : FPType.SNaN;
                    real = 0d;
                }
            }
            else
            {
                type = FPType.Nonzero; // Normal.
                real = Math.Pow(2d, (int)exp64 - 1023) * (1d + (double)frac64 * Math.Pow(2d, -52));
            }

            return sign ? -real : real;
        }

        private static ushort FPConvertNaN(ulong valueBits)
        {
            return (ushort)((valueBits & 0x8000000000000000ul) >> 48 | 0x7E00u |
                            (valueBits & 0x0007FC0000000000ul) >> 42);
        }
    }
}
