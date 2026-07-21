using ARMeilleure.State;
using System;
using System.Runtime.InteropServices;

namespace ARMeilleure.Instructions
{
    static class SoftFloat64
    {
        [UnmanagedCallersOnly]
        public static double FPAdd(double value1, double value2)
        {
            return FPAddFpscrImpl(value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static double FPAddFpscr(double value1, double value2, byte standardFpscr)
        {
            return FPAddFpscrImpl(value1, value2, standardFpscr == 1);
        }

        private static double FPAddFpscrImpl(double value1, double value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

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

                    if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0d);
                    }
                }
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static int FPCompare(double value1, double value2, byte signalNaNs)
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
        public static double FPCompareEQ(double value1, double value2)
        {
            return FPCompareEQFpscrImpl(value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static double FPCompareEQFpscr(double value1, double value2, byte standardFpscr)
        {
            return FPCompareEQFpscrImpl(value1, value2, standardFpscr == 1);
        }

        private static double FPCompareEQFpscrImpl(double value1, double value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out _, out _, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out _, out _, context, fpcr);

            double result;

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
        public static double FPCompareGE(double value1, double value2)
        {
            return FPCompareGEFpscrImpl(value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static double FPCompareGEFpscr(double value1, double value2, byte standardFpscr)
        {
            return FPCompareGEFpscrImpl(value1, value2, standardFpscr == 1);
        }

        private static double FPCompareGEFpscrImpl(double value1, double value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out _, out _, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out _, out _, context, fpcr);

            double result;

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
        public static double FPCompareGT(double value1, double value2)
        {
            return FPCompareGTFpscrImpl(value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static double FPCompareGTFpscr(double value1, double value2, byte standardFpscr)
        {
            return FPCompareGTFpscrImpl(value1, value2, standardFpscr == 1);
        }

        private static double FPCompareGTFpscrImpl(double value1, double value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out _, out _, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out _, out _, context, fpcr);

            double result;

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
        public static double FPCompareLE(double value1, double value2)
        {
            return FPCompareGEFpscrImpl(value2, value1, false);
        }

        [UnmanagedCallersOnly]
        public static double FPCompareLT(double value1, double value2)
        {
            return FPCompareGTFpscrImpl(value2, value1, false);
        }

        [UnmanagedCallersOnly]
        public static double FPCompareLEFpscr(double value1, double value2, byte standardFpscr)
        {
            return FPCompareGEFpscrImpl(value2, value1, standardFpscr == 1);
        }

        [UnmanagedCallersOnly]
        public static double FPCompareLTFpscr(double value1, double value2, byte standardFpscr)
        {
            return FPCompareGTFpscrImpl(value2, value1, standardFpscr == 1);
        }

        [UnmanagedCallersOnly]
        public static double FPDiv(double value1, double value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

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

                    if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0d);
                    }
                }
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static double FPMax(double value1, double value2)
        {
            return FPMaxFpscrImpl(value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static double FPMaxFpscr(double value1, double value2, byte standardFpscr)
        {
            return FPMaxFpscrImpl(value1, value2, standardFpscr == 1);
        }

        private static double FPMaxFpscrImpl(double value1, double value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

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

                        if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                        {
                            context.Fpsr |= FPSR.Ufc;

                            result = FPZero(result < 0d);
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

                        if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                        {
                            context.Fpsr |= FPSR.Ufc;

                            result = FPZero(result < 0d);
                        }
                    }
                }
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static double FPMaxNum(double value1, double value2)
        {
            return FPMaxNumFpscrImpl(value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static double FPMaxNumFpscr(double value1, double value2, byte standardFpscr)
        {
            return FPMaxNumFpscrImpl(value1, value2, standardFpscr == 1);
        }

        private static double FPMaxNumFpscrImpl(double value1, double value2, bool standardFpscr)
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
        public static double FPMin(double value1, double value2)
        {
            return FPMinFpscrImpl(value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static double FPMinFpscr(double value1, double value2, byte standardFpscr)
        {
            return FPMinFpscrImpl(value1, value2, standardFpscr == 1);
        }

        private static double FPMinFpscrImpl(double value1, double value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

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

                        if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                        {
                            context.Fpsr |= FPSR.Ufc;

                            result = FPZero(result < 0d);
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

                        if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                        {
                            context.Fpsr |= FPSR.Ufc;

                            result = FPZero(result < 0d);
                        }
                    }
                }
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static double FPMinNum(double value1, double value2)
        {
            return FPMinNumFpscrImpl(value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static double FPMinNumFpscr(double value1, double value2, byte standardFpscr)
        {
            return FPMinNumFpscrImpl(value1, value2, standardFpscr == 1);
        }

        private static double FPMinNumFpscrImpl(double value1, double value2, bool standardFpscr)
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
        public static double FPMul(double value1, double value2)
        {
            return FPMulFpscrImpl(value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static double FPMulFpscr(double value1, double value2, byte standardFpscr)
        {
            return FPMulFpscrImpl(value1, value2, standardFpscr == 1);
        }

        private static double FPMulFpscrImpl(double value1, double value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

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

                    if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0d);
                    }
                }
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static double FPMulAdd(double valueA, double value1, double value2)
        {
            return FPMulAddFpscrImpl(valueA, value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static double FPMulAddFpscr(double valueA, double value1, double value2, byte standardFpscr)
        {
            return FPMulAddFpscrImpl(valueA, value1, value2, standardFpscr == 1);
        }

        private static double FPMulAddFpscrImpl(double valueA, double value1, double value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            valueA = valueA.FPUnpack(out FPType typeA, out bool signA, out ulong addend, context, fpcr);
            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2, context, fpcr);

            bool inf1 = type1 == FPType.Infinity;
            bool zero1 = type1 == FPType.Zero;
            bool inf2 = type2 == FPType.Infinity;
            bool zero2 = type2 == FPType.Zero;

            double result = FPProcessNaNs3(typeA, type1, type2, addend, op1, op2, out bool done, context, fpcr);

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
                    result = Math.FusedMultiplyAdd(value1, value2, valueA);

                    if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0d);
                    }
                }
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static double FPMulSub(double valueA, double value1, double value2)
        {
            value1 = value1.FPNeg();

            return FPMulAddFpscrImpl(valueA, value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static double FPMulSubFpscr(double valueA, double value1, double value2, byte standardFpscr)
        {
            value1 = value1.FPNeg();

            return FPMulAddFpscrImpl(valueA, value1, value2, standardFpscr == 1);
        }

        [UnmanagedCallersOnly]
        public static double FPMulX(double value1, double value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

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

                    if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0d);
                    }
                }
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static double FPNegMulAdd(double valueA, double value1, double value2)
        {
            valueA = valueA.FPNeg();
            value1 = value1.FPNeg();

            return FPMulAddFpscrImpl(valueA, value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static double FPNegMulSub(double valueA, double value1, double value2)
        {
            valueA = valueA.FPNeg();

            return FPMulAddFpscrImpl(valueA, value1, value2, false);
        }

        [UnmanagedCallersOnly]
        public static double FPRecipEstimate(double value)
        {
            return FPRecipEstimateFpscrImpl(value, false);
        }

        [UnmanagedCallersOnly]
        public static double FPRecipEstimateFpscr(double value, byte standardFpscr)
        {
            return FPRecipEstimateFpscrImpl(value, standardFpscr == 1);
        }

        private static double FPRecipEstimateFpscrImpl(double value, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value.FPUnpack(out FPType type, out bool sign, out ulong op, context, fpcr);

            double result;

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
            else if (Math.Abs(value) < Math.Pow(2d, -1024))
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
            else if ((fpcr & FPCR.Fz) != 0 && (Math.Abs(value) >= Math.Pow(2d, 1022)))
            {
                result = FPZero(sign);

                context.Fpsr |= FPSR.Ufc;
            }
            else
            {
                ulong fraction = op & 0x000FFFFFFFFFFFFFul;
                uint exp = (uint)((op & 0x7FF0000000000000ul) >> 52);

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

                uint resultExp = 2045u - exp;

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

                result = BitConverter.Int64BitsToDouble(
                    (long)((sign ? 1ul : 0ul) << 63 | (resultExp & 0x7FFul) << 52 | (fraction & 0x000FFFFFFFFFFFFFul)));
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static double FPRecipStep(double value1, double value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.StandardFpcrValue;

            value1 = value1.FPUnpack(out FPType type1, out _, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out _, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity;
                bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity;
                bool zero2 = type2 == FPType.Zero;

                double product;

                if ((inf1 && zero2) || (zero1 && inf2))
                {
                    product = FPZero(false);
                }
                else
                {
                    product = FPMulFpscrImpl(value1, value2, true);
                }

                result = FPSubFpscr(FPTwo(false), product, true);
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static double FPRecipStepFused(double value1, double value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value1 = value1.FPNeg();

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

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
                    result = Math.FusedMultiplyAdd(value1, value2, 2d);

                    if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0d);
                    }
                }
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static double FPRecpX(double value)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value.FPUnpack(out FPType type, out bool sign, out ulong op, context, fpcr);

            double result;

            if (type is FPType.SNaN or FPType.QNaN)
            {
                result = FPProcessNaN(type, op, context, fpcr);
            }
            else
            {
                ulong notExp = (~op >> 52) & 0x7FFul;
                ulong maxExp = 0x7FEul;

                result = BitConverter.Int64BitsToDouble(
                    (long)((sign ? 1ul : 0ul) << 63 | (notExp == 0x7FFul ? maxExp : notExp) << 52));
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static double FPRSqrtEstimate(double value)
        {
            return FPRSqrtEstimateFpscrImpl(value, false);
        }

        [UnmanagedCallersOnly]
        public static double FPRSqrtEstimateFpscr(double value, byte standardFpscr)
        {
            return FPRSqrtEstimateFpscrImpl(value, standardFpscr == 1);
        }

        private static double FPRSqrtEstimateFpscrImpl(double value, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value.FPUnpack(out FPType type, out bool sign, out ulong op, context, fpcr);

            double result;

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
                ulong fraction = op & 0x000FFFFFFFFFFFFFul;
                uint exp = (uint)((op & 0x7FF0000000000000ul) >> 52);

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

                uint resultExp = (3068u - exp) >> 1;

                uint estimate = (uint)SoftFloat.RecipSqrtEstimateTable[scaled - 128u] + 256u;

                result = BitConverter.Int64BitsToDouble((long)((resultExp & 0x7FFul) << 52 | (estimate & 0xFFul) << 44));
            }

            return result;
        }

        public static double FPHalvedSub(double value1, double value2, ExecutionContext context, FPCR fpcr)
        {
            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

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
                    result = (value1 - value2) / 2.0;

                    if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0d);
                    }
                }
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static double FPRSqrtStep(double value1, double value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.StandardFpcrValue;

            value1 = value1.FPUnpack(out FPType type1, out _, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out _, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

            if (!done)
            {
                bool inf1 = type1 == FPType.Infinity;
                bool zero1 = type1 == FPType.Zero;
                bool inf2 = type2 == FPType.Infinity;
                bool zero2 = type2 == FPType.Zero;

                double product;

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
        public static double FPRSqrtStepFused(double value1, double value2)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value1 = value1.FPNeg();

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

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
                    result = Math.FusedMultiplyAdd(value1, value2, 3d) / 2d;

                    if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0d);
                    }
                }
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static double FPSqrt(double value)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = context.Fpcr;

            value = value.FPUnpack(out FPType type, out bool sign, out ulong op, context, fpcr);

            double result;

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
                result = Math.Sqrt(value);

                if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                {
                    context.Fpsr |= FPSR.Ufc;

                    result = FPZero(result < 0d);
                }
            }

            return result;
        }

        [UnmanagedCallersOnly]
        public static double FPSub(double value1, double value2)
        {
            return FPSubFpscr(value1, value2, false);
        }

        public static double FPSubFpscr(double value1, double value2, bool standardFpscr)
        {
            ExecutionContext context = NativeInterface.GetContext();
            FPCR fpcr = standardFpscr ? context.StandardFpcrValue : context.Fpcr;

            value1 = value1.FPUnpack(out FPType type1, out bool sign1, out ulong op1, context, fpcr);
            value2 = value2.FPUnpack(out FPType type2, out bool sign2, out ulong op2, context, fpcr);

            double result = FPProcessNaNs(type1, type2, op1, op2, out bool done, context, fpcr);

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

                    if ((fpcr & FPCR.Fz) != 0 && double.IsSubnormal(result))
                    {
                        context.Fpsr |= FPSR.Ufc;

                        result = FPZero(result < 0d);
                    }
                }
            }

            return result;
        }

        public static double FPDefaultNaN()
        {
            return BitConverter.Int64BitsToDouble(0x7ff8000000000000);
        }

        public static double FPInfinity(bool sign)
        {
            return sign ? double.NegativeInfinity : double.PositiveInfinity;
        }

        public static double FPZero(bool sign)
        {
            return sign ? -0d : +0d;
        }

        public static double FPMaxNormal(bool sign)
        {
            return sign ? double.MinValue : double.MaxValue;
        }

        private static double FPTwo(bool sign)
        {
            return sign ? -2d : +2d;
        }

        private static double FPThree(bool sign)
        {
            return sign ? -3d : +3d;
        }

        private static double FPOnePointFive(bool sign)
        {
            return sign ? -1.5d : +1.5d;
        }

        private static double FPNeg(this double value)
        {
            return -value;
        }

        private static double ZerosOrOnes(bool ones)
        {
            return BitConverter.Int64BitsToDouble(ones ? -1L : 0L);
        }

        private static double FPUnpack(
            this double value,
            out FPType type,
            out bool sign,
            out ulong valueBits,
            ExecutionContext context,
            FPCR fpcr)
        {
            valueBits = (ulong)BitConverter.DoubleToInt64Bits(value);

            sign = (~valueBits & 0x8000000000000000ul) == 0ul;

            if ((valueBits & 0x7FF0000000000000ul) == 0ul)
            {
                if ((valueBits & 0x000FFFFFFFFFFFFFul) == 0ul || (fpcr & FPCR.Fz) != 0)
                {
                    type = FPType.Zero;
                    value = FPZero(sign);

                    if ((valueBits & 0x000FFFFFFFFFFFFFul) != 0ul)
                    {
                        SoftFloat.FPProcessException(FPException.InputDenorm, context, fpcr);
                    }
                }
                else
                {
                    type = FPType.Nonzero;
                }
            }
            else if ((~valueBits & 0x7FF0000000000000ul) == 0ul)
            {
                if ((valueBits & 0x000FFFFFFFFFFFFFul) == 0ul)
                {
                    type = FPType.Infinity;
                }
                else
                {
                    type = (~valueBits & 0x0008000000000000ul) == 0ul ? FPType.QNaN : FPType.SNaN;
                    value = FPZero(sign);
                }
            }
            else
            {
                type = FPType.Nonzero;
            }

            return value;
        }

        private static double FPProcessNaNs(
            FPType type1,
            FPType type2,
            ulong op1,
            ulong op2,
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

        private static double FPProcessNaNs3(
            FPType type1,
            FPType type2,
            FPType type3,
            ulong op1,
            ulong op2,
            ulong op3,
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

        private static double FPProcessNaN(FPType type, ulong op, ExecutionContext context, FPCR fpcr)
        {
            if (type == FPType.SNaN)
            {
                op |= 1ul << 51;

                SoftFloat.FPProcessException(FPException.InvalidOp, context, fpcr);
            }

            if ((fpcr & FPCR.Dn) != 0)
            {
                return FPDefaultNaN();
            }

            return BitConverter.Int64BitsToDouble((long)op);
        }
    }
}
