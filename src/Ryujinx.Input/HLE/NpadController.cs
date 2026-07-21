using Ryujinx.Common;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Common.Configuration.Hid.Controller;
using Ryujinx.Common.Configuration.Hid.Controller.Motion;
using Ryujinx.Common.Configuration.Hid.Keyboard;
using Ryujinx.Common.Logging;
using Ryujinx.Common.Utilities;
using Ryujinx.HLE.HOS.Services.Hid;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CemuHookClient = Ryujinx.Input.Motion.CemuHook.Client;
using ConfigControllerType = Ryujinx.Common.Configuration.Hid.ControllerType;

namespace Ryujinx.Input.HLE
{
    public class NpadController : IDisposable
    {
        private const string KeyboardString = "keyboard";
        private const string ControllerString = "controller";

        private class HLEButtonMappingEntry
        {
            public readonly GamepadButtonInputId DriverInputId;
            public readonly ControllerKeys HLEInput;

            public HLEButtonMappingEntry(GamepadButtonInputId driverInputId, ControllerKeys hleInput)
            {
                DriverInputId = driverInputId;
                HLEInput = hleInput;
            }
        }

        private static readonly HLEButtonMappingEntry[] _hleButtonMapping =
        [
            new(GamepadButtonInputId.A, ControllerKeys.A),
            new(GamepadButtonInputId.B, ControllerKeys.B),
            new(GamepadButtonInputId.X, ControllerKeys.X),
            new(GamepadButtonInputId.Y, ControllerKeys.Y),
            new(GamepadButtonInputId.LeftStick, ControllerKeys.LStick),
            new(GamepadButtonInputId.RightStick, ControllerKeys.RStick),
            new(GamepadButtonInputId.LeftShoulder, ControllerKeys.L),
            new(GamepadButtonInputId.RightShoulder, ControllerKeys.R),
            new(GamepadButtonInputId.LeftTrigger, ControllerKeys.Zl),
            new(GamepadButtonInputId.RightTrigger, ControllerKeys.Zr),
            new(GamepadButtonInputId.DpadUp, ControllerKeys.DpadUp),
            new(GamepadButtonInputId.DpadDown, ControllerKeys.DpadDown),
            new(GamepadButtonInputId.DpadLeft, ControllerKeys.DpadLeft),
            new(GamepadButtonInputId.DpadRight, ControllerKeys.DpadRight),
            new(GamepadButtonInputId.Minus, ControllerKeys.Minus),
            new(GamepadButtonInputId.Plus, ControllerKeys.Plus),

            new(GamepadButtonInputId.SingleLeftTrigger0, ControllerKeys.SlLeft),
            new(GamepadButtonInputId.SingleRightTrigger0, ControllerKeys.SrLeft),
            new(GamepadButtonInputId.SingleLeftTrigger1, ControllerKeys.SlRight),
            new(GamepadButtonInputId.SingleRightTrigger1, ControllerKeys.SrRight)
        ];

        private class HLEKeyboardMappingEntry
        {
            public readonly Key TargetKey;
            public readonly byte Target;

            public HLEKeyboardMappingEntry(Key targetKey, byte target)
            {
                TargetKey = targetKey;
                Target = target;
            }
        }

        private static readonly HLEKeyboardMappingEntry[] _keyMapping =
        [
            new(Key.A, 0x4),
            new(Key.B, 0x5),
            new(Key.C, 0x6),
            new(Key.D, 0x7),
            new(Key.E, 0x8),
            new(Key.F, 0x9),
            new(Key.G, 0xA),
            new(Key.H, 0xB),
            new(Key.I, 0xC),
            new(Key.J, 0xD),
            new(Key.K, 0xE),
            new(Key.L, 0xF),
            new(Key.M, 0x10),
            new(Key.N, 0x11),
            new(Key.O, 0x12),
            new(Key.P, 0x13),
            new(Key.Q, 0x14),
            new(Key.R, 0x15),
            new(Key.S, 0x16),
            new(Key.T, 0x17),
            new(Key.U, 0x18),
            new(Key.V, 0x19),
            new(Key.W, 0x1A),
            new(Key.X, 0x1B),
            new(Key.Y, 0x1C),
            new(Key.Z, 0x1D),

            new(Key.Number1, 0x1E),
            new(Key.Number2, 0x1F),
            new(Key.Number3, 0x20),
            new(Key.Number4, 0x21),
            new(Key.Number5, 0x22),
            new(Key.Number6, 0x23),
            new(Key.Number7, 0x24),
            new(Key.Number8, 0x25),
            new(Key.Number9, 0x26),
            new(Key.Number0, 0x27),

            new(Key.Enter,        0x28),
            new(Key.Escape,       0x29),
            new(Key.BackSpace,    0x2A),
            new(Key.Tab,          0x2B),
            new(Key.Space,        0x2C),
            new(Key.Minus,        0x2D),
            new(Key.Plus,         0x2E),
            new(Key.BracketLeft,  0x2F),
            new(Key.BracketRight, 0x30),
            new(Key.BackSlash,    0x31),
            new(Key.Tilde,        0x32),
            new(Key.Semicolon,    0x33),
            new(Key.Quote,        0x34),
            new(Key.Grave,        0x35),
            new(Key.Comma,        0x36),
            new(Key.Period,       0x37),
            new(Key.Slash,        0x38),
            new(Key.CapsLock,     0x39),

            new(Key.F1,  0x3a),
            new(Key.F2,  0x3b),
            new(Key.F3,  0x3c),
            new(Key.F4,  0x3d),
            new(Key.F5,  0x3e),
            new(Key.F6,  0x3f),
            new(Key.F7,  0x40),
            new(Key.F8,  0x41),
            new(Key.F9,  0x42),
            new(Key.F10, 0x43),
            new(Key.F11, 0x44),
            new(Key.F12, 0x45),

            new(Key.PrintScreen, 0x46),
            new(Key.ScrollLock,  0x47),
            new(Key.Pause,       0x48),
            new(Key.Insert,      0x49),
            new(Key.Home,        0x4A),
            new(Key.PageUp,      0x4B),
            new(Key.Delete,      0x4C),
            new(Key.End,         0x4D),
            new(Key.PageDown,    0x4E),
            new(Key.Right,       0x4F),
            new(Key.Left,        0x50),
            new(Key.Down,        0x51),
            new(Key.Up,          0x52),

            new(Key.NumLock,        0x53),
            new(Key.KeypadDivide,   0x54),
            new(Key.KeypadMultiply, 0x55),
            new(Key.KeypadSubtract, 0x56),
            new(Key.KeypadAdd,      0x57),
            new(Key.KeypadEnter,    0x58),
            new(Key.Keypad1,        0x59),
            new(Key.Keypad2,        0x5A),
            new(Key.Keypad3,        0x5B),
            new(Key.Keypad4,        0x5C),
            new(Key.Keypad5,        0x5D),
            new(Key.Keypad6,        0x5E),
            new(Key.Keypad7,        0x5F),
            new(Key.Keypad8,        0x60),
            new(Key.Keypad9,        0x61),
            new(Key.Keypad0,        0x62),
            new(Key.KeypadDecimal,  0x63),

            new(Key.F13, 0x68),
            new(Key.F14, 0x69),
            new(Key.F15, 0x6A),
            new(Key.F16, 0x6B),
            new(Key.F17, 0x6C),
            new(Key.F18, 0x6D),
            new(Key.F19, 0x6E),
            new(Key.F20, 0x6F),
            new(Key.F21, 0x70),
            new(Key.F22, 0x71),
            new(Key.F23, 0x72),
            new(Key.F24, 0x73),

            new(Key.ControlLeft,  0xE0),
            new(Key.ShiftLeft,    0xE1),
            new(Key.AltLeft,      0xE2),
            new(Key.WinLeft,      0xE3),
            new(Key.ControlRight, 0xE4),
            new(Key.ShiftRight,   0xE5),
            new(Key.AltRight,     0xE6),
            new(Key.WinRight,     0xE7)
        ];

        private static readonly HLEKeyboardMappingEntry[] _keyModifierMapping =
        [
            new(Key.ControlLeft,  0),
            new(Key.ShiftLeft,    1),
            new(Key.AltLeft,      2),
            new(Key.WinLeft,      3),
            new(Key.ControlRight, 4),
            new(Key.ShiftRight,   5),
            new(Key.AltRight,     6),
            new(Key.WinRight,     7),
            new(Key.CapsLock,     8),
            new(Key.ScrollLock,   9),
            new(Key.NumLock,      10)
        ];

        private MotionInput _leftMotionInput;
        private MotionInput _rightMotionInput;

        private IGamepad _gamepad;
        private IGamepad _keyboardGamepad;
        private IGamepad _controllerGamepad;
        private readonly List<IGamepad> _assignedControllerGamepads = [];
        private readonly List<StandardControllerInputConfig> _assignedControllerConfigs = [];
        private InputConfig _config;
        private InputConfig _activeConfig;
        private StandardKeyboardInputConfig _keyboardConfig;
        private StandardControllerInputConfig _controllerConfig;
        private GamepadStateSnapshot _previousKeyboardState;
        private readonly List<GamepadStateSnapshot> _previousControllerStates = [];
        private DynamicInputSource _activeInputSource;
        private PlayerInputAssignment _playerInputAssignment;
        private bool _singleUsesKeyboardDriver;
        private IGamepadDriver _keyboardDriver;
        private IGamepadDriver _controllerDriver;
        private int _activeControllerIndex = -1;

        public IGamepadDriver GamepadDriver { get; private set; }
        public GamepadStateSnapshot State { get; private set; }
        public InputConfig ActiveConfig => _activeConfig;

        public string Id { get; private set; }

        public bool IsAvailable => _gamepad != null || _keyboardGamepad != null || _assignedControllerGamepads.Count > 0;

        private readonly CemuHookClient _cemuHookClient;
        private static readonly InputConfigJsonSerializerContext _serializerContext = new(JsonHelper.GetDefaultSerializerOptions());

        private enum DynamicInputSource
        {
            None,
            Keyboard,
            Controller,
        }

        public NpadController(CemuHookClient cemuHookClient)
        {
            State = default;
            Id = null;
            _cemuHookClient = cemuHookClient;
        }

        public bool MatchesDriverConfiguration(InputConfig config, PlayerInputAssignment playerInputAssignment)
        {
            if (_config?.EnableDynamicGamepadSwap != config.EnableDynamicGamepadSwap)
            {
                return false;
            }

            if (playerInputAssignment?.EnableDynamicInputSwap == true)
            {
                if (_playerInputAssignment == null || _playerInputAssignment.EnableDynamicInputSwap != playerInputAssignment.EnableDynamicInputSwap)
                {
                    return false;
                }

                return PlayerInputAssignmentHelper.AreEquivalent(_playerInputAssignment, playerInputAssignment);
            }

            return _singleUsesKeyboardDriver == (config is StandardKeyboardInputConfig) &&
                   Id == config.Id;
        }

        public bool UpdateDriverConfiguration(IGamepadDriver keyboardDriver, IGamepadDriver gamepadDriver, InputConfig config, PlayerInputAssignment playerInputAssignment)
        {
            _keyboardDriver = keyboardDriver;
            _controllerDriver = gamepadDriver;
            _playerInputAssignment = playerInputAssignment;

            DisposeOpenedGamepads();

            _gamepad = null;
            _keyboardGamepad = null;
            _controllerGamepad = null;
            _assignedControllerGamepads.Clear();
            _assignedControllerConfigs.Clear();
            _previousKeyboardState = default;
            _previousControllerStates.Clear();
            _activeInputSource = DynamicInputSource.None;
            _activeControllerIndex = -1;

            if (playerInputAssignment?.EnableDynamicInputSwap == true)
            {
                ConfigureDynamicGamepads(keyboardDriver, gamepadDriver, config);
            }
            else
            {
                _singleUsesKeyboardDriver = config is StandardKeyboardInputConfig;
                GamepadDriver = _singleUsesKeyboardDriver ? keyboardDriver : gamepadDriver;
                Id = config.Id;
                _gamepad = OpenSingleGamepad(GamepadDriver, config.Id, _singleUsesKeyboardDriver);
            }

            UpdateUserConfiguration(config);

            return IsAvailable;
        }

        public void UpdateUserConfiguration(InputConfig config)
        {
            InputConfig oldConfig = _config;

            if (_playerInputAssignment?.EnableDynamicInputSwap == true)
            {
                StandardControllerInputConfig oldControllerConfig = _controllerConfig;

                _config = config;
                UpdateDynamicConfigurations(config);

                if (_controllerConfig?.Motion == null)
                {
                    _leftMotionInput = null;
                    _rightMotionInput = null;
                }
                else if (NeedsMotionInputUpdate(oldControllerConfig, _controllerConfig))
                {
                    UpdateMotionInput(_controllerConfig.Motion);
                }

                if (_keyboardConfig != null)
                {
                    _keyboardGamepad?.SetConfiguration(_keyboardConfig);
                }

                for (int i = 0; i < _assignedControllerGamepads.Count; i++)
                {
                    StandardControllerInputConfig assignedControllerConfig = i < _assignedControllerConfigs.Count
                        ? _assignedControllerConfigs[i]
                        : _controllerConfig;

                    if (assignedControllerConfig != null)
                    {
                        _assignedControllerGamepads[i].SetConfiguration(assignedControllerConfig);
                    }
                }

                UpdateActiveGamepad();
                return;
            }

            _config = config;

            if (config is StandardControllerInputConfig controllerConfig)
            {
                if (controllerConfig.Motion == null)
                {
                    _leftMotionInput = null;
                    _rightMotionInput = null;
                }
                else if (NeedsMotionInputUpdate(oldConfig as StandardControllerInputConfig, controllerConfig))
                {
                    UpdateMotionInput(controllerConfig.Motion);
                }
            }
            else
            {
                // Non-controller doesn't have motions.
                _leftMotionInput = null;
                _rightMotionInput = null;
            }

            _activeConfig = config;

            _gamepad?.SetConfiguration(config);
        }

        private void UpdateMotionInput(MotionConfigController motionConfig)
        {
            if (motionConfig == null)
            {
                _leftMotionInput = null;
                _rightMotionInput = null;
                return;
            }

            if (motionConfig.MotionBackend != MotionInputBackendType.CemuHook)
            {
                _leftMotionInput = new MotionInput();
                _rightMotionInput = new MotionInput();
            }
            else
            {
                _leftMotionInput = null;
                _rightMotionInput = null;
            }
        }

        private bool NeedsMotionInputUpdate(StandardControllerInputConfig oldConfig, StandardControllerInputConfig newConfig)
        {
            if (newConfig?.Motion == null)
            {
                return false;
            }

            bool motionWasDisabled = oldConfig?.Motion == null;
            bool leftMotionMissing = _leftMotionInput == null;
            bool isJoyconPairNeedingRightMotion = newConfig.ControllerType == ConfigControllerType.JoyconPair && _rightMotionInput == null;
            bool motionEnabledChanged = !motionWasDisabled && oldConfig?.Motion?.EnableMotion != newConfig.Motion.EnableMotion;
            bool motionBackendChanged = !motionWasDisabled && oldConfig?.Motion?.MotionBackend != newConfig.Motion.MotionBackend;

            return motionWasDisabled ||
                   leftMotionMissing ||
                   isJoyconPairNeedingRightMotion ||
                   motionEnabledChanged ||
                   motionBackendChanged;
        }

        public void Update()
        {
            if (_playerInputAssignment?.EnableDynamicInputSwap == true)
            {
                UpdateDynamic();
                return;
            }

            // _gamepad may be altered by other threads
            IGamepad gamepad = _gamepad;

            if (gamepad != null && GamepadDriver != null)
            {
                State = gamepad.GetMappedStateSnapshot();
                _activeConfig = _config;

                if (_activeConfig is StandardControllerInputConfig controllerConfig && controllerConfig.Motion?.EnableMotion == true)
                {
                    UpdateControllerMotion(gamepad, controllerConfig);
                }
                else
                {
                    _leftMotionInput = null;
                    _rightMotionInput = null;
                }
            }
            else
            {
                // Reset states
                State = default;
                _leftMotionInput = null;
                _rightMotionInput = null;
            }
        }

        public GamepadInput GetHLEInputState()
        {
            GamepadInput state = new();

            // First update all buttons
            foreach (HLEButtonMappingEntry entry in _hleButtonMapping)
            {
                if (State.IsPressed(entry.DriverInputId))
                {
                    state.Buttons |= entry.HLEInput;
                }
            }

            if (_activeConfig is StandardKeyboardInputConfig)
            {
                (float leftAxisX, float leftAxisY) = State.GetStick(StickInputId.Left);
                (float rightAxisX, float rightAxisY) = State.GetStick(StickInputId.Right);

                state.LStick = new JoystickPosition
                {
                    Dx = ClampAxis(leftAxisX),
                    Dy = ClampAxis(leftAxisY),
                };

                state.RStick = new JoystickPosition
                {
                    Dx = ClampAxis(rightAxisX),
                    Dy = ClampAxis(rightAxisY),
                };
            }
            else if (_activeConfig is StandardControllerInputConfig controllerConfig)
            {
                (float leftAxisX, float leftAxisY) = State.GetStick(StickInputId.Left);
                (float rightAxisX, float rightAxisY) = State.GetStick(StickInputId.Right);

                state.LStick = ClampToCircle(ApplyDeadzone(leftAxisX, leftAxisY, controllerConfig.DeadzoneLeft), controllerConfig.RangeLeft);
                state.RStick = ClampToCircle(ApplyDeadzone(rightAxisX, rightAxisY, controllerConfig.DeadzoneRight), controllerConfig.RangeRight);
            }

            return state;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static JoystickPosition ApplyDeadzone(float x, float y, float deadzone)
        {
            float magnitudeClamped = Math.Min(MathF.Sqrt(x * x + y * y), 1f);

            if (magnitudeClamped <= deadzone)
            {
                return new JoystickPosition { Dx = 0, Dy = 0 };
            }

            return new JoystickPosition
            {
                Dx = ClampAxis((x / magnitudeClamped) * ((magnitudeClamped - deadzone) / (1 - deadzone))),
                Dy = ClampAxis((y / magnitudeClamped) * ((magnitudeClamped - deadzone) / (1 - deadzone))),
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static short ClampAxis(float value)
        {
            if (Math.Sign(value) < 0)
            {
                return (short)Math.Max(value * -short.MinValue, short.MinValue);
            }

            return (short)Math.Min(value * short.MaxValue, short.MaxValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static JoystickPosition ClampToCircle(JoystickPosition position, float range)
        {
            Vector2 point = new Vector2(position.Dx, position.Dy) * range;

            if (point.Length() > short.MaxValue)
            {
                point = point / point.Length() * short.MaxValue;
            }

            return new JoystickPosition
            {
                Dx = (int)point.X,
                Dy = (int)point.Y,
            };
        }

        public SixAxisInput GetHLEMotionState(bool isJoyconRightPair = false)
        {
            float[] orientationForHLE = new float[9];
            Vector3 gyroscope;
            Vector3 accelerometer;
            Vector3 rotation;

            MotionInput motionInput = _leftMotionInput;

            if (isJoyconRightPair)
            {
                if (_rightMotionInput == null)
                {
                    return default;
                }

                motionInput = _rightMotionInput;
            }

            if (motionInput != null)
            {
                gyroscope = Truncate(motionInput.Gyroscrope * 0.0027f, 3);
                accelerometer = Truncate(motionInput.Accelerometer, 3);
                rotation = Truncate(motionInput.Rotation * 0.0027f, 3);

                Matrix4x4 orientation = motionInput.GetOrientation();

                orientationForHLE[0] = Math.Clamp(orientation.M11, -1f, 1f);
                orientationForHLE[1] = Math.Clamp(orientation.M12, -1f, 1f);
                orientationForHLE[2] = Math.Clamp(orientation.M13, -1f, 1f);
                orientationForHLE[3] = Math.Clamp(orientation.M21, -1f, 1f);
                orientationForHLE[4] = Math.Clamp(orientation.M22, -1f, 1f);
                orientationForHLE[5] = Math.Clamp(orientation.M23, -1f, 1f);
                orientationForHLE[6] = Math.Clamp(orientation.M31, -1f, 1f);
                orientationForHLE[7] = Math.Clamp(orientation.M32, -1f, 1f);
                orientationForHLE[8] = Math.Clamp(orientation.M33, -1f, 1f);
            }
            else
            {
                gyroscope = new Vector3();
                accelerometer = new Vector3();
                rotation = new Vector3();
            }

            return new SixAxisInput
            {
                Accelerometer = accelerometer,
                Gyroscope = gyroscope,
                Rotation = rotation,
                Orientation = orientationForHLE,
            };
        }

        private static Vector3 Truncate(Vector3 value, int decimals)
        {
            float power = MathF.Pow(10, decimals);

            value.X = float.IsNegative(value.X) ? MathF.Ceiling(value.X * power) / power : MathF.Floor(value.X * power) / power;
            value.Y = float.IsNegative(value.Y) ? MathF.Ceiling(value.Y * power) / power : MathF.Floor(value.Y * power) / power;
            value.Z = float.IsNegative(value.Z) ? MathF.Ceiling(value.Z * power) / power : MathF.Floor(value.Z * power) / power;

            return value;
        }

        public static KeyboardInput GetHLEKeyboardInput(IGamepadDriver keyboardDriver)
        {
            if (keyboardDriver.GetGamepad("0") is not IKeyboard keyboard)
            {
                return default;
            }

            KeyboardStateSnapshot keyboardState = keyboard.GetKeyboardStateSnapshot();

            KeyboardInput hidKeyboard = new()
            {
                Modifier = 0,
                Keys = new ulong[0x4],
            };

            foreach (HLEKeyboardMappingEntry entry in _keyMapping)
            {
                ulong value = keyboardState.IsPressed(entry.TargetKey) ? 1UL : 0UL;

                hidKeyboard.Keys[entry.Target / 0x40] |= (value << (entry.Target % 0x40));
            }

            foreach (HLEKeyboardMappingEntry entry in _keyModifierMapping)
            {
                int value = keyboardState.IsPressed(entry.TargetKey) ? 1 : 0;

                hidKeyboard.Modifier |= value << entry.Target;
            }

            return hidKeyboard;

        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeOpenedGamepads();
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Dispose(true);
        }

        public void UpdateRumble(ConcurrentQueue<(VibrationValue, VibrationValue)> queue)
        {
            if (queue.TryDequeue(out (VibrationValue, VibrationValue) dualVibrationValue))
            {
                if (_controllerConfig is StandardControllerInputConfig dynamicControllerConfig &&
                    _playerInputAssignment?.EnableDynamicInputSwap == true &&
                    dynamicControllerConfig.Rumble?.EnableRumble == true)
                {
                    ApplyRumble(_controllerGamepad ?? _assignedControllerGamepads.FirstOrDefault(), dynamicControllerConfig, dualVibrationValue);
                }
                else if (_config is StandardControllerInputConfig controllerConfig && controllerConfig.Rumble?.EnableRumble == true)
                {
                    ApplyRumble(_gamepad, controllerConfig, dualVibrationValue);
                }
            }
        }

        public bool HasAssignedControllerId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            if (_playerInputAssignment?.EnableDynamicInputSwap == true)
            {
                return _assignedControllerGamepads.Any(gamepad => gamepad?.Id == id);
            }

            return Id == id;
        }

        private void ApplyRumble(IGamepad gamepad, StandardControllerInputConfig controllerConfig, (VibrationValue, VibrationValue) dualVibrationValue)
        {
            if (gamepad == null)
            {
                return;
            }

            VibrationValue leftVibrationValue = dualVibrationValue.Item1;
            VibrationValue rightVibrationValue = dualVibrationValue.Item2;
            
            float low = Math.Min(1f, (float)((rightVibrationValue.AmplitudeLow * 0.85 + rightVibrationValue.AmplitudeHigh * 0.15) * controllerConfig.Rumble.StrongRumble));
            float high = Math.Min(1f, (float)((leftVibrationValue.AmplitudeLow * 0.15 + leftVibrationValue.AmplitudeHigh * 0.85) * controllerConfig.Rumble.WeakRumble));

            leftVibrationValue.AmplitudeLow *= controllerConfig.Rumble.WeakRumble;
            leftVibrationValue.AmplitudeHigh *= controllerConfig.Rumble.StrongRumble;
            rightVibrationValue.AmplitudeLow *= controllerConfig.Rumble.WeakRumble;
            rightVibrationValue.AmplitudeHigh *= controllerConfig.Rumble.StrongRumble;

            if (!controllerConfig.Rumble.UseHDRumble || !gamepad.HDRumble(leftVibrationValue, rightVibrationValue))
            {
                gamepad.Rumble(low, high, 0xFFFFFFFF);
            }

            Logger.Debug?.Print(LogClass.Hid, $"Effect for {controllerConfig.PlayerIndex} " +
                // Value=value/multiplier * multiplier (result)
                $"L.low.amp={leftVibrationValue.AmplitudeLow / controllerConfig.Rumble.WeakRumble} * {controllerConfig.Rumble.WeakRumble} ({leftVibrationValue.AmplitudeLow}), " +
                $"L.high.amp={leftVibrationValue.AmplitudeHigh / controllerConfig.Rumble.WeakRumble} * {controllerConfig.Rumble.WeakRumble} ({leftVibrationValue.AmplitudeHigh}), " +
                $"L.low.freq={leftVibrationValue.FrequencyLow / controllerConfig.Rumble.WeakRumble} * {controllerConfig.Rumble.WeakRumble} ({leftVibrationValue.FrequencyLow}), " +
                $"L.high.freq={leftVibrationValue.FrequencyHigh / controllerConfig.Rumble.WeakRumble} * {controllerConfig.Rumble.WeakRumble} ({leftVibrationValue.FrequencyHigh}), " +
                $"R.low.amp={rightVibrationValue.AmplitudeLow / controllerConfig.Rumble.StrongRumble} * {controllerConfig.Rumble.StrongRumble} ({rightVibrationValue.AmplitudeLow}), " +
                $"R.high.amp={rightVibrationValue.AmplitudeHigh / controllerConfig.Rumble.StrongRumble} * {controllerConfig.Rumble.StrongRumble} ({rightVibrationValue.AmplitudeHigh}), " +
                $"R.low.freq={rightVibrationValue.FrequencyLow / controllerConfig.Rumble.StrongRumble} * {controllerConfig.Rumble.StrongRumble} ({rightVibrationValue.FrequencyLow}), " +
                $"R.high.freq={rightVibrationValue.FrequencyHigh / controllerConfig.Rumble.StrongRumble} * {controllerConfig.Rumble.StrongRumble} ({rightVibrationValue.FrequencyHigh})");
        }

        private void ConfigureDynamicGamepads(IGamepadDriver keyboardDriver, IGamepadDriver gamepadDriver, InputConfig config)
        {
            AssignedInputDevice assignedKeyboard = _playerInputAssignment?.Devices.FirstOrDefault(device => device.Type == AssignedInputDeviceType.Keyboard);

            if (!string.IsNullOrEmpty(assignedKeyboard?.Id))
            {
                _keyboardGamepad = OpenSingleGamepad(keyboardDriver, assignedKeyboard.Id, true);
            }

            foreach (AssignedInputDevice assignedController in ResolveDynamicControllerAssignments(gamepadDriver, config))
            {
                IGamepad controllerGamepad = OpenSingleGamepad(gamepadDriver, assignedController.Id, false);

                if (controllerGamepad != null)
                {
                    _assignedControllerGamepads.Add(controllerGamepad);
                    _assignedControllerConfigs.Add(null);
                    _previousControllerStates.Add(default);
                }
            }

            _controllerGamepad = _assignedControllerGamepads.FirstOrDefault();
            GamepadDriver = null;
            Id = _assignedControllerGamepads.FirstOrDefault()?.Id ?? config.Id;
        }

        private IEnumerable<AssignedInputDevice> ResolveDynamicControllerAssignments(IGamepadDriver gamepadDriver, InputConfig config)
        {
            if (gamepadDriver == null)
            {
                yield break;
            }

            List<AssignedInputDevice> assignedControllers = _playerInputAssignment?.Devices
                .Where(device => device.Type == AssignedInputDeviceType.Controller)
                .ToList() ?? [];

            if (_playerInputAssignment?.EnableDynamicInputSwap == true)
            {
                foreach (AssignedInputDevice assignedController in assignedControllers)
                {
                    for (int index = 0; index < gamepadDriver.GamepadsIds.Length; index++)
                    {
                        string gamepadId = gamepadDriver.GamepadsIds[index];
                        if (string.Equals(gamepadId, assignedController.Id, StringComparison.Ordinal))
                        {
                            yield return assignedController;
                            break;
                        }
                    }
                }

                yield break;
            }

            if (config is StandardControllerInputConfig)
            {
                for (int index = 0; index < gamepadDriver.GamepadsIds.Length; index++)
                {
                    string gamepadId = gamepadDriver.GamepadsIds[index];
                    if (string.Equals(gamepadId, config.Id, StringComparison.Ordinal))
                    {
                        yield return new AssignedInputDevice
                        {
                            Type = AssignedInputDeviceType.Controller, Id = gamepadId,
                        };
                        yield break;
                    }
                }
            }

            if (!gamepadDriver.GamepadsIds.IsEmpty)
            {
                yield return new AssignedInputDevice
                {
                    Type = AssignedInputDeviceType.Controller,
                    Id = gamepadDriver.GamepadsIds[0],
                };
            }
        }

        private static IGamepad OpenSingleGamepad(IGamepadDriver driver, string id, bool keyboard)
        {
            if (driver == null || string.IsNullOrEmpty(id))
            {
                return null;
            }

            if (keyboard && driver is IKeyboardModeDriver keyboardModeDriver)
            {
                return keyboardModeDriver.GetKeyboard(id, KeyboardInputMode.Physical);
            }

            return driver.GetGamepad(id);
        }

        private void UpdateDynamicConfigurations(InputConfig config)
        {
            if (config is StandardKeyboardInputConfig keyboardConfig)
            {
                AssignedInputDevice assignedKeyboard = _playerInputAssignment?.Devices.FirstOrDefault(device => device.Type == AssignedInputDeviceType.Keyboard);

                _keyboardConfig = ResolveKeyboardConfiguration(assignedKeyboard, keyboardConfig, _keyboardGamepad);

                _assignedControllerConfigs.Clear();

                foreach (IGamepad controllerGamepad in _assignedControllerGamepads)
                {
                    AssignedInputDevice assignedController = _playerInputAssignment?.Devices.FirstOrDefault(device =>
                        device.Type == AssignedInputDeviceType.Controller &&
                        device.Id == controllerGamepad.Id);

                    _assignedControllerConfigs.Add(ResolveControllerConfiguration(assignedController, keyboardConfig, controllerGamepad));
                }

                _controllerConfig = _assignedControllerConfigs.FirstOrDefault();
            }
            else if (config is StandardControllerInputConfig controllerConfig)
            {
                _assignedControllerConfigs.Clear();

                foreach (IGamepad controllerGamepad in _assignedControllerGamepads)
                {
                    AssignedInputDevice assignedController = _playerInputAssignment?.Devices.FirstOrDefault(device =>
                        device.Type == AssignedInputDeviceType.Controller &&
                        device.Id == controllerGamepad.Id);

                    _assignedControllerConfigs.Add(ResolveControllerConfiguration(assignedController, controllerConfig, controllerGamepad));
                }

                _controllerConfig = _assignedControllerConfigs.FirstOrDefault() ?? controllerConfig;

                if (_keyboardGamepad != null)
                {
                    AssignedInputDevice assignedKeyboard = _playerInputAssignment?.Devices.FirstOrDefault(device => device.Type == AssignedInputDeviceType.Keyboard);

                    _keyboardConfig = ResolveKeyboardConfiguration(assignedKeyboard, controllerConfig, _keyboardGamepad);
                }
                else
                {
                    _keyboardConfig = null;
                }
            }
        }

        private StandardKeyboardInputConfig ResolveKeyboardConfiguration(AssignedInputDevice assignedKeyboard, InputConfig baseConfig, IGamepad keyboardGamepad)
        {
            if (keyboardGamepad == null)
            {
                return null;
            }

            if (TryLoadAssignedProfile<StandardKeyboardInputConfig>(assignedKeyboard, KeyboardString, keyboardGamepad, baseConfig, out StandardKeyboardInputConfig profileConfig))
            {
                return profileConfig;
            }

            if (baseConfig is StandardKeyboardInputConfig keyboardBaseConfig)
            {
                StandardKeyboardInputConfig clonedConfig = CloneConfig(keyboardBaseConfig);

                if (clonedConfig != null)
                {
                    clonedConfig.Id = keyboardGamepad.Id;
                    clonedConfig.Name = keyboardGamepad.Name;
                    clonedConfig.PlayerIndex = baseConfig.PlayerIndex;
                    clonedConfig.EnableDynamicGamepadSwap = true;
                    return clonedConfig;
                }
            }

            StandardKeyboardInputConfig defaultConfig = InputConfigDefaults.CreateDefaultKeyboardConfiguration(
                keyboardGamepad.Id,
                keyboardGamepad.Name,
                baseConfig.ControllerType,
                baseConfig.PlayerIndex);
            defaultConfig.EnableDynamicGamepadSwap = true;
            return defaultConfig;
        }

        private StandardControllerInputConfig ResolveControllerConfiguration(AssignedInputDevice assignedController, InputConfig baseConfig, IGamepad controllerGamepad)
        {
            if (controllerGamepad == null)
            {
                return null;
            }

            if (TryLoadAssignedProfile<StandardControllerInputConfig>(assignedController, ControllerString, controllerGamepad, baseConfig, out StandardControllerInputConfig profileConfig))
            {
                return profileConfig;
            }

            if (baseConfig is StandardControllerInputConfig controllerBaseConfig)
            {
                StandardControllerInputConfig clonedConfig = CloneConfig(controllerBaseConfig);

                if (clonedConfig != null)
                {
                    clonedConfig.Id = controllerGamepad.Id;
                    clonedConfig.Name = controllerGamepad.Name;
                    clonedConfig.PlayerIndex = baseConfig.PlayerIndex;
                    clonedConfig.EnableDynamicGamepadSwap = true;
                    return clonedConfig;
                }
            }

            StandardControllerInputConfig defaultConfig = InputConfigDefaults.CreateDefaultControllerConfiguration(
                controllerGamepad.Id,
                controllerGamepad.Name,
                baseConfig.ControllerType,
                baseConfig.PlayerIndex,
                controllerGamepad.Name?.Contains("Nintendo") == true);
            defaultConfig.EnableDynamicGamepadSwap = true;
            return defaultConfig;
        }

        private static T CloneConfig<T>(T config) where T : InputConfig
        {
            return JsonHelper.Deserialize(
                JsonHelper.Serialize(config, _serializerContext.InputConfig),
                _serializerContext.InputConfig) as T;
        }

        private static bool TryLoadAssignedProfile<T>(AssignedInputDevice assignedDevice, string profileDirectory, IGamepad gamepad, InputConfig baseConfig, out T config)
            where T : InputConfig
        {
            config = null;

            if (string.IsNullOrWhiteSpace(assignedDevice?.ProfileName))
            {
                return false;
            }

            string path = Path.Combine(AppDataManager.ProfilesDirPath, profileDirectory, assignedDevice.ProfileName + ".json");

            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                config = JsonHelper.DeserializeFromFile(path, _serializerContext.InputConfig) as T;
            }
            catch (JsonException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            if (config == null)
            {
                return false;
            }

            config.Id = gamepad.Id;
            config.Name = gamepad.Name;
            config.PlayerIndex = baseConfig.PlayerIndex;
            config.EnableDynamicGamepadSwap = true;
            return true;
        }

        private void UpdateDynamic()
        {
            GamepadStateSnapshot keyboardState = _keyboardGamepad?.GetMappedStateSnapshot() ?? default;
            bool keyboardHasInput = _keyboardGamepad != null && HasInput(keyboardState);
            bool keyboardNewInput = _keyboardGamepad != null && HasNewInput(keyboardState, _previousKeyboardState);
            int controllerWithNewInput = -1;
            int controllerWithHeldInput = -1;

            // Note: dynamic swap is "last input wins", so we scan every assigned controller
            // and promote whichever one most recently produced a meaningful state change.
            for (int i = 0; i < _assignedControllerGamepads.Count; i++)
            {
                IGamepad controllerGamepad = _assignedControllerGamepads[i];
                GamepadStateSnapshot controllerState = controllerGamepad?.GetMappedStateSnapshot() ?? default;

                if (HasNewInput(controllerState, _previousControllerStates[i]))
                {
                    controllerWithNewInput = i;
                }

                if (controllerWithHeldInput == -1 && HasInput(controllerState))
                {
                    controllerWithHeldInput = i;
                }

                _previousControllerStates[i] = controllerState;
            }

            if (keyboardNewInput && controllerWithNewInput == -1)
            {
                _activeInputSource = DynamicInputSource.Keyboard;
            }
            else if (controllerWithNewInput != -1 && !keyboardNewInput)
            {
                _activeInputSource = DynamicInputSource.Controller;
                _activeControllerIndex = controllerWithNewInput;
            }
            else if (_activeInputSource == DynamicInputSource.Keyboard && !keyboardHasInput && controllerWithHeldInput != -1)
            {
                _activeInputSource = DynamicInputSource.Controller;
                _activeControllerIndex = controllerWithHeldInput;
            }
            else if (_activeInputSource == DynamicInputSource.Controller && controllerWithHeldInput == -1 && keyboardHasInput)
            {
                _activeInputSource = DynamicInputSource.Keyboard;
            }
            else if (_activeInputSource == DynamicInputSource.None)
            {
                _activeInputSource = _config switch
                {
                    StandardKeyboardInputConfig when _keyboardGamepad != null => DynamicInputSource.Keyboard,
                    StandardControllerInputConfig when _assignedControllerGamepads.Count > 0 => DynamicInputSource.Controller,
                    _ when keyboardHasInput => DynamicInputSource.Keyboard,
                    _ when controllerWithHeldInput != -1 => DynamicInputSource.Controller,
                    _ when _keyboardGamepad != null => DynamicInputSource.Keyboard,
                    _ when _assignedControllerGamepads.Count > 0 => DynamicInputSource.Controller,
                    _ => DynamicInputSource.None,
                };

                if (_activeInputSource == DynamicInputSource.Controller)
                {
                    _activeControllerIndex = controllerWithHeldInput != -1 ? controllerWithHeldInput : 0;
                }
            }

            UpdateActiveGamepad();

            State = _activeInputSource switch
            {
                DynamicInputSource.Keyboard => keyboardState,
                DynamicInputSource.Controller when _activeControllerIndex >= 0 && _activeControllerIndex < _previousControllerStates.Count => _previousControllerStates[_activeControllerIndex],
                _ => default,
            };

            if (_activeConfig is StandardControllerInputConfig controllerConfig && _controllerGamepad != null && _activeInputSource == DynamicInputSource.Controller)
            {
                UpdateControllerMotion(_controllerGamepad, controllerConfig);
            }
            else
            {
                _leftMotionInput = null;
                _rightMotionInput = null;
            }

            _previousKeyboardState = keyboardState;
        }

        private void UpdateActiveGamepad()
        {
            (_gamepad, _activeConfig, GamepadDriver) = _activeInputSource switch
            {
                DynamicInputSource.Keyboard => (_keyboardGamepad, _keyboardConfig, _keyboardDriver),
                DynamicInputSource.Controller =>
                (
                    _activeControllerIndex >= 0 && _activeControllerIndex < _assignedControllerGamepads.Count
                        ? _assignedControllerGamepads[_activeControllerIndex]
                        : _assignedControllerGamepads.FirstOrDefault(),
                    _activeControllerIndex >= 0 && _activeControllerIndex < _assignedControllerConfigs.Count
                        ? _assignedControllerConfigs[_activeControllerIndex]
                        : _assignedControllerConfigs.FirstOrDefault(),
                    _controllerDriver
                ),
                _ => ((IGamepad)null, (InputConfig)null, (IGamepadDriver)null)
            };

            _controllerGamepad = _gamepad;
        }

        private void UpdateControllerMotion(IGamepad gamepad, StandardControllerInputConfig controllerConfig)
        {
            if (gamepad == null || controllerConfig?.Motion == null || !controllerConfig.Motion.EnableMotion)
            {
                _leftMotionInput = null;
                _rightMotionInput = null;
                return;
            }

            if (controllerConfig.Motion.MotionBackend == MotionInputBackendType.GamepadDriver)
            {
                if ((gamepad.Features & GamepadFeaturesFlag.Motion) != 0)
                {
                    _leftMotionInput ??= new MotionInput();
                    _rightMotionInput ??= new MotionInput();

                    Vector3 accelerometer = gamepad.GetMotionData(MotionInputId.Accelerometer);
                    Vector3 gyroscope = gamepad.GetMotionData(MotionInputId.Gyroscope);

                    accelerometer = new Vector3(accelerometer.X, -accelerometer.Z, accelerometer.Y);
                    gyroscope = new Vector3(gyroscope.X, -gyroscope.Z, gyroscope.Y);

                    _leftMotionInput.Update(accelerometer, gyroscope, (ulong)PerformanceCounter.ElapsedNanoseconds / 1000, controllerConfig.Motion.Sensitivity, (float)controllerConfig.Motion.GyroDeadzone);

                    if (controllerConfig.ControllerType == ConfigControllerType.JoyconPair)
                    {
                        if (gamepad.Id == "JoyConPair")
                        {
                            Vector3 rightAccelerometer = gamepad.GetMotionData(MotionInputId.SecondAccelerometer);
                            Vector3 rightGyroscope = gamepad.GetMotionData(MotionInputId.SecondGyroscope);

                            rightAccelerometer = new Vector3(rightAccelerometer.X, -rightAccelerometer.Z, rightAccelerometer.Y);
                            rightGyroscope = new Vector3(rightGyroscope.X, -rightGyroscope.Z, rightGyroscope.Y);

                            _rightMotionInput.Update(rightAccelerometer, rightGyroscope, (ulong)PerformanceCounter.ElapsedNanoseconds / 1000, controllerConfig.Motion.Sensitivity, (float)controllerConfig.Motion.GyroDeadzone);
                        }
                        else
                        {
                            _rightMotionInput = _leftMotionInput;
                        }
                    }
                }
                else
                {
                    _leftMotionInput = null;
                    _rightMotionInput = null;
                }
            }
            else if (controllerConfig.Motion.MotionBackend == MotionInputBackendType.CemuHook && controllerConfig.Motion is CemuHookMotionConfigController cemuControllerConfig)
            {
                int clientId = (int)controllerConfig.PlayerIndex;

                _cemuHookClient.RegisterClient(clientId, cemuControllerConfig.DsuServerHost, cemuControllerConfig.DsuServerPort);
                _cemuHookClient.RequestData(clientId, cemuControllerConfig.Slot);
                _cemuHookClient.TryGetData(clientId, cemuControllerConfig.Slot, out _leftMotionInput);

                if (controllerConfig.ControllerType == ConfigControllerType.JoyconPair)
                {
                    if (!cemuControllerConfig.MirrorInput)
                    {
                        _cemuHookClient.RequestData(clientId, cemuControllerConfig.AltSlot);
                        _cemuHookClient.TryGetData(clientId, cemuControllerConfig.AltSlot, out _rightMotionInput);
                    }
                    else
                    {
                        _rightMotionInput = _leftMotionInput;
                    }
                }
            }
        }

        private static bool HasInput(GamepadStateSnapshot state)
        {
            for (GamepadButtonInputId inputId = GamepadButtonInputId.A; inputId < GamepadButtonInputId.Count; inputId++)
            {
                if (state.IsPressed(inputId))
                {
                    return true;
                }
            }

            return StickIsActive(state.GetStick(StickInputId.Left)) || StickIsActive(state.GetStick(StickInputId.Right));
        }

        private static bool HasNewInput(GamepadStateSnapshot current, GamepadStateSnapshot previous)
        {
            for (GamepadButtonInputId inputId = GamepadButtonInputId.A; inputId < GamepadButtonInputId.Count; inputId++)
            {
                if (current.IsPressed(inputId) && !previous.IsPressed(inputId))
                {
                    return true;
                }
            }

            return StickBecameActive(current.GetStick(StickInputId.Left), previous.GetStick(StickInputId.Left)) ||
                   StickBecameActive(current.GetStick(StickInputId.Right), previous.GetStick(StickInputId.Right));
        }

        private static bool StickIsActive((float X, float Y) stick)
        {
            const float Threshold = 0.2f;

            return MathF.Abs(stick.X) > Threshold || MathF.Abs(stick.Y) > Threshold;
        }

        private static bool StickBecameActive((float X, float Y) current, (float X, float Y) previous)
        {
            bool currentActive = StickIsActive(current);
            bool previousActive = StickIsActive(previous);

            return currentActive && (!previousActive || MathF.Abs(current.X - previous.X) > 0.1f || MathF.Abs(current.Y - previous.Y) > 0.1f);
        }

        private void DisposeOpenedGamepads()
        {
            if (!ReferenceEquals(_gamepad, _keyboardGamepad) && !_assignedControllerGamepads.Contains(_gamepad))
            {
                _gamepad?.Dispose();
            }

            _keyboardGamepad?.Dispose();

            foreach (IGamepad controllerGamepad in _assignedControllerGamepads.Distinct())
            {
                controllerGamepad?.Dispose();
            }
        }
    }
}
