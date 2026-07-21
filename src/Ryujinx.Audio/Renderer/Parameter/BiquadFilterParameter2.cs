using Ryujinx.Common.Memory;
using System.Runtime.InteropServices;

namespace Ryujinx.Audio.Renderer.Parameter
{
    /// <summary>
    /// Biquad filter parameters.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 0x18, Pack = 1)]
    public struct BiquadFilterParameter2
    {
        /// <summary>
        /// Set to true if the biquad filter is active.
        /// </summary>
        [MarshalAs(UnmanagedType.I1)]
        public bool Enable;

        /// <summary>
        /// Reserved/padding.
        /// </summary>
        private readonly byte _reserved1;
        private readonly byte _reserved2;
        private readonly byte _reserved3;

        /// <summary>
        /// Biquad filter numerator (b0, b1, b2).
        /// </summary>
        public Array3<float> Numerator;

        /// <summary>
        /// Biquad filter denominator (a1, a2).
        /// </summary>
        /// <remarks>a0 = 1</remarks>
        public Array2<float> Denominator;
    }
}
