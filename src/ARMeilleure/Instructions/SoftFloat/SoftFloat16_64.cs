using ARMeilleure.State;
using System;
using System.Runtime.InteropServices;

namespace ARMeilleure.Instructions
{
    static class SoftFloat16_64
    {
        [UnmanagedCallersOnly]
        public static double FPConvert(ushort valueBits)
        {
            ExecutionContext context = NativeInterface.GetContext();

            double real = valueBits.FPUnpackCv(out FPType type, out bool sign, context);

            double result;

            if (type is FPType.SNaN or FPType.QNaN)
            {
                if ((context.Fpcr & FPCR.Dn) != 0)
                {
                    result = SoftFloat64.FPDefaultNaN();
                }
                else
                {
                    result = FPConvertNaN(valueBits);
                }

                if (type == FPType.SNaN)
                {
                    SoftFloat.FPProcessException(FPException.InvalidOp, context);
                }
            }
            else if (type == FPType.Infinity)
            {
                result = SoftFloat64.FPInfinity(sign);
            }
            else if (type == FPType.Zero)
            {
                result = SoftFloat64.FPZero(sign);
            }
            else
            {
                result = FPRoundCv(real, context);
            }

            return result;
        }

        private static double FPRoundCv(double real, ExecutionContext context)
        {
            const int MinimumExp = -1022;

            const int E = 11;
            const int F = 52;

            bool sign;
            double mantissa;

            if (real < 0d)
            {
                sign = true;
                mantissa = -real;
            }
            else
            {
                sign = false;
                mantissa = real;
            }

            int exponent = 0;

            while (mantissa < 1d)
            {
                mantissa *= 2d;
                exponent--;
            }

            while (mantissa >= 2d)
            {
                mantissa /= 2d;
                exponent++;
            }

            if ((context.Fpcr & FPCR.Fz) != 0 && exponent < MinimumExp)
            {
                context.Fpsr |= FPSR.Ufc;

                return SoftFloat64.FPZero(sign);
            }

            uint biasedExp = (uint)Math.Max(exponent - MinimumExp + 1, 0);

            if (biasedExp == 0u)
            {
                mantissa /= Math.Pow(2d, MinimumExp - exponent);
            }

            ulong intMant = (ulong)Math.Floor(mantissa * Math.Pow(2d, F));
            double error = mantissa * Math.Pow(2d, F) - (double)intMant;

            if (biasedExp == 0u && (error != 0d || (context.Fpcr & FPCR.Ufe) != 0))
            {
                SoftFloat.FPProcessException(FPException.Underflow, context);
            }

            bool overflowToInf;
            bool roundUp;

            switch (context.Fpcr.RoundingMode)
            {
                case FPRoundingMode.ToNearest:
                    roundUp = (error > 0.5d || (error == 0.5d && (intMant & 1u) == 1u));
                    overflowToInf = true;
                    break;

                case FPRoundingMode.TowardsPlusInfinity:
                    roundUp = (error != 0d && !sign);
                    overflowToInf = !sign;
                    break;

                case FPRoundingMode.TowardsMinusInfinity:
                    roundUp = (error != 0d && sign);
                    overflowToInf = sign;
                    break;

                case FPRoundingMode.TowardsZero:
                    roundUp = false;
                    overflowToInf = false;
                    break;

                default:
                    throw new ArgumentException($"Invalid rounding mode \"{context.Fpcr.RoundingMode}\".");
            }

            if (roundUp)
            {
                intMant++;

                if (intMant == 1ul << F)
                {
                    biasedExp = 1u;
                }

                if (intMant == 1ul << (F + 1))
                {
                    biasedExp++;
                    intMant >>= 1;
                }
            }

            double result;

            if (biasedExp >= (1u << E) - 1u)
            {
                result = overflowToInf ? SoftFloat64.FPInfinity(sign) : SoftFloat64.FPMaxNormal(sign);

                SoftFloat.FPProcessException(FPException.Overflow, context);

                error = 1d;
            }
            else
            {
                result = BitConverter.Int64BitsToDouble(
                    (long)((sign ? 1ul : 0ul) << 63 | (biasedExp & 0x7FFul) << 52 | (intMant & 0x000FFFFFFFFFFFFFul)));
            }

            if (error != 0d)
            {
                SoftFloat.FPProcessException(FPException.Inexact, context);
            }

            return result;
        }

        private static double FPConvertNaN(ushort valueBits)
        {
            return BitConverter.Int64BitsToDouble(
                (long)(((ulong)valueBits & 0x8000ul) << 48 | 0x7FF8000000000000ul | ((ulong)valueBits & 0x01FFul) << 42));
        }
    }
}
