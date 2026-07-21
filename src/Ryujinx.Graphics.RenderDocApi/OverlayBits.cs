// ReSharper disable UnusedMember.Global

using System;

namespace Ryujinx.Graphics.RenderDocApi
{
    [Flags]
    public enum OverlayBits
    {
        /// <summary>
        /// This single bit controls whether the overlay is enabled or disabled globally
        /// </summary>
        Enabled = 1 << 0,
        /// <summary>
        /// Show the average framerate over several seconds as well as min/max
        /// </summary>
        FrameRate = 1 << 1,
        /// <summary>
        /// Show the current frame number
        /// </summary>
        FrameNumber = 1 << 2,
        /// <summary>
        /// Show a list of recent captures, and how many captures have been made
        /// </summary>
        CaptureList = 1 << 3,
        /// <summary>
        /// Default values for the overlay mask
        /// </summary>
        Default = Enabled | FrameRate | FrameNumber | CaptureList,
        /// <summary>
        /// Enable all bits
        /// </summary>
        All = ~0,
        /// <summary>
        /// Disable all bits
        /// </summary>
        None = 0
    }
}
