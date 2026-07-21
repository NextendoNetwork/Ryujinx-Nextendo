using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SDL;
using static SDL.SDL3;

namespace Ryujinx.SDL3.Common
{
    public class SDL3Driver : IDisposable
    {
        private static SDL3Driver _instance;

        public static SDL3Driver Instance
        {
            get
            {
                _instance ??= new SDL3Driver();

                return _instance;
            }
        }

        public static Action<Action> MainThreadDispatcher { get; set; }

        private const SDL_InitFlags SdlInitFlags = SDL_InitFlags.SDL_INIT_EVENTS | SDL_InitFlags.SDL_INIT_GAMEPAD | SDL_InitFlags.SDL_INIT_JOYSTICK | SDL_InitFlags.SDL_INIT_AUDIO | SDL_InitFlags.SDL_INIT_VIDEO;

        private bool _isRunning;
        private uint _refereceCount;
        private Thread _worker;

        public event Action<SDL_JoystickID> OnJoyStickConnected;
        public event Action<SDL_JoystickID> OnJoystickDisconnected;

        public event Action<SDL_JoystickID, SDL_PowerState> OnJoyBatteryUpdated;

        private ConcurrentDictionary<SDL_WindowID, Action<SDL_Event>> _registeredWindowHandlers;

        private readonly Lock _lock = new();

        private SDL3Driver() { }

        public void Initialize()
        {
            lock (_lock)
            {
                _refereceCount++;

                if (_isRunning)
                {
                    return;
                }

                SDL_SetHint(SDL_HINT_APP_NAME, "Ryujinx");
                SDL_SetHint(SDL_HINT_JOYSTICK_ENHANCED_REPORTS , "1");
                SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");
                SDL_SetHint(SDL_HINT_JOYSTICK_HIDAPI_SWITCH_HOME_LED, "0");
                SDL_SetHint(SDL_HINT_JOYSTICK_HIDAPI_JOY_CONS, "1");
                SDL_SetHint(SDL_HINT_VIDEO_ALLOW_SCREENSAVER, "1");

                // NOTE: As of SDL3 2.24.0, joycons are combined by default but the motion source only come from one of them.
                // We disable this behavior for now.
                SDL_SetHint(SDL_HINT_JOYSTICK_HIDAPI_COMBINE_JOY_CONS, "0");

                if (!SDL_Init(SdlInitFlags))
                {
                    string errorMessage = $"SDL3 initialization failed with error \"{SDL_GetError()}\"";

                    Logger.Error?.Print(LogClass.Application, errorMessage);

                    throw new Exception(errorMessage);
                }

                // First ensure that we only enable joystick events (for connected/disconnected).
                SDL_SetGamepadEventsEnabled(false);
                SDL_SetJoystickEventsEnabled(true);
                if (SDL_GamepadEventsEnabled())
                {
                    Logger.Error?.PrintMsg(LogClass.Application, "Couldn't change the state of game controller events.");
                }

                if (!SDL_JoystickEventsEnabled())
                {
                    Logger.Error?.PrintMsg(LogClass.Application, $"Failed to enable joystick event polling: {SDL_GetError()}");
                }

                // Disable all joysticks information, we don't need them no need to flood the event queue for that.
                SDL_SetEventEnabled((uint)SDL_EventType.SDL_EVENT_JOYSTICK_AXIS_MOTION, false);
                SDL_SetEventEnabled((uint)SDL_EventType.SDL_EVENT_JOYSTICK_BALL_MOTION, false);
                SDL_SetEventEnabled((uint)SDL_EventType.SDL_EVENT_JOYSTICK_HAT_MOTION, false);
                SDL_SetEventEnabled((uint)SDL_EventType.SDL_EVENT_JOYSTICK_BUTTON_DOWN, false);
                SDL_SetEventEnabled((uint)SDL_EventType.SDL_EVENT_JOYSTICK_BUTTON_UP, false);

                SDL_SetEventEnabled((uint)SDL_EventType.SDL_EVENT_GAMEPAD_SENSOR_UPDATE, false);

                string gamepadDbPath = Path.Combine(AppDataManager.BaseDirPath, "SDL_GameControllerDB.txt");

                if (File.Exists(gamepadDbPath))
                {
                    SDL_AddGamepadMappingsFromFile(gamepadDbPath);
                }

                _registeredWindowHandlers = new ConcurrentDictionary<SDL_WindowID, Action<SDL_Event>>();
                _worker = new Thread(EventWorker);
                _isRunning = true;
                _worker.Start();
            }
        }

        public bool RegisterWindow(SDL_WindowID windowId, Action<SDL_Event> windowEventHandler)
        {
            return _registeredWindowHandlers.TryAdd(windowId, windowEventHandler);
        }

        public void UnregisterWindow(SDL_WindowID windowId)
        {
            _registeredWindowHandlers.Remove(windowId, out _);
        }

        private void HandleSDLEvent(ref SDL_Event evnt)
        {
            SDL_EventType type = evnt.Type;
            if (type == SDL_EventType.SDL_EVENT_JOYSTICK_ADDED)
            {
                SDL_JoystickID instanceId = evnt.jbutton.which;

                // SDL3 loves to be inconsistent here by providing the device id instead of the instance id (like on removed event), as such we just grab it and send it inside our system.
                Logger.Debug?.Print(LogClass.Application, $"Added joystick instance id {instanceId}");

                OnJoyStickConnected?.Invoke(instanceId);
            }
            else if (type == SDL_EventType.SDL_EVENT_JOYSTICK_REMOVED)
            {
                Logger.Debug?.Print(LogClass.Application, $"Removed joystick instance id {evnt.jbutton.which}");

                OnJoystickDisconnected?.Invoke(evnt.jbutton.which);
            }
            else if (type == SDL_EventType.SDL_EVENT_JOYSTICK_BATTERY_UPDATED)
            {
                OnJoyBatteryUpdated?.Invoke(evnt.jbutton.which, evnt.jbattery.state);
            }
            else if (
                ((uint)type >= (uint)SDL_EventType.SDL_EVENT_WINDOW_FIRST && (uint)type <= (uint)SDL_EventType.SDL_EVENT_WINDOW_LAST) ||
                type is SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN or SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP
            )
            {
                if (_registeredWindowHandlers.TryGetValue(evnt.window.windowID, out Action<SDL_Event> handler))
                {
                    handler(evnt);
                }
            }
        }

        private unsafe void EventWorker()
        {
            const int WaitTimeMs = 10;

            using ManualResetEventSlim waitHandle = new(false);

            while (_isRunning)
            {
                MainThreadDispatcher?.Invoke(() =>
                {
                    SDL_Event evnt = new();
                    while (SDL_PollEvent(&evnt))
                    {
                        HandleSDLEvent(ref evnt);
                    }
                });

                waitHandle.Wait(WaitTimeMs);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            lock (_lock)
            {
                if (_isRunning)
                {
                    _refereceCount--;

                    if (_refereceCount == 0)
                    {
                        _isRunning = false;

                        _worker?.Join();

                        SDL_Quit();

                        OnJoyStickConnected = null;
                        OnJoystickDisconnected = null;
                    }
                }
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Dispose(true);
        }
    }
}
