using System.Runtime.CompilerServices;
using ConfigPhysicalKey = Ryujinx.Common.Configuration.Hid.PhysicalKey;

namespace Ryujinx.Input
{
    /// <summary>
    /// A snapshot of a <see cref="IKeyboard"/>.
    /// </summary>
    public class KeyboardStateSnapshot
    {
        public readonly bool[] KeysState;

        /// <summary>
        /// Create a new <see cref="KeyboardStateSnapshot"/>.
        /// </summary>
        /// <param name="keysState">The keys state</param>
        public KeyboardStateSnapshot(bool[] keysState)
        {
            KeysState = keysState;
        }

        /// <summary>
        /// Check if a given key is pressed.
        /// </summary>
        /// <param name="key">The key</param>
        /// <returns>True if the given key is pressed</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsPressed(Key key) => KeysState[(int)key];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsPressed(ConfigPhysicalKey key) => KeysState[(int)key];
    }
}
