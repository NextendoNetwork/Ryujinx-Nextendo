using ARMeilleure.Decoders;
using ARMeilleure.IntermediateRepresentation;
using ARMeilleure.Translation;

using static ARMeilleure.Instructions.InstEmitHelper;

namespace ARMeilleure.Instructions
{
    static partial class InstEmit32
    {
        #region "Sha1"
        // [Nextendo] SHA1 crypto-extension instructions in AArch32. ARMeilleure implemented these
        // only for A64 (SetA64 in OpCodeTable), so a 32-bit title using the hardware SHA1 path
        // (e.g. CTGP-DX's nn::crypto::Sha1Impl, an NX32 build) hit "undefined instruction" and
        // crashed. These mirror the A64 emitters (same SoftFallback core), reading A32 operands.
        public static void Sha1c_V(ArmEmitterContext context)
        {
            OpCode32SimdReg op = (OpCode32SimdReg)context.CurrOp;

            Operand d = GetVecA32(op.Qd);
            Operand ne = context.VectorExtract(OperandType.I32, GetVecA32(op.Qn), 0);
            Operand m = GetVecA32(op.Qm);

            Operand res = context.Call(typeof(SoftFallback).GetMethod(nameof(SoftFallback.HashChoose)), d, ne, m);

            context.Copy(GetVecA32(op.Qd), res);
        }

        public static void Sha1h_V(ArmEmitterContext context)
        {
            OpCode32Simd op = (OpCode32Simd)context.CurrOp;

            Operand ne = context.VectorExtract(OperandType.I32, GetVecA32(op.Qm), 0);

            Operand res = context.Call(typeof(SoftFallback).GetMethod(nameof(SoftFallback.FixedRotate)), ne);

            context.Copy(GetVecA32(op.Qd), context.VectorCreateScalar(res));
        }

        public static void Sha1m_V(ArmEmitterContext context)
        {
            OpCode32SimdReg op = (OpCode32SimdReg)context.CurrOp;

            Operand d = GetVecA32(op.Qd);
            Operand ne = context.VectorExtract(OperandType.I32, GetVecA32(op.Qn), 0);
            Operand m = GetVecA32(op.Qm);

            Operand res = context.Call(typeof(SoftFallback).GetMethod(nameof(SoftFallback.HashMajority)), d, ne, m);

            context.Copy(GetVecA32(op.Qd), res);
        }

        public static void Sha1p_V(ArmEmitterContext context)
        {
            OpCode32SimdReg op = (OpCode32SimdReg)context.CurrOp;

            Operand d = GetVecA32(op.Qd);
            Operand ne = context.VectorExtract(OperandType.I32, GetVecA32(op.Qn), 0);
            Operand m = GetVecA32(op.Qm);

            Operand res = context.Call(typeof(SoftFallback).GetMethod(nameof(SoftFallback.HashParity)), d, ne, m);

            context.Copy(GetVecA32(op.Qd), res);
        }

        public static void Sha1su0_V(ArmEmitterContext context)
        {
            OpCode32SimdReg op = (OpCode32SimdReg)context.CurrOp;

            Operand d = GetVecA32(op.Qd);
            Operand n = GetVecA32(op.Qn);
            Operand m = GetVecA32(op.Qm);

            Operand res = context.Call(typeof(SoftFallback).GetMethod(nameof(SoftFallback.Sha1SchedulePart1)), d, n, m);

            context.Copy(GetVecA32(op.Qd), res);
        }

        public static void Sha1su1_V(ArmEmitterContext context)
        {
            OpCode32Simd op = (OpCode32Simd)context.CurrOp;

            Operand d = GetVecA32(op.Qd);
            Operand m = GetVecA32(op.Qm);

            Operand res = context.Call(typeof(SoftFallback).GetMethod(nameof(SoftFallback.Sha1SchedulePart2)), d, m);

            context.Copy(GetVecA32(op.Qd), res);
        }
        #endregion

        #region "Sha256"
        public static void Sha256h_V(ArmEmitterContext context)
        {
            OpCode32SimdReg op = (OpCode32SimdReg)context.CurrOp;

            Operand d = GetVecA32(op.Qd);
            Operand n = GetVecA32(op.Qn);
            Operand m = GetVecA32(op.Qm);

            Operand res = InstEmitSimdHashHelper.EmitSha256h(context, d, n, m, part2: false);

            context.Copy(GetVecA32(op.Qd), res);
        }

        public static void Sha256h2_V(ArmEmitterContext context)
        {
            OpCode32SimdReg op = (OpCode32SimdReg)context.CurrOp;

            Operand d = GetVecA32(op.Qd);
            Operand n = GetVecA32(op.Qn);
            Operand m = GetVecA32(op.Qm);

            Operand res = InstEmitSimdHashHelper.EmitSha256h(context, n, d, m, part2: true);

            context.Copy(GetVecA32(op.Qd), res);
        }

        public static void Sha256su0_V(ArmEmitterContext context)
        {
            OpCode32Simd op = (OpCode32Simd)context.CurrOp;

            Operand d = GetVecA32(op.Qd);
            Operand m = GetVecA32(op.Qm);

            Operand res = InstEmitSimdHashHelper.EmitSha256su0(context, d, m);

            context.Copy(GetVecA32(op.Qd), res);
        }

        public static void Sha256su1_V(ArmEmitterContext context)
        {
            OpCode32SimdReg op = (OpCode32SimdReg)context.CurrOp;

            Operand d = GetVecA32(op.Qd);
            Operand n = GetVecA32(op.Qn);
            Operand m = GetVecA32(op.Qm);

            Operand res = InstEmitSimdHashHelper.EmitSha256su1(context, d, n, m);

            context.Copy(GetVecA32(op.Qd), res);
        }
        #endregion
    }
}
