using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Input;
using System;
using System.Collections.Generic;
using System.Numerics;
using MouseButton = Ryujinx.Input.MouseButton;
using Size = System.Drawing.Size;

namespace Ryujinx.Ava.Input
{
    internal class AvaloniaMouseDriver : IGamepadDriver
    {
        private const int ScrollTimerIntervalMilliseconds = 50;

        private Control _widget;
        private bool _isDisposed;
        private Size _size;
        private readonly TopLevel _window;
        private DispatcherTimer _scrollStopTimer;

        // [Nextendo] Mouse-panning (mouse-look) state.
        private Vector2 _panLastPos;
        private bool _panActive;

        public bool[] PressedButtons { get; }
        public Vector2 CurrentPosition { get; private set; }
        public Vector2 Scroll { get; private set; }

        public string DriverName => "AvaloniaMouseDriver";
        public ReadOnlySpan<string> GamepadsIds => new[] { "0" };

        public AvaloniaMouseDriver(TopLevel window, Control parent)
        {
            _widget = parent;
            _window = window;

            _widget.PointerMoved += Parent_PointerMovedEvent;
            _widget.PointerPressed += Parent_PointerPressedEvent;
            _widget.PointerReleased += Parent_PointerReleasedEvent;
            _widget.PointerWheelChanged += Parent_PointerWheelChanged;

            _window.PointerMoved += Parent_PointerMovedEvent;
            _window.PointerPressed += Parent_PointerPressedEvent;
            _window.PointerReleased += Parent_PointerReleasedEvent;
            _window.PointerWheelChanged += Parent_PointerWheelChanged;

            _scrollStopTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ScrollTimerIntervalMilliseconds)
            };

            PressedButtons = new bool[(int)MouseButton.Count];

            _size = new Size((int)parent.Bounds.Width, (int)parent.Bounds.Height);

            parent.GetObservable(Visual.BoundsProperty).Subscribe(Resized);
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

        private void Resized(Rect rect)
        {
            _size = new Size((int)rect.Width, (int)rect.Height);
        }

        private void HandleScrollStopped()
        {
            Scroll = new Vector2(0, 0);
        }

        private void Parent_PointerWheelChanged(object o, PointerWheelEventArgs args)
        {
            Scroll = new Vector2((float)args.Delta.X, (float)args.Delta.Y);

            _scrollStopTimer?.Stop();

            _scrollStopTimer.Tick += (_, __) =>
            {
                _scrollStopTimer.Stop();

                HandleScrollStopped();

            };
            _scrollStopTimer.Start();
        }

        private void Parent_PointerReleasedEvent(object o, PointerReleasedEventArgs args)
        {
            uint button = (uint)args.InitialPressMouseButton - 1;

            if ((uint)PressedButtons.Length > button)
            {
                PressedButtons[button] = false;
            }
        }
        private void Parent_PointerPressedEvent(object o, PointerPressedEventArgs args)
        {
            PointerPoint currentPoint = args.GetCurrentPoint(_widget);
            uint button = (uint)currentPoint.Properties.PointerUpdateKind;

            if ((uint)PressedButtons.Length > button)
            {
                PressedButtons[button] = true;
            }

            if (args.Pointer.Type == PointerType.Touch) // mouse position is unchanged for touch events, set touch position
            {
                CurrentPosition = new Vector2((float)currentPoint.Position.X, (float)currentPoint.Position.Y);
            }
        }

        private void Parent_PointerMovedEvent(object o, PointerEventArgs args)
        {
            Point position = args.GetPosition(_widget);
            Vector2 pos = new((float)position.X, (float)position.Y);

            CurrentPosition = pos;

            HandleMousePanning(args, pos);
        }

        // [Nextendo] Mouse-look: feed relative mouse movement to the right stick (via MousePanning),
        // and recenter the OS cursor near the window edges on Windows so panning never runs out of
        // room. Hold Alt to release the mouse (e.g. to reach a menu).
        private void HandleMousePanning(PointerEventArgs args, Vector2 pos)
        {
            var hid = ConfigurationState.Instance.Hid;
            MousePanning.Enabled = hid.EnableMousePanning.Value;
            MousePanning.Sensitivity = hid.MousePanningSensitivity.Value;
            MousePanning.InvertX = hid.MousePanningInvertX.Value;
            MousePanning.InvertY = hid.MousePanningInvertY.Value;

            bool releaseHeld = args.KeyModifiers.HasFlag(KeyModifiers.Alt);

            if (!MousePanning.Enabled || releaseHeld)
            {
                _panActive = false;
                MousePanning.Capturing = false;

                return;
            }

            MousePanning.Capturing = true;

            if (!_panActive)
            {
                // First move after (re)capture: seed the reference so we don't emit a jump.
                _panActive = true;
                _panLastPos = pos;

                return;
            }

            Vector2 delta = pos - _panLastPos;
            _panLastPos = pos;

            if (delta != Vector2.Zero)
            {
                MousePanning.Accumulate(delta.X, delta.Y);
            }

            RecenterCursorNearEdge(pos);
        }

        private void RecenterCursorNearEdge(Vector2 pos)
        {
            if (!OperatingSystem.IsWindows() || _widget is null)
            {
                return; // OS cursor warp is Windows-only for now; elsewhere panning is edge-limited.
            }

            int w = _size.Width;
            int h = _size.Height;

            if (w <= 0 || h <= 0)
            {
                return;
            }

            const int Margin = 100;

            if (pos.X > Margin && pos.X < w - Margin && pos.Y > Margin && pos.Y < h - Margin)
            {
                return; // comfortably inside — no need to recenter yet.
            }

            Point center = new(w / 2.0, h / 2.0);
            PixelPoint screen = _widget.PointToScreen(center);

            try { SetCursorPos(screen.X, screen.Y); } catch { /* best-effort */ }

            // Re-seed so the warp itself isn't read as movement on the next event.
            _panLastPos = new Vector2((float)center.X, (float)center.Y);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        public void SetMousePressed(MouseButton button)
        {
            if ((uint)PressedButtons.Length > (uint)button)
            {
                PressedButtons[(uint)button] = true;
            }
        }

        public void SetMouseReleased(MouseButton button)
        {
            if ((uint)PressedButtons.Length > (uint)button)
            {
                PressedButtons[(uint)button] = false;
            }
        }

        public void SetPosition(double x, double y)
        {
            CurrentPosition = new Vector2((float)x, (float)y);
        }

        public bool IsButtonPressed(MouseButton button)
        {
            if ((uint)PressedButtons.Length > (uint)button)
            {
                return PressedButtons[(uint)button];
            }

            return false;
        }

        public Size GetClientSize()
        {
            return _size;
        }

        public IGamepad GetGamepad(string id)
        {
            return new AvaloniaMouse(this);
        }

        public IEnumerable<IGamepad> GetGamepads() => [GetGamepad("0")];

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            _widget.PointerMoved -= Parent_PointerMovedEvent;
            _widget.PointerPressed -= Parent_PointerPressedEvent;
            _widget.PointerReleased -= Parent_PointerReleasedEvent;
            _widget.PointerWheelChanged -= Parent_PointerWheelChanged;

            _window.PointerMoved -= Parent_PointerMovedEvent;
            _window.PointerPressed -= Parent_PointerPressedEvent;
            _window.PointerReleased -= Parent_PointerReleasedEvent;
            _window.PointerWheelChanged -= Parent_PointerWheelChanged;

            _widget = null;
        }
    }
}
