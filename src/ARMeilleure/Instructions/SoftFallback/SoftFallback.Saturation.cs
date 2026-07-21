using System.Runtime.InteropServices;

namespace ARMeilleure.Instructions
{
    static partial class SoftFallback
    {
        [UnmanagedCallersOnly]
        public static int SatF32ToS32(float value)
        {
            if (float.IsNaN(value))
            {
                return 0;
            }

            return value >= int.MaxValue ? int.MaxValue :
                value <= int.MinValue ? int.MinValue : (int)value;
        }

        [UnmanagedCallersOnly]
        public static long SatF32ToS64(float value)
        {
            if (float.IsNaN(value))
            {
                return 0;
            }

            return value >= long.MaxValue ? long.MaxValue :
                value <= long.MinValue ? long.MinValue : (long)value;
        }

        [UnmanagedCallersOnly]
        public static uint SatF32ToU32(float value)
        {
            if (float.IsNaN(value))
            {
                return 0;
            }

            return value >= uint.MaxValue ? uint.MaxValue :
                value <= uint.MinValue ? uint.MinValue : (uint)value;
        }

        [UnmanagedCallersOnly]
        public static ulong SatF32ToU64(float value)
        {
            if (float.IsNaN(value))
            {
                return 0;
            }

            return value >= ulong.MaxValue ? ulong.MaxValue :
                value <= ulong.MinValue ? ulong.MinValue : (ulong)value;
        }

        [UnmanagedCallersOnly]
        public static int SatF64ToS32(double value)
        {
            if (double.IsNaN(value))
            {
                return 0;
            }

            return value >= int.MaxValue ? int.MaxValue :
                value <= int.MinValue ? int.MinValue : (int)value;
        }

        [UnmanagedCallersOnly]
        public static long SatF64ToS64(double value)
        {
            if (double.IsNaN(value))
            {
                return 0;
            }

            return value >= long.MaxValue ? long.MaxValue :
                value <= long.MinValue ? long.MinValue : (long)value;
        }

        [UnmanagedCallersOnly]
        public static uint SatF64ToU32(double value)
        {
            if (double.IsNaN(value))
            {
                return 0;
            }

            return value >= uint.MaxValue ? uint.MaxValue :
                value <= uint.MinValue ? uint.MinValue : (uint)value;
        }

        [UnmanagedCallersOnly]
        public static ulong SatF64ToU64(double value)
        {
            if (double.IsNaN(value))
            {
                return 0;
            }

            return value >= ulong.MaxValue ? ulong.MaxValue :
                value <= ulong.MinValue ? ulong.MinValue : (ulong)value;
        }
    }
}
