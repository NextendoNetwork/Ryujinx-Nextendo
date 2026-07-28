using System.Numerics;

namespace Ryujinx.Input
{
    /// <summary>
    /// [Nextendo] Shared state for mouse-panning ("mouse-look"): the GUI mouse driver accumulates
    /// relative mouse movement here while capturing, and <see cref="HLE.NpadController"/> consumes it
    /// once per frame to deflect the right stick. This lets a mouse aim like the right stick — useful
    /// for shooters and Splatoon.
    ///
    /// Lives in Ryujinx.Input (not the GUI project) so NpadController can read it without an upward
    /// reference: the GUI mouse driver writes, NpadController reads.
    /// </summary>
    public static class MousePanning
    {
        /// <summary>Mouse movement (client pixels) per frame → right-stick units, at sensitivity 1.0.</summary>
        public const float BaseScale = 0.0125f;

        /// <summary>Feature enabled (from config). Set by the host when a game starts, cleared on stop.</summary>
        public static volatile bool Enabled;

        /// <summary>True while the mouse is being captured for panning (game focused, not released).</summary>
        public static volatile bool Capturing;

        /// <summary>Sensitivity multiplier (from config). Set by the host.</summary>
        public static float Sensitivity = 1f;

        /// <summary>Invert the horizontal aim axis (from config).</summary>
        public static volatile bool InvertX;

        /// <summary>Invert the vertical aim axis (from config).</summary>
        public static volatile bool InvertY;

        private static readonly object _lock = new();
        private static float _accumX;
        private static float _accumY;

        /// <summary>Add relative mouse movement (client pixels). Called from the UI thread.</summary>
        public static void Accumulate(float dx, float dy)
        {
            lock (_lock)
            {
                _accumX += dx;
                _accumY += dy;
            }
        }

        /// <summary>Read and clear the movement accumulated since the last call (one game frame).</summary>
        public static Vector2 ConsumeDelta()
        {
            lock (_lock)
            {
                Vector2 delta = new(_accumX, _accumY);
                _accumX = 0f;
                _accumY = 0f;

                return delta;
            }
        }

        /// <summary>Drop any pending movement and stop capturing (e.g. game closed, focus lost).</summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _accumX = 0f;
                _accumY = 0f;
            }

            Capturing = false;
        }
    }
}
