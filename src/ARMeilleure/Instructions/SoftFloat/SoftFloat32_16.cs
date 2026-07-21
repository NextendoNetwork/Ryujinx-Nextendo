using ARMeilleure.State;
using System;
using System.Runtime.InteropServices;

namespace ARMeilleure.Instructions
{
    static class SoftFloat32_16
    {
        [UnmanagedCallersOnly]
        public static ushort FPConvert(float value)
        {
            ExecutionContext context = NativeInterface.GetContext();

            double real = value.FPUnpackCv(out FPType type, out bool sign, out uint valueBits, context);

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
            this float value,
            out FPType type,
            out bool sign,
            out uint valueBits,
            ExecutionContext context)
        {
            valueBits = (uint)BitConverter.SingleToInt32Bits(value);

            sign = (~valueBits & 0x80000000u) == 0u;

            uint exp32 = (valueBits & 0x7F800000u) >> 23;
            uint frac32 = valueBits & 0x007FFFFFu;

            double real;

            if (exp32 == 0u)
            {
                if (frac32 == 0u || (context.Fpcr & FPCR.Fz) != 0)
                {
                    type = FPType.Zero;
                    real = 0d;

                    if (frac32 != 0u)
                    {
                        SoftFloat.FPProcessException(FPException.InputDenorm, context);
                    }
                }
                else
                {
                    type = FPType.Nonzero; // Subnormal.
                    real = Math.Pow(2d, -126) * ((double)frac32 * Math.Pow(2d, -23));
                }
            }
            else if (exp32 == 0xFFu)
            {
                if (frac32 == 0u)
                {
                    type = FPType.Infinity;
                    real = Math.Pow(2d, 1000);
                }
                else
                {
                    type = (~frac32 & 0x00400000u) == 0u ? FPType.QNaN : FPType.SNaN;
                    real = 0d;
                }
            }
            else
            {
                type = FPType.Nonzero; // Normal.
                real = Math.Pow(2d, (int)exp32 - 127) * (1d + (double)frac32 * Math.Pow(2d, -23));
            }

            return sign ? -real : real;
        }

        private static ushort FPConvertNaN(uint valueBits)
        {
            return (ushort)((valueBits & 0x80000000u) >> 16 | 0x7E00u | (valueBits & 0x003FE000u) >> 13);
        }
    }
}
