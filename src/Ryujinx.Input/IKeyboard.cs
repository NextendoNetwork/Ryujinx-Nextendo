using System.Buffers;
using System.Runtime.CompilerServices;
using ConfigPhysicalKey = Ryujinx.Common.Configuration.Hid.PhysicalKey;

namespace Ryujinx.Input
{
    /// <summary>
    /// Represent an emulated keyboard.
    /// </summary>
    public interface IKeyboard : IGamepad
    {
        private static bool[] _keyState;

        /// <summary>
        /// Check if a given key is pressed on the keyboard.
        /// </summary>
        /// <param name="key">The key</param>
        /// <returns>True if the given key is pressed on the keyboard</returns>
        bool IsPressed(Key key);

        /// <summary>
        /// Get a snaphost of the state of the keyboard.
        /// </summary>
        /// <returns>A snaphost of the state of the keyboard.</returns>
        KeyboardStateSnapshot GetKeyboardStateSnapshot();

        /// <summary>
        /// Get a snaphost of the state of a keyboard.
        /// </summary>
        /// <param name="keyboard">The keyboard to do a snapshot of</param>
        /// <returns>A snaphost of the state of the keyboard.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static KeyboardStateSnapshot GetStateSnapshot(IKeyboard keyboard)
        {
            if (_keyState is null)
            {
                _keyState = new bool[(int)ConfigPhysicalKey.Count];
            }
            
            for (ConfigPhysicalKey key = 0; key < ConfigPhysicalKey.Count; key++)
            {
                _keyState[(int)key] = keyboard.IsPressed((Key)(int)key);
            }

            return new KeyboardStateSnapshot(_keyState);
        }

        /// <summary>
        /// Try to consume a recently pressed key.
        /// </summary>
        /// <param name="key">The pressed key, if available.</param>
        /// <returns>True if a key press was consumed.</returns>
        bool TryConsumePressedKey(out Key key)
        {
            key = Key.Unknown;
            return false;
        }
    }
}
