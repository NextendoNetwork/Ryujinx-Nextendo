using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Common.Logging;
using Ryujinx.Input;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AvaKey = Avalonia.Input.Key;
using ConfigPhysicalKey = Ryujinx.Common.Configuration.Hid.PhysicalKey;
using Key = Ryujinx.Input.Key;

namespace Ryujinx.Ava.Input
{
    internal class AvaloniaKeyboardDriver : IKeyboardModeDriver
    {
        private enum PhysicalKeySource
        {
            Direct,
            ObservedFallback,
            Unknown,
        }

        [Flags]
        private enum CGEventFlags : ulong
        {
             AlphaShift = 1UL << 16 // CapsLock
        }

        private enum CGEventSourceStateID : uint
        {
            HIDSystemState = 1
        }

        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern CGEventFlags CGEventSourceFlagsState(CGEventSourceStateID stateID);
        private static readonly string[] _keyboardIdentifers = ["0"];
        private readonly Control _control;
        private readonly Window _window;
        private readonly HashSet<Key> _semanticPressedKeys;
        private readonly HashSet<ConfigPhysicalKey> _physicalPressedKeys;
        private readonly HashSet<Key> _keysToRestoreAfterActivation;
        private readonly Dictionary<Key, ConfigPhysicalKey> _observedPhysicalKeysBySemanticKey;
        private readonly Queue<Key> _semanticPressedKeyQueue;
        private readonly Queue<Key> _physicalPressedKeyQueue;
        private readonly Lock _pressedKeyQueueLock;
        private readonly KeyboardInputMode _defaultMode;

        public event EventHandler<KeyEventArgs> KeyPressed;
        public event EventHandler<KeyEventArgs> KeyRelease;
        public event EventHandler<string> TextInput;

        public string DriverName => "AvaloniaKeyboardDriver";
        public ReadOnlySpan<string> GamepadsIds => _keyboardIdentifers;

        public AvaloniaKeyboardDriver(Control control, KeyboardInputMode defaultMode = KeyboardInputMode.Semantic)
        {
            _control = control;
            _window = control as Window ?? TopLevel.GetTopLevel(control) as Window;
            _semanticPressedKeys = [];
            _physicalPressedKeys = [];
            _keysToRestoreAfterActivation = [];
            _observedPhysicalKeysBySemanticKey = [];
            _semanticPressedKeyQueue = [];
            _physicalPressedKeyQueue = [];
            _pressedKeyQueueLock = new();
            _defaultMode = defaultMode;

            _control.AddHandler(InputElement.KeyDownEvent, OnKeyPress, RoutingStrategies.Tunnel, true);
            _control.AddHandler(InputElement.KeyUpEvent, OnKeyRelease, RoutingStrategies.Tunnel, true);
            _control.TextInput += Control_TextInput;
            _window?.Activated += Window_Activated;
            _window?.Deactivated += Window_Deactivated;
        }

        private void Window_Activated(object sender, EventArgs e)
        {
            RestorePressedKeysAfterActivation();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            lock (_pressedKeyQueueLock)
            {
                _keysToRestoreAfterActivation.Clear();
                _keysToRestoreAfterActivation.UnionWith(_semanticPressedKeys);
                _observedPhysicalKeysBySemanticKey.Clear();
            }

            Clear();
        }

        private void Control_TextInput(object sender, TextInputEventArgs e)
        {
            TextInput?.Invoke(this, e.Text);
        }

        public event Action<string> OnGamepadConnected
        {
            add { }
            remove { }
        }

        public event Action<string> OnGamepadDisconnected
        {
            add { }
            remove { }
        }

        public IGamepad GetGamepad(string id)
        {
            return GetKeyboard(id, _defaultMode);
        }

        public IKeyboard GetKeyboard(string id, KeyboardInputMode mode)
        {
            if (!_keyboardIdentifers[0].Equals(id))
            {
                return null;
            }

            return new AvaloniaKeyboard(this, _keyboardIdentifers[0], LocaleManager.Instance[LocaleKeys.KeyboardLayout_KeyboardInputMode], mode);
        }

        public IEnumerable<IGamepad> GetGamepads() => [GetGamepad("0")];

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _control.RemoveHandler(InputElement.KeyDownEvent, OnKeyPress);
                _control.RemoveHandler(InputElement.KeyUpEvent, OnKeyRelease);
                _control.TextInput -= Control_TextInput;
                if (_window != null)
                {
                    _window.Activated -= Window_Activated;
                    _window.Deactivated -= Window_Deactivated;
                }
                _observedPhysicalKeysBySemanticKey.Clear();
            }
        }
        protected void OnKeyPress(object sender, KeyEventArgs args)
        {
            UpdateKeyStates(args, true);
            KeyPressed?.Invoke(this, args);
        }

        protected void OnKeyRelease(object sender, KeyEventArgs args)
        {
            UpdateKeyStates(args, false);
            KeyRelease?.Invoke(this, args);
        }

        internal bool IsPressed(Key key, KeyboardInputMode mode)
        {
            if (key is Key.Unbound or Key.Unknown)
            {
                return false;
            }

            if (key == Key.CapsLock)
            {
                return IsCapsLockOnMacOS();
            }

            return mode == KeyboardInputMode.Physical
                ? _physicalPressedKeys.Contains((ConfigPhysicalKey)(int)key)
                : _semanticPressedKeys.Contains(key);
        }

        private bool IsCapsLockOnMacOS()
        {
            bool currentState = false;

            try
            {
                if (OperatingSystem.IsMacOS())
                {
                    CGEventFlags flags = CGEventSourceFlagsState(CGEventSourceStateID.HIDSystemState);
                    currentState = (flags & CGEventFlags.AlphaShift) != 0;
                }
                else
                {
                    // Fallback: use Avalonia's tracked key state (semantic CapsLock)
                    if (AvaloniaKeyboardMappingHelper.TryGetAvaKey(Key.CapsLock, out AvaKey nativeKey))
                    {
                        currentState = _semanticPressedKeys.Contains(Key.CapsLock);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug?.Print(LogClass.UI, $"Failed to query CapsLock state: {ex}");
            }

            return currentState;
        }

        internal void Clear(KeyboardInputMode mode)
        {
            lock (_pressedKeyQueueLock)
            {
                if (mode == KeyboardInputMode.Physical)
                {
                    _physicalPressedKeys.Clear();
                    _physicalPressedKeyQueue.Clear();
                }
                else
                {
                    _semanticPressedKeys.Clear();
                    _semanticPressedKeyQueue.Clear();
                }
            }
        }

        public void Clear()
        {
            lock (_pressedKeyQueueLock)
            {
                _semanticPressedKeys.Clear();
                _physicalPressedKeys.Clear();
                _semanticPressedKeyQueue.Clear();
                _physicalPressedKeyQueue.Clear();
            }
        }

        private void RestorePressedKeysAfterActivation()
        {
            if (!OperatingSystem.IsWindows())
            {
                lock (_pressedKeyQueueLock)
                {
                    _keysToRestoreAfterActivation.Clear();
                }

                return;
            }

            lock (_pressedKeyQueueLock)
            {
                if (_keysToRestoreAfterActivation.Count == 0)
                {
                    return;
                }

                foreach (Key key in _keysToRestoreAfterActivation)
                {
                    if (!TryGetWindowsVirtualKey(key, out int virtualKey) ||
                        !IsWindowsKeyPressed(virtualKey))
                    {
                        continue;
                    }

                    _semanticPressedKeys.Add(key);

                    ConfigPhysicalKey physicalKey = GetPhysicalKeyForSemanticKey(key);

                    if (physicalKey is not ConfigPhysicalKey.Unknown and not ConfigPhysicalKey.Unbound)
                    {
                        _physicalPressedKeys.Add(physicalKey);
                    }
                }

                _keysToRestoreAfterActivation.Clear();
            }
        }

        private ConfigPhysicalKey GetPhysicalKeyForSemanticKey(Key key)
        {
            if (_observedPhysicalKeysBySemanticKey.TryGetValue(key, out ConfigPhysicalKey physicalKey))
            {
                return physicalKey;
            }

            return key is >= Key.Unknown and < Key.Count
                ? (ConfigPhysicalKey)(int)key
                : ConfigPhysicalKey.Unknown;
        }

        [SupportedOSPlatform("windows")]
        private static bool IsWindowsKeyPressed(int virtualKey)
        {
            return (Win32NativeInterop.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        private static bool TryGetWindowsVirtualKey(Key key, out int virtualKey)
        {
            switch (key)
            {
                case >= Key.A and <= Key.Z:
                    virtualKey = 'A' + (int)(key - Key.A);
                    return true;
                case >= Key.Number0 and <= Key.Number9:
                    virtualKey = '0' + (int)(key - Key.Number0);
                    return true;
                case >= Key.F1 and <= Key.F24:
                    virtualKey = 0x70 + (int)(key - Key.F1);
                    return true;
                case Key.ShiftLeft:
                    virtualKey = 0xA0;
                    return true;
                case Key.ShiftRight:
                    virtualKey = 0xA1;
                    return true;
                case Key.ControlLeft:
                    virtualKey = 0xA2;
                    return true;
                case Key.ControlRight:
                    virtualKey = 0xA3;
                    return true;
                case Key.AltLeft:
                    virtualKey = 0xA4;
                    return true;
                case Key.AltRight:
                    virtualKey = 0xA5;
                    return true;
                case Key.WinLeft:
                    virtualKey = 0x5B;
                    return true;
                case Key.WinRight:
                    virtualKey = 0x5C;
                    return true;
                case Key.Menu:
                    virtualKey = 0x5D;
                    return true;
                case Key.Up:
                    virtualKey = 0x26;
                    return true;
                case Key.Down:
                    virtualKey = 0x28;
                    return true;
                case Key.Left:
                    virtualKey = 0x25;
                    return true;
                case Key.Right:
                    virtualKey = 0x27;
                    return true;
                case Key.Enter:
                    virtualKey = 0x0D;
                    return true;
                case Key.Escape:
                    virtualKey = 0x1B;
                    return true;
                case Key.Space:
                    virtualKey = 0x20;
                    return true;
                case Key.Tab:
                    virtualKey = 0x09;
                    return true;
                case Key.BackSpace:
                    virtualKey = 0x08;
                    return true;
                case Key.Insert:
                    virtualKey = 0x2D;
                    return true;
                case Key.Delete:
                    virtualKey = 0x2E;
                    return true;
                case Key.PageUp:
                    virtualKey = 0x21;
                    return true;
                case Key.PageDown:
                    virtualKey = 0x22;
                    return true;
                case Key.Home:
                    virtualKey = 0x24;
                    return true;
                case Key.End:
                    virtualKey = 0x23;
                    return true;
                case Key.CapsLock:
                    virtualKey = 0x14;
                    return true;
                case Key.ScrollLock:
                    virtualKey = 0x91;
                    return true;
                case Key.PrintScreen:
                    virtualKey = 0x2C;
                    return true;
                case Key.Pause:
                    virtualKey = 0x13;
                    return true;
                case Key.NumLock:
                    virtualKey = 0x90;
                    return true;
                case Key.Clear:
                    virtualKey = 0x0C;
                    return true;
                case >= Key.Keypad0 and <= Key.Keypad9:
                    virtualKey = 0x60 + (int)(key - Key.Keypad0);
                    return true;
                case Key.KeypadDivide:
                    virtualKey = 0x6F;
                    return true;
                case Key.KeypadMultiply:
                    virtualKey = 0x6A;
                    return true;
                case Key.KeypadSubtract:
                    virtualKey = 0x6D;
                    return true;
                case Key.KeypadAdd:
                    virtualKey = 0x6B;
                    return true;
                case Key.KeypadDecimal:
                    virtualKey = 0x6E;
                    return true;
                case Key.KeypadEnter:
                    virtualKey = 0x0D;
                    return true;
                case Key.Tilde:
                    virtualKey = 0xC0;
                    return true;
                case Key.Grave:
                    virtualKey = 0xE2;
                    return true;
                case Key.Minus:
                    virtualKey = 0xBD;
                    return true;
                case Key.Plus:
                    virtualKey = 0xBB;
                    return true;
                case Key.BracketLeft:
                    virtualKey = 0xDB;
                    return true;
                case Key.BracketRight:
                    virtualKey = 0xDD;
                    return true;
                case Key.Semicolon:
                    virtualKey = 0xBA;
                    return true;
                case Key.Quote:
                    virtualKey = 0xDE;
                    return true;
                case Key.Comma:
                    virtualKey = 0xBC;
                    return true;
                case Key.Period:
                    virtualKey = 0xBE;
                    return true;
                case Key.Slash:
                    virtualKey = 0xBF;
                    return true;
                case Key.BackSlash:
                    virtualKey = 0xDC;
                    return true;
                default:
                    virtualKey = 0;
                    return false;
            }
        }

        internal bool TryConsumePressedKey(KeyboardInputMode mode, out Key key)
        {
            lock (_pressedKeyQueueLock)
            {
                Queue<Key> queue = mode == KeyboardInputMode.Physical ? _physicalPressedKeyQueue : _semanticPressedKeyQueue;

                if (queue.TryDequeue(out key))
                {
                    return true;
                }
            }

            key = Key.Unknown;
            return false;
        }

        private static void UpdateKeyState(HashSet<Key> pressedKeys, Key key, bool isPressed)
        {
            if (key is Key.Unknown or Key.Unbound)
            {
                return;
            }

            if (isPressed)
            {
                pressedKeys.Add(key);
                return;
            }

            pressedKeys.Remove(key);
        }

        private static void UpdateKeyState(HashSet<ConfigPhysicalKey> pressedKeys, ConfigPhysicalKey key, bool isPressed)
        {
            if (key is ConfigPhysicalKey.Unknown or ConfigPhysicalKey.Unbound)
            {
                return;
            }

            if (isPressed)
            {
                pressedKeys.Add(key);
                return;
            }

            pressedKeys.Remove(key);
        }

        private void UpdateKeyStates(KeyEventArgs args, bool isPressed)
        {
            Key semanticKey = AvaloniaKeyboardMappingHelper.ToInputKey(args.Key);
            Key resolvedSemanticKey = AvaloniaKeyboardMappingHelper.ToInputKey(args.PhysicalKey, args.Key);
            ConfigPhysicalKey physicalKey = GetPhysicalInputKey(args, semanticKey, out PhysicalKeySource physicalKeySource);
            bool semanticWasPressed = _semanticPressedKeys.Contains(resolvedSemanticKey);
            bool physicalWasPressed = _physicalPressedKeys.Contains(physicalKey);
            bool semanticStateChanged = resolvedSemanticKey is not Key.Unknown and not Key.Unbound && semanticWasPressed != isPressed;
            bool physicalStateChanged = physicalKey is not ConfigPhysicalKey.Unknown and not ConfigPhysicalKey.Unbound && physicalWasPressed != isPressed;
            bool bufferedSemanticPress = false;
            bool bufferedPhysicalPress = false;

            UpdateKeyState(_semanticPressedKeys, resolvedSemanticKey, isPressed);
            UpdateKeyState(_physicalPressedKeys, physicalKey, isPressed);

            if (isPressed)
            {
                lock (_pressedKeyQueueLock)
                {
                    if (!semanticWasPressed && resolvedSemanticKey is not Key.Unknown and not Key.Unbound)
                    {
                        _semanticPressedKeyQueue.Enqueue(resolvedSemanticKey);
                        bufferedSemanticPress = true;
                    }

                    if (!physicalWasPressed && physicalKey is not ConfigPhysicalKey.Unknown and not ConfigPhysicalKey.Unbound)
                    {
                        _physicalPressedKeyQueue.Enqueue((Key)(int)physicalKey);
                        bufferedPhysicalPress = true;
                    }
                }
            }

            if (isPressed &&
                semanticKey is not Key.Unknown and not Key.Unbound &&
                physicalKey is not ConfigPhysicalKey.Unknown and not ConfigPhysicalKey.Unbound)
            {
                _observedPhysicalKeysBySemanticKey[semanticKey] = physicalKey;
            }

            if (ConfigurationState.Instance.Logger.EnableAvaloniaLog &&
                (semanticStateChanged || physicalStateChanged))
            {
                Logger.Info?.Print(
                    LogClass.UI,
                    $"Keyboard {(isPressed ? "down" : "up")}: avaloniaKey={args.Key}, avaloniaPhysical={args.PhysicalKey}, keySymbol={FormatKeySymbol(args.KeySymbol)}, modifiers={args.KeyModifiers}, semantic={semanticKey}, resolvedSemantic={resolvedSemanticKey}, physical={physicalKey}, physicalSource={physicalKeySource}, bufferedSemantic={bufferedSemanticPress}, bufferedPhysical={bufferedPhysicalPress}, semanticPressed={_semanticPressedKeys.Count}, physicalPressed={_physicalPressedKeys.Count}");
            }
        }

        private ConfigPhysicalKey GetPhysicalInputKey(KeyEventArgs args, Key semanticKey, out PhysicalKeySource source)
        {
            Key key = AvaloniaKeyboardMappingHelper.ToInputKey(args.PhysicalKey);

            if (key is >= Key.Unknown and < Key.Count)
            {
                source = PhysicalKeySource.Direct;
                return (ConfigPhysicalKey)(int)key;
            }

            if (semanticKey is not Key.Unknown and not Key.Unbound &&
                _observedPhysicalKeysBySemanticKey.TryGetValue(semanticKey, out ConfigPhysicalKey observedPhysicalKey))
            {
                source = PhysicalKeySource.ObservedFallback;
                return observedPhysicalKey;
            }

            source = PhysicalKeySource.Unknown;
            return ConfigPhysicalKey.Unknown;
        }

        private static string FormatKeySymbol(string keySymbol)
        {
            return string.IsNullOrEmpty(keySymbol) ? "<none>" : keySymbol;
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
