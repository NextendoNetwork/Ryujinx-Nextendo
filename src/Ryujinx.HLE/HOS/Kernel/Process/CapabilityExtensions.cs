using System.Numerics;

namespace Ryujinx.HLE.HOS.Kernel.Process
{
    static class CapabilityExtensions
    {
        extension(CapabilityType type)
        {
            public uint Flag => (uint)type + 1;

            public uint Id => (uint)BitOperations.TrailingZeroCount(type.Flag);
        }
        
        public static CapabilityType GetCapabilityType(this uint cap)
        {
            return (CapabilityType)(((cap + 1) & ~cap) - 1);
        }
    }
}
