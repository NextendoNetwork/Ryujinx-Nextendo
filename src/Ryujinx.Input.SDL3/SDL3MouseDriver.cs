using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using SDL;
using static SDL.SDL3;

namespace Ryujinx.Input.SDL3
{
    public class SDL3MouseDriver : IGamepadDriver
    {
        private const int CursorHideIdleTime = 5; // seconds

        private bool _isDisposed;
        private readonly HideCursorMode _hideCursorMode;
        private bool _isHidden;
        private long _lastCursorMoveTime;

        public bool[] PressedButtons { get; }

        public Vector2 CurrentPosition { get; private set; }
        public Vector2 Scroll { get; private set; }
        public Size ClientSize;

        public SDL3MouseDriver(HideCursorMode hideCursorMode)
        {
            PressedButtons = new bool[(int)MouseButton.Count];
            _hideCursorMode = hideCursorMode;

            if (_hideCursorMode == HideCursorMode.Always)
            {
                if (!SDL_HideCursor())
                {
                    Logger.Error?.PrintMsg(LogClass.Application, "Failed to disable the cursor.");
                }

                _isHidden = true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static MouseButton DriverButtonToMouseButton(uint rawButton)
        {
            Debug.Assert(rawButton is > 0 and <= (int)MouseButton.Count);

            return (MouseButton)(rawButton - 1);
        }

        public unsafe void UpdatePosition()
        {
            float posX = 0;
            float posY = 0;
            _ = SDL_GetMouseState(&posX, &posY);
            Vector2 position = new(posX, posY);

            if (CurrentPosition != position)
            {
                CurrentPosition = position;
                _lastCursorMoveTime = Stopwatch.GetTimestamp();
            }

            CheckIdle();
        }

        private void CheckIdle()
        {
            if (_hideCursorMode != HideCursorMode.OnIdle)
            {
                return;
            }

            long cursorMoveDelta = Stopwatch.GetTimestamp() - _lastCursorMoveTime;

            if (cursorMoveDelta >= CursorHideIdleTime * Stopwatch.Frequency)
            {
                if (!_isHidden)
                {
                    if (!SDL_HideCursor())
                    {
                        Logger.Error?.PrintMsg(LogClass.Application, "Failed to disable the cursor.");
                    }

                    _isHidden = true;
                }
            }
            else
            {
                if (_isHidden)
                {
                    if (!SDL_ShowCursor())
                    {
                        Logger.Error?.PrintMsg(LogClass.Application, "Failed to enable the cursor.");
                    }

                    _isHidden = false;
                }
            }
        }

        public void Update(SDL_Event evnt)
        {
            switch (evnt.Type)
            {
                case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
                case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
                    uint rawButton = (uint)evnt.button.Button;

                    if (rawButton is > 0 and <= ((int)MouseButton.Count))
                    {
                        PressedButtons[(int)DriverButtonToMouseButton(rawButton)] = evnt.Type == SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN;

                        CurrentPosition = new Vector2(evnt.button.x, evnt.button.y);
                    }

                    break;

                // NOTE: On Linux using Wayland mouse motion events won't be received at all.
                case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                    CurrentPosition = new Vector2(evnt.motion.x, evnt.motion.y);
                    _lastCursorMoveTime = Stopwatch.GetTimestamp();

                    break;

                case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                    Scroll = new Vector2(evnt.wheel.x, evnt.wheel.y);

                    break;
            }
        }

        public void SetClientSize(int width, int height)
        {
            ClientSize = new Size(width, height);
        }

        public bool IsButtonPressed(MouseButton button)
        {
            return PressedButtons[(int)button];
        }

        public Size GetClientSize()
        {
            return ClientSize;
        }

        public string DriverName => "SDL3";

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

        public ReadOnlySpan<string> GamepadsIds => new[] { "0" };

        public IGamepad GetGamepad(string id)
        {
            return new SDL3Mouse(this);
        }

        public IEnumerable<IGamepad> GetGamepads() => [GetGamepad("0")];

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            GC.SuppressFinalize(this);
            _isDisposed = true;
        }
    }
}
