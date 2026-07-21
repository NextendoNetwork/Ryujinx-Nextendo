using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
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

            CurrentPosition = new Vector2((float)position.X, (float)position.Y);
        }

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
