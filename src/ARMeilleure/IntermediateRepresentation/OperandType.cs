using System;

namespace ARMeilleure.IntermediateRepresentation
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
            
            public RegisterType Register => type switch
            {
                OperandType.FP32 => RegisterType.Vector,
                OperandType.FP64 => RegisterType.Vector,
                OperandType.I32 => RegisterType.Integer,
                OperandType.I64 => RegisterType.Integer,
                OperandType.V128 => RegisterType.Vector,
                _ => throw new InvalidOperationException($"Invalid operand type \"{type}\".")
            };
            
            public int ByteSize => type switch
            {
                OperandType.FP32 => 4,
                OperandType.FP64 => 8,
                OperandType.I32 => 4,
                OperandType.I64 => 8,
                OperandType.V128 => 16,
                _ => throw new InvalidOperationException($"Invalid operand type \"{type}\".")
            };
            
            public int ByteSizeLog2 => type switch
            {
                OperandType.FP32 => 2,
                OperandType.FP64 => 3,
                OperandType.I32 => 2,
                OperandType.I64 => 3,
                OperandType.V128 => 4,
                _ => throw new InvalidOperationException($"Invalid operand type \"{type}\".")
            };
        }
    }
}
