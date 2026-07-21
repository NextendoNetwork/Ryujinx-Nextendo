namespace Ryujinx.Cpu.AppleHv.Arm
{
    enum ExceptionLevel : uint
    {
        PstateMask = 0xfffffff0,
        EL1h = 0b0101,
        El1t = 0b0100,
        EL0 = 0b0000,
    }
}
