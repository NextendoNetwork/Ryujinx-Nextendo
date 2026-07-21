using ARMeilleure.State;
using System;

namespace ARMeilleure.Instructions
{
    static class SoftFloat16
    {
        public static ushort FPDefaultNaN()
        {
            return (ushort)0x7E00u;
        }

        public static ushort FPInfinity(bool sign)
        {
            return sign ? (ushort)0xFC00u : (ushort)0x7C00u;
        }

        public static ushort FPZero(bool sign)
        {
            return sign ? (ushort)0x8000u : (ushort)0x0000u;
        }

        public static ushort FPMaxNormal(bool sign)
        {
            return sign ? (ushort)0xFBFFu : (ushort)0x7BFFu;
        }

        public static double FPUnpackCv(
            this ushort valueBits,
            out FPType type,
            out bool sign,
            ExecutionContext context)
        {
            sign = (~(uint)valueBits & 0x8000u) == 0u;

            uint exp16 = ((uint)valueBits & 0x7C00u) >> 10;
            uint frac16 = (uint)valueBits & 0x03FFu;

            double real;

            if (exp16 == 0u)
            {
                if (frac16 == 0u)
                {
                    type = FPType.Zero;
                    real = 0d;
                }
                else
                {
                    type = FPType.Nonzero; // Subnormal.
                    real = Math.Pow(2d, -14) * ((double)frac16 * Math.Pow(2d, -10));
                }
            }
            else if (exp16 == 0x1Fu && (context.Fpcr & FPCR.Ahp) == 0)
            {
                if (frac16 == 0u)
                {
                    type = FPType.Infinity;
                    real = Math.Pow(2d, 1000);
                }
                else
                {
                    type = (~frac16 & 0x0200u) == 0u ? FPType.QNaN : FPType.SNaN;
                    real = 0d;
                }
            }
            else
            {
                type = FPType.Nonzero; // Normal.
                real = Math.Pow(2d, (int)exp16 - 15) * (1d + (double)frac16 * Math.Pow(2d, -10));
            }

            return sign ? -real : real;
        }

        public static ushort FPRoundCv(double real, ExecutionContext context)
        {
            const int MinimumExp = -14;

            const int E = 5;
            const int F = 10;

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

            uint biasedExp = (uint)Math.Max(exponent - MinimumExp + 1, 0);

            if (biasedExp == 0u)
            {
                mantissa /= Math.Pow(2d, MinimumExp - exponent);
            }

            uint intMant = (uint)Math.Floor(mantissa * Math.Pow(2d, F));
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

                if (intMant == 1u << F)
                {
                    biasedExp = 1u;
                }

                if (intMant == 1u << (F + 1))
                {
                    biasedExp++;
                    intMant >>= 1;
                }
            }

            ushort resultBits;

            if ((context.Fpcr & FPCR.Ahp) == 0)
            {
                if (biasedExp >= (1u << E) - 1u)
                {
                    resultBits = overflowToInf ? FPInfinity(sign) : FPMaxNormal(sign);

                    SoftFloat.FPProcessException(FPException.Overflow, context);

                    error = 1d;
                }
                else
                {
                    resultBits = (ushort)((sign ? 1u : 0u) << 15 | (biasedExp & 0x1Fu) << 10 | (intMant & 0x03FFu));
                }
            }
            else
            {
                if (biasedExp >= 1u << E)
                {
                    resultBits = (ushort)((sign ? 1u : 0u) << 15 | 0x7FFFu);

                    SoftFloat.FPProcessException(FPException.InvalidOp, context);

                    error = 0d;
                }
                else
                {
                    resultBits = (ushort)((sign ? 1u : 0u) << 15 | (biasedExp & 0x1Fu) << 10 | (intMant & 0x03FFu));
                }
            }

            if (error != 0d)
            {
                SoftFloat.FPProcessException(FPException.Inexact, context);
            }

            return resultBits;
        }
    }
}
