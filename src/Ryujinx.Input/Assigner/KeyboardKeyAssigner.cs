using Ryujinx.Common.Logging;

namespace Ryujinx.Input.Assigner
{
    /// <summary>
    /// <see cref="IButtonAssigner"/> implementation for <see cref="IKeyboard"/>.
    /// </summary>
    public class KeyboardKeyAssigner : IButtonAssigner
    {
        private readonly IKeyboard _keyboard;

        private KeyboardStateSnapshot _keyboardState;
        private Button? _pressedButton;

        public KeyboardKeyAssigner(IKeyboard keyboard)
        {
            _keyboard = keyboard;
        }

        public void Initialize()
        {
            _pressedButton = null;
        }

        public void ReadInput()
        {
            _keyboardState = _keyboard.GetKeyboardStateSnapshot();

            if (_pressedButton is null)
            {
                Button? buttonFromState = GetPressedButtonFromState();
                Button? buttonFromBufferedPress = buttonFromState is null ? GetPressedButtonFromBufferedPress() : null;

                _pressedButton = buttonFromState ?? buttonFromBufferedPress;
            }

            if (_pressedButton is not null)
            {
                string source = _pressedButton.HasValue && GetPressedButtonFromState() is not null ? "state" : "buffered-press";
                Logger.Debug?.Print(LogClass.UI, $"Keyboard assigner registered key={_pressedButton.Value.AsHidType<Key>()}, source={source}, cancelPressed={ShouldCancel()}");
            }
        }

        public bool IsAnyButtonPressed()
        {
            return _pressedButton is not null;
        }

        public bool ShouldCancel()
        {
            return _keyboardState.IsPressed(Key.Escape);
        }

        public Button? GetPressedButton()
        {
            return !ShouldCancel() ? _pressedButton : null;
        }

        private Button? GetPressedButtonFromState()
        {
            Key aliasedKey = GetAliasedPressedKey();

            if (aliasedKey != Key.Unknown)
            {
                return new Button(aliasedKey);
            }

            for (Key key = Key.Unknown; key < Key.Count; key++)
            {
                if (_keyboardState.IsPressed(key))
                {
                    return new Button(key);
                }
            }

            return null;
        }

        private Button? GetPressedButtonFromBufferedPress()
        {
            return _keyboard.TryConsumePressedKey(out Key key) ? new Button(key) : null;
        }

        private Key GetAliasedPressedKey()
        {
            // On some layouts (for example AltGr on Windows), Right Alt is reported as Ctrl+Alt.
            // Prefer AltRight in that case so the binding reflects the physical key used.
            if (_keyboardState.IsPressed(Key.ControlLeft) && _keyboardState.IsPressed(Key.AltRight))
            {
                return Key.AltRight;
            }

            // On some Copilot keyboards, the key in the right-control position is reported as
            // ShiftLeft+Win+F23. Prefer ControlRight so the binding reflects that physical key.
            if (_keyboardState.IsPressed(Key.ShiftLeft) &&
                _keyboardState.IsPressed(Key.F23) &&
                (_keyboardState.IsPressed(Key.WinLeft) || _keyboardState.IsPressed(Key.WinRight)))
            {
                return Key.ControlRight;
            }

            return Key.Unknown;
        }
    }
}
