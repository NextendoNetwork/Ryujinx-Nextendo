using ARMeilleure.State;
using System;
using System.Runtime.InteropServices;

namespace ARMeilleure.Instructions
{
static class SoftFloat32
    {
        [UnmanagedCallersOnly]
        public static float FPAdd(float value1, float value2)
        {
            return FPAddFpscrImpl(value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static float FPAddFpscr(float value1, float value2, byte standardFpscr)
        {
            return FPAddFpscrImpl(value1, value2, standardFpscr == 1);
        }

        private static float FPAddFpscrImpl(float value1, float value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity;
                bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity;
                bool zero2 = type2 == FPType.Zero;

                if (inf1 && inf2 && sign1 == !sign2)
                {
                    result = FPDefaultNaN();

                    SoftFloat.FPProcessException(FPException.InvalidOp, context, fpcr);
                }
                else if ((inf1 && !sign1) || (inf2 && !sign2))
                {
                    result = FPInfinity(false);
                }
                else if ((inf1 && sign1) || (inf2 && sign2))
                {
                    result = FPInfinity(true);
                }
                else if (zero1 && zero2 && sign1 == sign2)
                {
                    result = FPZero(sign1);
                }
                else
                {
                    result = value1 + value2;

                    if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0f);
                    }
                }
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static int FPCompare(float value1, float value2, byte signalNaNs)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out _, out _, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out _, out _, context, fpcr);

            int result;

            if (type1 == FPType.SNaN || type1 == FPType.QNaN || type2 == FPType.SNaN || type2 == FPType.QNaN)
            {
                result = 0b0011;

                if (type1 == FPType.SNaN || type2 == FPType.SNaN || signalNaNs == 1)
                {
                    SoftFloat.FPProcessException(FPException.InvalidOp, context, fpcr);
                }
            }
            else
            {
                if (value1 == value2)
                {
                    result = 0b0110;
                }
                else if (value1 < value2)
                {
                    result = 0b1000;
                }
                else
                {
                    result = 0b0010;
                }
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static float FPCompareEQ(float value1, float value2)
        {
            return FPCompareEQFpscrImpl(value1, value2, false);
        }

        private static float FPCompareEQFpscrImpl(float value1, float value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out _, out _, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out _, out _, context, fpcr);

            float result;

            if (type1 == FPType.SNaN || type1 == FPType.QNaN || type2 == FPType.SNaN || type2 == FPType.QNaN)
            {
                result = ZerosOrOnes(false);

                if (type1 == FPType.SNaN || type2 == FPType.SNaN)
                {
                    SoftFloat.FPProcessException(FPException.InvalidOp, context, fpcr);
                }
            }
            else
            {
                result = ZerosOrOnes(value1 == value2);
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static float FPCompareEQFpscr(float value1, float value2, byte standardFpscr)
        {
            return FPCompareEQFpscrImpl(value1, value2, standardFpscr == 1);
        }

        [UnmanagedCallersOnly]
        public static float FPCompareGE(float value1, float value2)
        {
            return FPCompareGEFpscrImpl(value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static float FPCompareGEFpscr(float value1, float value2, byte standardFpscr)
        {
            return FPCompareGEFpscrImpl(value1, value2, standardFpscr == 1);
        }

        private static float FPCompareGEFpscrImpl(float value1, float value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out _, out _, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out _, out _, context, fpcr);

            float result;

            if (type1 == FPType.SNaN || type1 == FPType.QNaN || type2 == FPType.SNaN || type2 == FPType.QNaN)
            {
                result = ZerosOrOnes(false);

                SoftFloat.FPProcessException(FPException.InvalidOp, context, fpcr);
            }
            else
            {
                result = ZerosOrOnes(value1 >= value2);
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static float FPCompareGT(float value1, float value2)
        {
            return FPCompareGTFpscrImpl(value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static float FPCompareGTFpscr(float value1, float value2, byte standardFpscr)
        {
            return FPCompareGTFpscrImpl(value1, value2, standardFpscr == 1);
        }

        private static float FPCompareGTFpscrImpl(float value1, float value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out _, out _, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out _, out _, context, fpcr);

            float result;

            if (type1 == FPType.SNaN || type1 == FPType.QNaN || type2 == FPType.SNaN || type2 == FPType.QNaN)
            {
                result = ZerosOrOnes(false);

                SoftFloat.FPProcessException(FPException.InvalidOp, context, fpcr);
            }
            else
            {
                result = ZerosOrOnes(value1 > value2);
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static float FPCompareLE(float value1, float value2)
        {
            return FPCompareGEFpscrImpl(value2, value1, false);
        }

        [UnmanagedCallersOnly]
        public static float FPCompareLT(float value1, float value2)
        {
            return FPCompareGTFpscrImpl(value2, value1, false);
        }

        [UnmanagedCallersOnly]
        public static float FPCompareLEFpscr(float value1, float value2, byte standardFpscr)
        {
            return FPCompareGEFpscrImpl(value2, value1, standardFpscr == 1);
        }

        [UnmanagedCallersOnly]
        public static float FPCompareLTFpscr(float value1, float value2, byte standardFpscr)
        {
            return FPCompareGEFpscrImpl(value2, value1, standardFpscr == 1);
        }

        [UnmanagedCallersOnly]
        public static float FPDiv(float value1, float value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity;
                bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity;
                bool zero2 = type2 == FPType.Zero;

                if ((inf1 && inf2) || (zero1 && zero2))
                {
                    result = FPDefaultNaN();

                    SoftFloat.FPProcessException(FPException.InvalidOp, context, fpcr);
                }
                else if (inf1 || zero2)
                {
                    result = FPInfinity(sign1 ^ sign2);

                    if (!inf1)
                    {
                        SoftFloat.FPProcessException(FPException.DivideByZero, context, fpcr);
                    }
                }
                else if (zero1 || inf2)
                {
                    result = FPZero(sign1 ^ sign2);
                }
                else
                {
                    result = value1 / value2;

                    if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0f);
                    }
                }
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static float FPMax(float value1, float value2)
        {
            return FPMaxFpscrImpl(value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static float FPMaxFpscr(float value1, float value2, byte standardFpscr)
        {
            return FPMaxFpscrImpl(value1, value2, standardFpscr == 1);
        }

        private static float FPMaxFpscrImpl(float value1, float value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                if (value1 > value2)
                {
                    if (type1 == FPType.Infinity)
                    {
                        result = FPInfinity(sign1);
                    }
                    else if (type1 == FPType.Zero)
                    {
                        result = FPZero(sign1 && sign2);
                    }
                    else
                    {
                        result = value1;

                        if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                        {
                            context.Fpsr |= FPSR.Ufc;

                            result = FPZero(result < 0f);
                        }
                    }
                }
                else
                {
                    if (type2 == FPType.Infinity)
                    {
                        result = FPInfinity(sign2);
                    }
                    else if (type2 == FPType.Zero)
                    {
                        result = FPZero(sign1 && sign2);
                    }
                    else
                    {
                        result = value2;

                        if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                        {
                            context.Fpsr |= FPSR.Ufc;

                            result = FPZero(result < 0f);
                        }
                    }
                }
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static float FPMaxNum(float value1, float value2)
        {
            return FPMaxNumFpscrImpl(value1, value2, false);
        }

        private static float FPMaxNumFpscrImpl(float value1, float value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1.FPUnpack(out FPType type1, out _, out _, context, fpcr);
            value2.FPUnpack(out FPType type2, out _, out _, context, fpcr);

            if (type1 == FPType.QNaN && type2 != FPType.QNaN)
            {
                value1 = FPInfinity(true);
            }
            else if (type1 != FPType.QNaN && type2 == FPType.QNaN)
            {
                value2 = FPInfinity(true);
            }

            return FPMaxFpscrImpl(value1, value2, standardFpscr);
        }

        [UnmanagedCallersOnly]
        public static float FPMaxNumFpscr(float value1, float value2, byte standardFpscr)
        {
            return FPMaxNumFpscrImpl(value1, value2, standardFpscr == 1);
        }

        [UnmanagedCallersOnly]
        public static float FPMin(float value1, float value2)
        {
            return FPMinFpscrImpl(value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static float FPMinFpscr(float value1, float value2, byte standardFpscr)
        {
            return FPMinFpscrImpl(value1, value2, standardFpscr == 1);
        }

        private static float FPMinFpscrImpl(float value1, float value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                if (value1 < value2)
                {
                    if (type1 == FPType.Infinity)
                    {
                        result = FPInfinity(sign1);
                    }
                    else if (type1 == FPType.Zero)
                    {
                        result = FPZero(sign1 || sign2);
                    }
                    else
                    {
                        result = value1;

                        if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                        {
                            context.Fpsr |= FPSR.Ufc;

                            result = FPZero(result < 0f);
                        }
                    }
                }
                else
                {
                    if (type2 == FPType.Infinity)
                    {
                        result = FPInfinity(sign2);
                    }
                    else if (type2 == FPType.Zero)
                    {
                        result = FPZero(sign1 || sign2);
                    }
                    else
                    {
                        result = value2;

                        if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                        {
                            context.Fpsr |= FPSR.Ufc;

                            result = FPZero(result < 0f);
                        }
                    }
                }
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static float FPMinNum(float value1, float value2)
        {
            return FPMinNumFpscrImpl(value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static float FPMinNumFpscr(float value1, float value2, byte standardFpscr)
        {
            return FPMinNumFpscrImpl(value1, value2, standardFpscr == 1);
        }

        private static float FPMinNumFpscrImpl(float value1, float value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1.FPUnpack(out FPType type1, out _, out _, context, fpcr);
            value2.FPUnpack(out FPType type2, out _, out _, context, fpcr);

            if (type1 == FPType.QNaN && type2 != FPType.QNaN)
            {
                value1 = FPInfinity(false);
            }
            else if (type1 != FPType.QNaN && type2 == FPType.QNaN)
            {
                value2 = FPInfinity(false);
            }

            return FPMinFpscrImpl(value1, value2, standardFpscr);
        }

        [UnmanagedCallersOnly]
        public static float FPMul(float value1, float value2)
        {
            return FPMulFpscrImpl(value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static float FPMulFpscr(float value1, float value2, byte standardFpscr)
        {
            return FPMulFpscrImpl(value1, value2, standardFpscr == 1);
        }

        private static float FPMulFpscrImpl(float value1, float value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity;
                bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity;
                bool zero2 = type2 == FPType.Zero;

                if ((inf1 && zero2) || (zero1 && inf2))
                {
                    result = FPDefaultNaN();

                    SoftFloat.FPProcessException(FPException.InvalidOp, context, fpcr);
                }
                else if (inf1 || inf2)
                {
                    result = FPInfinity(sign1 ^ sign2);
                }
                else if (zero1 || zero2)
                {
                    result = FPZero(sign1 ^ sign2);
                }
                else
                {
                    result = value1 * value2;

                    if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0f);
                    }
                }
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static float FPMulAdd(float valueA, float value1, float value2)
        {
            return FPMulAddFpscrImpl(valueA, value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static float FPMulAddFpscr(float valueA, float value1, float value2, byte standardFpscr)
        {
            return FPMulAddFpscrImpl(valueA, value1, value2, standardFpscr == 1);
        }

        private static float FPMulAddFpscrImpl(float valueA, float value1, float value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            valueA = valueA.FPUnpack(out FPType typeA, out bool signA, out uint addend, context, fpcr);
            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2, context, fpcr);

            bool inf1 = type1 == FPType.Infinity;
            bool zero1 = type1 == FPType.Zero;
            bool inf2 = type2 == FPType.Infinity;
            bool zero2 = type2 == FPType.Zero;

            float result = FPProcessNaNs3(typeA, type1, type2, addend, op1, op2, out bool done, context, fpcr);

            if (typeA == FPType.QNaN && ((inf1 && zero2) || (zero1 && inf2)))
            {
                result = FPDefaultNaN();

                SoftFloat.FPProcessException(FPException.InvalidOp, context, fpcr);
            }

            if (!done)
            {
                bool infA = typeA == FPType.Infinity;
                bool zeroA = typeA == FPType.Zero;

                bool signP = sign1 ^ sign2;
                bool infP = inf1 || inf2;
                bool zeroP = zero1 || zero2;

                if ((inf1 && zero2) || (zero1 && inf2) || (infA && infP && signA != signP))
                {
                    result = FPDefaultNaN();

                    SoftFloat.FPProcessException(FPException.InvalidOp, context, fpcr);
                }
                else if ((infA && !signA) || (infP && !signP))
                {
                    result = FPInfinity(false);
                }
                else if ((infA && signA) || (infP && signP))
                {
                    result = FPInfinity(true);
                }
                else if (zeroA && zeroP && signA == signP)
                {
                    result = FPZero(signA);
                }
                else
                {
                    result = MathF.FusedMultiplyAdd(value1, value2, valueA);

                    if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0f);
                    }
                }
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static float FPMulSub(float valueA, float value1, float value2)
        {
            value1 = value1.FPNeg();

            return FPMulAddFpscrImpl(valueA, value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static float FPMulSubFpscr(float valueA, float value1, float value2, byte standardFpscr)
        {
            value1 = value1.FPNeg();

            return FPMulAddFpscrImpl(valueA, value1, value2, standardFpscr == 1);
        }

        [UnmanagedCallersOnly]
        public static float FPMulX(float value1, float value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity;
                bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity;
                bool zero2 = type2 == FPType.Zero;

                if ((inf1 && zero2) || (zero1 && inf2))
                {
                    result = FPTwo(sign1 ^ sign2);
                }
                else if (inf1 || inf2)
                {
                    result = FPInfinity(sign1 ^ sign2);
                }
                else if (zero1 || zero2)
                {
                    result = FPZero(sign1 ^ sign2);
                }
                else
                {
                    result = value1 * value2;

                    if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0f);
                    }
                }
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static float FPNegMulAdd(float valueA, float value1, float value2)
        {
            valueA = valueA.FPNeg();
            value1 = value1.FPNeg();

            return FPMulAddFpscrImpl(valueA, value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static float FPNegMulSub(float valueA, float value1, float value2)
        {
            valueA = valueA.FPNeg();

            return FPMulAddFpscrImpl(valueA, value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static float FPRecipEstimate(float value)
        {
            return FPRecipEstimateFpscrImpl(value, false);
        }

        [UnmanagedCallersOnly]
        public static float FPRecipEstimateFpscr(float value, byte standardFpscr)
        {
            return FPRecipEstimateFpscrImpl(value, standardFpscr == 1);
        }

        private static float FPRecipEstimateFpscrImpl(float value, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value.FPUnpack(out FPType type, out bool sign, out uint op, context, fpcr);

            float result;

            if (type is FPType.SNaN or FPType.QNaN)
            {
                result = FPProcessNaN(type, op, context, fpcr);
            }
            else if (type == FPType.Infinity)
            {
                result = FPZero(sign);
            }
            else if (type == FPType.Zero)
            {
                result = FPInfinity(sign);

                SoftFloat.FPProcessException(FPException.DivideByZero, context, fpcr);
            }
            else if (MathF.Abs(value) < MathF.Pow(2f, -128))
            {
                bool overflowToInf = fpcr.RoundingMode switch
                {
                    FPRoundingMode.ToNearest => true,
                    FPRoundingMode.TowardsPlusInfinity => !sign,
                    FPRoundingMode.TowardsMinusInfinity => sign,
                    FPRoundingMode.TowardsZero => false,
                    _ => throw new ArgumentException($"Invalid rounding mode \"{fpcr.RoundingMode}\"."),
                };
                result = overflowToInf ? FPInfinity(sign) : FPMaxNormal(sign);

                SoftFloat.FPProcessException(FPException.Overflow, context, fpcr);
                SoftFloat.FPProcessException(FPException.Inexact, context, fpcr);
            }
            else if ((fpcr & FPCR.Fz) != 0 && (MathF.Abs(value) >= MathF.Pow(2f, 126)))
            {
                result = FPZero(sign);

                context.Fpsr |= FPSR.Ufc;
            }
            else
            {
                ulong fraction = (ulong)(op & 0x007FFFFFu) << 29;
                uint exp = (op & 0x7F800000u) >> 23;

                if (exp == 0u)
                {
                    if ((fraction & 0x0008000000000000ul) == 0ul)
                    {
                        fraction = (fraction & 0x0003FFFFFFFFFFFFul) << 2;
                        exp -= 1u;
                    }
                    else
                    {
                        fraction = (fraction & 0x0007FFFFFFFFFFFFul) << 1;
                    }
                }

                uint scaled = (uint)(((fraction & 0x000FF00000000000ul) | 0x0010000000000000ul) >> 44);

                uint resultExp = 253u - exp;

                uint estimate = (uint)SoftFloat.RecipEstimateTable[scaled - 256u] + 256u;

                fraction = (ulong)(estimate & 0xFFu) << 44;

                if (resultExp == 0u)
                {
                    fraction = ((fraction & 0x000FFFFFFFFFFFFEul) | 0x0010000000000000ul) >> 1;
                }
                else if (resultExp + 1u == 0u)
                {
                    fraction = ((fraction & 0x000FFFFFFFFFFFFCul) | 0x0010000000000000ul) >> 2;
                    resultExp = 0u;
                }

                result = BitConverter.Int32BitsToSingle(
                    (int)((sign ? 1u : 0u) << 31 | (resultExp & 0xFFu) << 23 | (uint)(fraction >> 29) & 0x007FFFFFu));
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static float FPRecipStep(float value1, float value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.StandardFpcrValue;

            value1 = value1.FPUnpack(out FPType type1, out _, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out _, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity;
                bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity;
                bool zero2 = type2 == FPType.Zero;

                float product;

                if ((inf1 && zero2) || (zero1 && inf2))
                {
                    product = FPZero(false);
                }
                else
                {
                    product = FPMulFpscrImpl(value1, value2, true);
                }

                result = FPSubFpscrImpl(FPTwo(false), product, true);
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static float FPRecipStepFused(float value1, float value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value1 = value1.FPNeg();

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity;
                bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity;
                bool zero2 = type2 == FPType.Zero;

                if ((inf1 && zero2) || (zero1 && inf2))
                {
                    result = FPTwo(false);
                }
                else if (inf1 || inf2)
                {
                    result = FPInfinity(sign1 ^ sign2);
                }
                else
                {
                    result = MathF.FusedMultiplyAdd(value1, value2, 2f);

                    if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0f);
                    }
                }
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static float FPRecpX(float value)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value.FPUnpack(out FPType type, out bool sign, out uint op, context, fpcr);

            float result;

            if (type is FPType.SNaN or FPType.QNaN)
            {
                result = FPProcessNaN(type, op, context, fpcr);
            }
            else
            {
                uint notExp = (~op >> 23) & 0xFFu;
                uint maxExp = 0xFEu;

                result = BitConverter.Int32BitsToSingle(
                    (int)((sign ? 1u : 0u) << 31 | (notExp == 0xFFu ? maxExp : notExp) << 23));
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static float FPRSqrtEstimate(float value)
        {
            return FPRSqrtEstimateFpscrImpl(value, false);
        }

        [UnmanagedCallersOnly]
        public static float FPRSqrtEstimateFpscr(float value, byte standardFpscr)
        {
            return FPRSqrtEstimateFpscrImpl(value, standardFpscr == 1);
        }

        private static float FPRSqrtEstimateFpscrImpl(float value, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value.FPUnpack(out FPType type, out bool sign, out uint op, context, fpcr);

            float result;

            if (type is FPType.SNaN or FPType.QNaN)
            {
                result = FPProcessNaN(type, op, context, fpcr);
            }
            else if (type == FPType.Zero)
            {
                result = FPInfinity(sign);

                SoftFloat.FPProcessException(FPException.DivideByZero, context, fpcr);
            }
            else if (sign)
            {
                result = FPDefaultNaN();

                SoftFloat.FPProcessException(FPException.InvalidOp, context, fpcr);
            }
            else if (type == FPType.Infinity)
            {
                result = FPZero(false);
            }
            else
            {
                ulong fraction = (ulong)(op & 0x007FFFFFu) << 29;
                uint exp = (op & 0x7F800000u) >> 23;

                if (exp == 0u)
                {
                    while ((fraction & 0x0008000000000000ul) == 0ul)
                    {
                        fraction = (fraction & 0x0007FFFFFFFFFFFFul) << 1;
                        exp -= 1u;
                    }

                    fraction = (fraction & 0x0007FFFFFFFFFFFFul) << 1;
                }

                uint scaled;

                if ((exp & 1u) == 0u)
                {
                    scaled = (uint)(((fraction & 0x000FF00000000000ul) | 0x0010000000000000ul) >> 44);
                }
                else
                {
                    scaled = (uint)(((fraction & 0x000FE00000000000ul) | 0x0010000000000000ul) >> 45);
                }

                uint resultExp = (380u - exp) >> 1;

                uint estimate = (uint)SoftFloat.RecipSqrtEstimateTable[scaled - 128u] + 256u;

                result = BitConverter.Int32BitsToSingle((int)((resultExp & 0xFFu) << 23 | (estimate & 0xFFu) << 15));
            }

            return result;
        }

        public static float FPHalvedSub(float value1, float value2, ExecutionContext context, FPCR fpcr)
        {
            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity;
                bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity;
                bool zero2 = type2 == FPType.Zero;

                if (inf1 && inf2 && sign1 == sign2)
                {
                    result = FPDefaultNaN();

                    SoftFloat.FPProcessException(FPException.InvalidOp, context, fpcr);
                }
                else if ((inf1 && !sign1) || (inf2 && sign2))
                {
                    result = FPInfinity(false);
                }
                else if ((inf1 && sign1) || (inf2 && !sign2))
                {
                    result = FPInfinity(true);
                }
                else if (zero1 && zero2 && sign1 == !sign2)
                {
                    result = FPZero(sign1);
                }
                else
                {
                    result = (value1 - value2) / 2.0f;

                    if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0f);
                    }
                }
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static float FPRSqrtStep(float value1, float value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.StandardFpcrValue;

            value1 = value1.FPUnpack(out FPType type1, out _, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out _, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity;
                bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity;
                bool zero2 = type2 == FPType.Zero;

                float product;

                if ((inf1 && zero2) || (zero1 && inf2))
                {
                    product = FPZero(false);
                }
                else
                {
                    product = FPMulFpscrImpl(value1, value2, true);
                }

                result = FPHalvedSub(FPThree(false), product, context, fpcr);
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static float FPRSqrtStepFused(float value1, float value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value1 = value1.FPNeg();

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity;
                bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity;
                bool zero2 = type2 == FPType.Zero;

                if ((inf1 && zero2) || (zero1 && inf2))
                {
                    result = FPOnePointFive(false);
                }
                else if (inf1 || inf2)
                {
                    result = FPInfinity(sign1 ^ sign2);
                }
                else
                {
                    result = MathF.FusedMultiplyAdd(value1, value2, 3f) / 2f;

                    if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0f);
                    }
                }
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static float FPSqrt(float value)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value = value.FPUnpack(out FPType type, out bool sign, out uint op, context, fpcr);

            float result;

            if (type is FPType.SNaN or FPType.QNaN)
            {
                result = FPProcessNaN(type, op, context, fpcr);
            }
            else if (type == FPType.Zero)
            {
                result = FPZero(sign);
            }
            else if (type == FPType.Infinity && !sign)
            {
                result = FPInfinity(sign);
            }
            else if (sign)
            {
                result = FPDefaultNaN();

                SoftFloat.FPProcessException(FPException.InvalidOp, context, fpcr);
            }
            else
            {
                result = MathF.Sqrt(value);

                if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                {
                    context.Fpsr |= FPSR.Ufc;

                    result = FPZero(result < 0f);
                }
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static float FPSub(float value1, float value2)
        {
            return FPSubFpscrImpl(value1, value2, false);
        }

        private static float FPSubFpscrImpl(float value1, float value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out uint op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out uint op2, context, fpcr);

            float result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity;
                bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity;
                bool zero2 = type2 == FPType.Zero;

                if (inf1 && inf2 && sign1 == sign2)
                {
                    result = FPDefaultNaN();

                    SoftFloat.FPProcessException(FPException.InvalidOp, context, fpcr);
                }
                else if ((inf1 && !sign1) || (inf2 && sign2))
                {
                    result = FPInfinity(false);
                }
                else if ((inf1 && sign1) || (inf2 && !sign2))
                {
                    result = FPInfinity(true);
                }
                else if (zero1 && zero2 && sign1 == !sign2)
                {
                    result = FPZero(sign1);
                }
                else
                {
                    result = value1 - value2;

                    if ((fpcr & FPCR.Fz) != 0 && float.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0f);
                    }
                }
            }

            return result;
        }

        public static float FPDefaultNaN()
        {
            return BitConverter.Int32BitsToSingle(0x7fc00000);
        }

        public static float FPInfinity(bool sign)
        {
            return sign ? float.NegativeInfinity : float.PositiveInfinity;
        }

        public static float FPZero(bool sign)
        {
            return sign ? -0f : +0f;
        }

        public static float FPMaxNormal(bool sign)
        {
            return sign ? float.MinValue : float.MaxValue;
        }

        private static float FPTwo(bool sign)
        {
            return sign ? -2f : +2f;
        }

        private static float FPThree(bool sign)
        {
            return sign ? -3f : +3f;
        }

        private static float FPOnePointFive(bool sign)
        {
            return sign ? -1.5f : +1.5f;
        }

        private static float FPNeg(this float value)
        {
            return -value;
        }

        private static float ZerosOrOnes(bool ones)
        {
            return BitConverter.Int32BitsToSingle(ones ? -1 : 0);
        }

        private static float FPUnpack(
            this float value,
            out FPType type,
            out bool sign,
            out uint valueBits,
            ExecutionContext context,
            FPCR fpcr)
        {
            valueBits = (uint)BitConverter.SingleToInt32Bits(value);

            sign = (~valueBits & 0x80000000u) == 0u;

            if ((valueBits & 0x7F800000u) == 0u)
            {
                if ((valueBits & 0x007FFFFFu) == 0u || (fpcr & FPCR.Fz) != 0)
                {
                    type = FPType.Zero;
                    value = FPZero(sign);

                    if ((valueBits & 0x007FFFFFu) != 0u)
                    {
                        SoftFloat.FPProcessException(FPException.InputDenorm, context, fpcr);
                    }
                }
                else
                {
                    type = FPType.Nonzero;
                }
            }
            else if ((~valueBits & 0x7F800000u) == 0u)
            {
                if ((valueBits & 0x007FFFFFu) == 0u)
                {
                    type = FPType.Infinity;
                }
                else
                {
                    type = (~valueBits & 0x00400000u) == 0u ? FPType.QNaN : FPType.SNaN;
                    value = FPZero(sign);
                }
            }
            else
            {
                type = FPType.Nonzero;
            }

            return value;
        }

        private static float FPProcessNaNs(
            FPType type1,
            FPType type2,
            uint op1,
            uint op2,
            out bool done,
            ExecutionContext context,
            FPCR fpcr)
        {
            done = true;

            if (type1 == FPType.SNaN)
            {
                return FPProcessNaN(type1, op1, context, fpcr);
            }
            else if (type2 == FPType.SNaN)
            {
                return FPProcessNaN(type2, op2, context, fpcr);
            }
            else if (type1 == FPType.QNaN)
            {
                return FPProcessNaN(type1, op1, context, fpcr);
            }
            else if (type2 == FPType.QNaN)
            {
                return FPProcessNaN(type2, op2, context, fpcr);
            }

            done = false;

            return FPZero(false);
        }

        private static float FPProcessNaNs3(
            FPType type1,
            FPType type2,
            FPType type3,
            uint op1,
            uint op2,
            uint op3,
            out bool done,
            ExecutionContext context,
            FPCR fpcr)
        {
            done = true;

            if (type1 == FPType.SNaN)
            {
                return FPProcessNaN(type1, op1, context, fpcr);
            }
            else if (type2 == FPType.SNaN)
            {
                return FPProcessNaN(type2, op2, context, fpcr);
            }
            else if (type3 == FPType.SNaN)
            {
                return FPProcessNaN(type3, op3, context, fpcr);
            }
            else if (type1 == FPType.QNaN)
            {
                return FPProcessNaN(type1, op1, context, fpcr);
            }
            else if (type2 == FPType.QNaN)
            {
                return FPProcessNaN(type2, op2, context, fpcr);
            }
            else if (type3 == FPType.QNaN)
            {
                return FPProcessNaN(type3, op3, context, fpcr);
            }

            done = false;

            return FPZero(false);
        }

        private static float FPProcessNaN(FPType type, uint op, ExecutionContext context, FPCR fpcr)
        {
            if (type == FPType.SNaN)
            {
                op |= 1u << 22;

                SoftFloat.FPProcessException(FPException.InvalidOp, context, fpcr);
            }

            if ((fpcr & FPCR.Dn) != 0)
            {
                return FPDefaultNaN();
            }

            return BitConverter.Int32BitsToSingle((int)op);
        }
    }
}
