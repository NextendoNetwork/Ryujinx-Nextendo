using System;

namespace Ryujinx.Cpu.LightningJit.CodeGen
{
    enum OperandType
    {
        None,
        I32,
        I64,
        FP32,
        FP64,
        V128,
    }

    static class OperandTypeExtensions
    {
        extension(OperandType type)
        {
            public bool IsInteger => type is OperandType.I32 or OperandType.I64;

            public int ByteSize => type switch
            {
                OperandType.FP32 => 4,
                OperandType.FP64 => 8,
                OperandType.I32 => 4,
                OperandType.I64 => 8,
                OperandType.V128 => 16,
                _ => throw new InvalidOperationException($"Invalid operand type \"{type}\"."),
            };
        }
    }
}
