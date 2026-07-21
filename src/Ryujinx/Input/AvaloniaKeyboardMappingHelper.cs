using Ryujinx.Input;
using System;
using System.Collections.Generic;
using AvaKey = Avalonia.Input.Key;
using AvaPhysicalKey = Avalonia.Input.PhysicalKey;

namespace Ryujinx.Ava.Input
{
    internal static class AvaloniaKeyboardMappingHelper
    {
        private static readonly AvaKey[] _keyMapping =
        [
            // NOTE: Invalid
            AvaKey.None,

            AvaKey.LeftShift,
            AvaKey.RightShift,
            AvaKey.LeftCtrl,
            AvaKey.RightCtrl,
            AvaKey.LeftAlt,
            AvaKey.RightAlt,
            AvaKey.LWin,
            AvaKey.RWin,
            AvaKey.Apps,
            AvaKey.F1,
            AvaKey.F2,
            AvaKey.F3,
            AvaKey.F4,
            AvaKey.F5,
            AvaKey.F6,
            AvaKey.F7,
            AvaKey.F8,
            AvaKey.F9,
            AvaKey.F10,
            AvaKey.F11,
            AvaKey.F12,
            AvaKey.F13,
            AvaKey.F14,
            AvaKey.F15,
            AvaKey.F16,
            AvaKey.F17,
            AvaKey.F18,
            AvaKey.F19,
            AvaKey.F20,
            AvaKey.F21,
            AvaKey.F22,
            AvaKey.F23,
            AvaKey.F24,

            AvaKey.None,
            AvaKey.None,
            AvaKey.None,
            AvaKey.None,
            AvaKey.None,
            AvaKey.None,
            AvaKey.None,
            AvaKey.None,
            AvaKey.None,
            AvaKey.None,
            AvaKey.None,

            AvaKey.Up,
            AvaKey.Down,
            AvaKey.Left,
            AvaKey.Right,
            AvaKey.Return,
            AvaKey.Escape,
            AvaKey.Space,
            AvaKey.Tab,
            AvaKey.Back,
            AvaKey.Insert,
            AvaKey.Delete,
            AvaKey.PageUp,
            AvaKey.PageDown,
            AvaKey.Home,
            AvaKey.End,
            AvaKey.CapsLock,
            AvaKey.Scroll,
            AvaKey.Print,
            AvaKey.Pause,
            AvaKey.NumLock,
            AvaKey.Clear,
            AvaKey.NumPad0,
            AvaKey.NumPad1,
            AvaKey.NumPad2,
            AvaKey.NumPad3,
            AvaKey.NumPad4,
            AvaKey.NumPad5,
            AvaKey.NumPad6,
            AvaKey.NumPad7,
            AvaKey.NumPad8,
            AvaKey.NumPad9,
            AvaKey.Divide,
            AvaKey.Multiply,
            AvaKey.Subtract,
            AvaKey.Add,
            AvaKey.Decimal,
            AvaKey.Enter,
            AvaKey.A,
            AvaKey.B,
            AvaKey.C,
            AvaKey.D,
            AvaKey.E,
            AvaKey.F,
            AvaKey.G,
            AvaKey.H,
            AvaKey.I,
            AvaKey.J,
            AvaKey.K,
            AvaKey.L,
            AvaKey.M,
            AvaKey.N,
            AvaKey.O,
            AvaKey.P,
            AvaKey.Q,
            AvaKey.R,
            AvaKey.S,
            AvaKey.T,
            AvaKey.U,
            AvaKey.V,
            AvaKey.W,
            AvaKey.X,
            AvaKey.Y,
            AvaKey.Z,
            AvaKey.D0,
            AvaKey.D1,
            AvaKey.D2,
            AvaKey.D3,
            AvaKey.D4,
            AvaKey.D5,
            AvaKey.D6,
            AvaKey.D7,
            AvaKey.D8,
            AvaKey.D9,
            AvaKey.OemTilde,
            AvaKey.Oem102,
            AvaKey.OemMinus,
            AvaKey.OemPlus,
            AvaKey.OemOpenBrackets,
            AvaKey.OemCloseBrackets,
            AvaKey.OemSemicolon,
            AvaKey.OemQuotes,
            AvaKey.OemComma,
            AvaKey.OemPeriod,
            AvaKey.OemQuestion,
            AvaKey.OemPipe,

            // NOTE: invalid
            AvaKey.None
        ];

        private static readonly AvaPhysicalKey[] _physicalKeyMapping =
        [
            // NOTE: Invalid
            AvaPhysicalKey.None,

            AvaPhysicalKey.ShiftLeft,
            AvaPhysicalKey.ShiftRight,
            AvaPhysicalKey.ControlLeft,
            AvaPhysicalKey.ControlRight,
            AvaPhysicalKey.AltLeft,
            AvaPhysicalKey.AltRight,
            AvaPhysicalKey.MetaLeft,
            AvaPhysicalKey.MetaRight,
            AvaPhysicalKey.ContextMenu,
            AvaPhysicalKey.F1,
            AvaPhysicalKey.F2,
            AvaPhysicalKey.F3,
            AvaPhysicalKey.F4,
            AvaPhysicalKey.F5,
            AvaPhysicalKey.F6,
            AvaPhysicalKey.F7,
            AvaPhysicalKey.F8,
            AvaPhysicalKey.F9,
            AvaPhysicalKey.F10,
            AvaPhysicalKey.F11,
            AvaPhysicalKey.F12,
            AvaPhysicalKey.F13,
            AvaPhysicalKey.F14,
            AvaPhysicalKey.F15,
            AvaPhysicalKey.F16,
            AvaPhysicalKey.F17,
            AvaPhysicalKey.F18,
            AvaPhysicalKey.F19,
            AvaPhysicalKey.F20,
            AvaPhysicalKey.F21,
            AvaPhysicalKey.F22,
            AvaPhysicalKey.F23,
            AvaPhysicalKey.F24,

            AvaPhysicalKey.None,
            AvaPhysicalKey.None,
            AvaPhysicalKey.None,
            AvaPhysicalKey.None,
            AvaPhysicalKey.None,
            AvaPhysicalKey.None,
            AvaPhysicalKey.None,
            AvaPhysicalKey.None,
            AvaPhysicalKey.None,
            AvaPhysicalKey.None,
            AvaPhysicalKey.None,

            AvaPhysicalKey.ArrowUp,
            AvaPhysicalKey.ArrowDown,
            AvaPhysicalKey.ArrowLeft,
            AvaPhysicalKey.ArrowRight,
            AvaPhysicalKey.Enter,
            AvaPhysicalKey.Escape,
            AvaPhysicalKey.Space,
            AvaPhysicalKey.Tab,
            AvaPhysicalKey.Backspace,
            AvaPhysicalKey.Insert,
            AvaPhysicalKey.Delete,
            AvaPhysicalKey.PageUp,
            AvaPhysicalKey.PageDown,
            AvaPhysicalKey.Home,
            AvaPhysicalKey.End,
            AvaPhysicalKey.CapsLock,
            AvaPhysicalKey.ScrollLock,
            AvaPhysicalKey.PrintScreen,
            AvaPhysicalKey.Pause,
            AvaPhysicalKey.NumLock,
            AvaPhysicalKey.NumPadClear,
            AvaPhysicalKey.NumPad0,
            AvaPhysicalKey.NumPad1,
            AvaPhysicalKey.NumPad2,
            AvaPhysicalKey.NumPad3,
            AvaPhysicalKey.NumPad4,
            AvaPhysicalKey.NumPad5,
            AvaPhysicalKey.NumPad6,
            AvaPhysicalKey.NumPad7,
            AvaPhysicalKey.NumPad8,
            AvaPhysicalKey.NumPad9,
            AvaPhysicalKey.NumPadDivide,
            AvaPhysicalKey.NumPadMultiply,
            AvaPhysicalKey.NumPadSubtract,
            AvaPhysicalKey.NumPadAdd,
            AvaPhysicalKey.NumPadDecimal,
            AvaPhysicalKey.NumPadEnter,
            AvaPhysicalKey.A,
            AvaPhysicalKey.B,
            AvaPhysicalKey.C,
            AvaPhysicalKey.D,
            AvaPhysicalKey.E,
            AvaPhysicalKey.F,
            AvaPhysicalKey.G,
            AvaPhysicalKey.H,
            AvaPhysicalKey.I,
            AvaPhysicalKey.J,
            AvaPhysicalKey.K,
            AvaPhysicalKey.L,
            AvaPhysicalKey.M,
            AvaPhysicalKey.N,
            AvaPhysicalKey.O,
            AvaPhysicalKey.P,
            AvaPhysicalKey.Q,
            AvaPhysicalKey.R,
            AvaPhysicalKey.S,
            AvaPhysicalKey.T,
            AvaPhysicalKey.U,
            AvaPhysicalKey.V,
            AvaPhysicalKey.W,
            AvaPhysicalKey.X,
            AvaPhysicalKey.Y,
            AvaPhysicalKey.Z,
            AvaPhysicalKey.Digit0,
            AvaPhysicalKey.Digit1,
            AvaPhysicalKey.Digit2,
            AvaPhysicalKey.Digit3,
            AvaPhysicalKey.Digit4,
            AvaPhysicalKey.Digit5,
            AvaPhysicalKey.Digit6,
            AvaPhysicalKey.Digit7,
            AvaPhysicalKey.Digit8,
            AvaPhysicalKey.Digit9,
            AvaPhysicalKey.Backquote,
            AvaPhysicalKey.IntlBackslash,
            AvaPhysicalKey.Minus,
            AvaPhysicalKey.Equal,
            AvaPhysicalKey.BracketLeft,
            AvaPhysicalKey.BracketRight,
            AvaPhysicalKey.Semicolon,
            AvaPhysicalKey.Quote,
            AvaPhysicalKey.Comma,
            AvaPhysicalKey.Period,
            AvaPhysicalKey.Slash,
            AvaPhysicalKey.Backslash,

            // NOTE: invalid
            AvaPhysicalKey.None
        ];

        private static readonly Dictionary<AvaKey, Key> _avaKeyMapping;
        private static readonly Dictionary<AvaPhysicalKey, Key> _avaPhysicalKeyMapping;

        static AvaloniaKeyboardMappingHelper()
        {
            Key[] inputKeys = Enum.GetValues<Key>();

            // NOTE: Avalonia.Input.Key is not contiguous and quite large, so use a dictionary instead of an array.
            _avaKeyMapping = new Dictionary<AvaKey, Key>();
            _avaPhysicalKeyMapping = new Dictionary<AvaPhysicalKey, Key>();

            foreach (Key key in inputKeys)
            {
                if (TryGetAvaKey(key, out AvaKey avaKey))
                {
                    _avaKeyMapping[avaKey] = key;
                }

                if (TryGetAvaPhysicalKey(key, out AvaPhysicalKey avaPhysicalKey))
                {
                    _avaPhysicalKeyMapping[avaPhysicalKey] = key;
                }
            }

            // Alias additional Avalonia key values to improve non-US layout support.
            _avaKeyMapping[AvaKey.Oem1] = Key.Semicolon;
            _avaKeyMapping[AvaKey.Oem2] = Key.Slash;
            _avaKeyMapping[AvaKey.Oem3] = Key.Tilde;
            _avaKeyMapping[AvaKey.Oem4] = Key.BracketLeft;
            _avaKeyMapping[AvaKey.Oem5] = Key.BackSlash;
            _avaKeyMapping[AvaKey.Oem6] = Key.BracketRight;
            _avaKeyMapping[AvaKey.Oem7] = Key.Quote;
            _avaKeyMapping[AvaKey.OemBackslash] = Key.Grave;
            _avaKeyMapping[AvaKey.Oem102] = Key.Grave;

            // Common alternates for non-US/JIS physical keys.
            _avaPhysicalKeyMapping[AvaPhysicalKey.IntlRo] = Key.BackSlash;
            _avaPhysicalKeyMapping[AvaPhysicalKey.IntlYen] = Key.BackSlash;
        }

        public static bool TryGetAvaKey(Key key, out AvaKey avaKey)
        {
            avaKey = AvaKey.None;

            bool keyExist = key < Key.Count && (int)key < _keyMapping.Length;
            if (keyExist)
            {
                avaKey = _keyMapping[(int)key];
            }

            return keyExist;
        }

        public static bool TryGetAvaPhysicalKey(Key key, out AvaPhysicalKey avaPhysicalKey)
        {
            avaPhysicalKey = AvaPhysicalKey.None;

            bool keyExist = key < Key.Count && (int)key < _physicalKeyMapping.Length;
            if (keyExist)
            {
                avaPhysicalKey = _physicalKeyMapping[(int)key];
            }

            return keyExist;
        }

        public static Key ToInputKey(AvaKey key)
        {
            return _avaKeyMapping.GetValueOrDefault(key, Key.Unknown);
        }

        public static Key ToInputKey(AvaPhysicalKey key)
        {
            return _avaPhysicalKeyMapping.GetValueOrDefault(key, Key.Unknown);
        }

        public static Key ToInputKey(AvaPhysicalKey physicalKey, AvaKey key)
        {
            Key inputKey = ToInputKey(key);

            return inputKey != Key.Unknown ? inputKey : ToInputKey(physicalKey);
        }
    }
}
