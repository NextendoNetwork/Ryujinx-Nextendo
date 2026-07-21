using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Common.Configuration.Hid.Controller;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Hid;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using SDL;
using static SDL.SDL3;

namespace Ryujinx.Input.SDL3
{
    internal unsafe class SDL3JoyCon : IGamepad
    {
        private bool HasConfiguration => _configuration != null;

        private readonly record struct ButtonMappingEntry(GamepadButtonInputId To, GamepadButtonInputId From)
        {
            public bool IsValid => To is not GamepadButtonInputId.Unbound && From is not GamepadButtonInputId.Unbound;
        }

        private StandardControllerInputConfig _configuration;

        private readonly Dictionary<GamepadButtonInputId, SDL_GamepadButton> _leftButtonsDriverMapping = new()
        {
             {GamepadButtonInputId.LeftStick, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK},
             {GamepadButtonInputId.DpadUp, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST},
             {GamepadButtonInputId.DpadDown, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST},
             {GamepadButtonInputId.DpadLeft, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH},
             {GamepadButtonInputId.DpadRight, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH},
             {GamepadButtonInputId.Minus, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START},
             {GamepadButtonInputId.LeftShoulder, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_PADDLE1},
             {GamepadButtonInputId.LeftTrigger, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_PADDLE2},
             {GamepadButtonInputId.SingleRightTrigger0, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER},
             {GamepadButtonInputId.SingleLeftTrigger0, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER},
        };
        private readonly Dictionary<GamepadButtonInputId, SDL_GamepadButton> _rightButtonsDriverMapping = new()
        {
             {GamepadButtonInputId.RightStick, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK},
             {GamepadButtonInputId.A, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH},
             {GamepadButtonInputId.B, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST},
             {GamepadButtonInputId.X, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST},
             {GamepadButtonInputId.Y, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH},
             {GamepadButtonInputId.Plus, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START},
             {GamepadButtonInputId.RightShoulder, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_PADDLE1},
             {GamepadButtonInputId.RightTrigger, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_PADDLE2},
             {GamepadButtonInputId.SingleRightTrigger1, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER},
             {GamepadButtonInputId.SingleLeftTrigger1, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER}
        };

        private readonly Dictionary<GamepadButtonInputId, SDL_GamepadButton> _buttonsDriverMapping;
        private readonly Lock _userMappingLock = new();

        private readonly List<ButtonMappingEntry> _buttonsUserMapping;

        private readonly StickInputId[] _stickUserMapping = new StickInputId[(int)StickInputId.Count]
        {
            StickInputId.Unbound, StickInputId.Left, StickInputId.Right,
        };

        public GamepadFeaturesFlag Features { get; }

        private SDL_Gamepad* _gamepadHandle;
        
        private NpadHdRumble _hdRumble;

        private enum JoyConType
        {
            Left, Right
        }

        public const string Prefix = "Nintendo Switch Joy-Con";
        public const string LeftName = "Nintendo Switch Joy-Con (L)";
        public const string RightName = "Nintendo Switch Joy-Con (R)";

        private readonly JoyConType _joyConType;

        public SDL3JoyCon(SDL_Gamepad* gamepadHandle, string driverId)
        {
            _gamepadHandle = gamepadHandle;
            _hdRumble = NpadHdRumble.Create(gamepadHandle);
            _buttonsUserMapping = new List<ButtonMappingEntry>(10);

            Name = SDL_GetGamepadName(_gamepadHandle);
            Id = driverId;
            Features = GetFeaturesFlag();

            // Enable motion tracking
            if ((Features & GamepadFeaturesFlag.Motion) != 0)
            {
                if (!SDL_SetGamepadSensorEnabled(_gamepadHandle, SDL_SensorType.SDL_SENSOR_ACCEL, true))
                {
                    Logger.Error?.Print(LogClass.Hid,
                        $"Could not enable data reporting for SensorType {SDL_SensorType.SDL_SENSOR_ACCEL}.");
                }

                if (!SDL_SetGamepadSensorEnabled(_gamepadHandle, SDL_SensorType.SDL_SENSOR_GYRO, true))
                {
                    Logger.Error?.Print(LogClass.Hid,
                        $"Could not enable data reporting for SensorType {SDL_SensorType.SDL_SENSOR_GYRO}.");
                }
            }

            switch (Name)
            {
                case LeftName:
                    {
                        _buttonsDriverMapping = _leftButtonsDriverMapping;
                        _joyConType = JoyConType.Left;
                        break;
                    }
                case RightName:
                    {
                        _buttonsDriverMapping = _rightButtonsDriverMapping;
                        _joyConType = JoyConType.Right;
                        break;
                    }
            }
        }

        private GamepadFeaturesFlag GetFeaturesFlag()
        {
            GamepadFeaturesFlag result = GamepadFeaturesFlag.None;

            if (SDL_GamepadHasSensor(_gamepadHandle, SDL_SensorType.SDL_SENSOR_ACCEL) &&
                SDL_GamepadHasSensor(_gamepadHandle, SDL_SensorType.SDL_SENSOR_GYRO))
            {
                result |= GamepadFeaturesFlag.Motion;
            }

            if (SDL_RumbleGamepad(_gamepadHandle, 0, 0, 100))
            {
                result |= GamepadFeaturesFlag.Rumble;
            }

            return result;
        }

        public string Id { get; }
        public string Name { get; }
        public bool IsConnected => SDL_GamepadConnected(_gamepadHandle);

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && _hdRumble != null)
            {
                _hdRumble.Dispose();
            }
            if (disposing && _gamepadHandle != null)
            {
                SDL_CloseGamepad(_gamepadHandle);

                _gamepadHandle = null;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        public void SetTriggerThreshold(float triggerThreshold)
        {
            // No operations
        }
        
        public bool HDRumble(VibrationValue left, VibrationValue right)
        {
            return _hdRumble?.HdRumble(left, right) ?? false;
        }

        public bool Rumble(float lowFrequency, float highFrequency, uint durationMs)
        {
            if ((Features & GamepadFeaturesFlag.Rumble) == 0)
            {
                return false;
            }

            ushort lowFrequencyRaw = (ushort)(lowFrequency * ushort.MaxValue);
            ushort highFrequencyRaw = (ushort)(highFrequency * ushort.MaxValue);

            if (durationMs == uint.MaxValue)
            {
                if (!SDL_RumbleGamepad(_gamepadHandle, lowFrequencyRaw, highFrequencyRaw, SDL_HAPTIC_INFINITY))
                    Logger.Error?.Print(LogClass.Hid, "Rumble is not supported on this game controller.");
            }
            else if (durationMs > SDL_HAPTIC_INFINITY)
            {
                Logger.Error?.Print(LogClass.Hid, $"Unsupported rumble duration {durationMs}");
            }
            else
            {
                if (!SDL_RumbleGamepad(_gamepadHandle, lowFrequencyRaw, highFrequencyRaw, durationMs))
                    Logger.Error?.Print(LogClass.Hid, "Rumble is not supported on this game controller.");
            }
            
            if (!String.IsNullOrEmpty(SDL_GetError()))
            {
                Logger.Error?.PrintMsg(LogClass.Hid, SDL_GetError());
                SDL_ClearError();
                return false;
            }

            return true;
        }

        public Vector3 GetMotionData(MotionInputId inputId)
        {
            SDL_SensorType sensorType = inputId switch
            {
                MotionInputId.Accelerometer => SDL_SensorType.SDL_SENSOR_ACCEL,
                MotionInputId.Gyroscope => SDL_SensorType.SDL_SENSOR_GYRO,
                _ => SDL_SensorType.SDL_SENSOR_INVALID
            };

            if ((Features & GamepadFeaturesFlag.Motion) == 0 || sensorType is SDL_SensorType.SDL_SENSOR_INVALID)
                return Vector3.Zero;

            const int ElementCount = 3;

            float[] values = new float[3];

            fixed (float* pValues = &values[0]) {
                if (!SDL_GetGamepadSensorData(_gamepadHandle, sensorType, pValues, ElementCount))
                    return Vector3.Zero;

                Vector3 value = _joyConType switch
                {
                    JoyConType.Left => new Vector3(-values[2], values[1], values[0]),
                    JoyConType.Right => new Vector3(values[2], values[1], -values[0]),
                    _ => throw new NotSupportedException($"Unsupported JoyCon type: {_joyConType}")
                };

                return inputId switch
                {
                    MotionInputId.Gyroscope => RadToDegree(value),
                    MotionInputId.Accelerometer => GsToMs2(value),
                    _ => value
                };
            }
        }

        private static Vector3 RadToDegree(Vector3 rad) => rad * (180 / MathF.PI);

        private static Vector3 GsToMs2(Vector3 gs) => gs / SDL_STANDARD_GRAVITY;

        public void SetConfiguration(InputConfig configuration)
        {
            lock (_userMappingLock)
            {
                _configuration = (StandardControllerInputConfig)configuration;

                _buttonsUserMapping.Clear();

                // First update sticks
                _stickUserMapping[(int)StickInputId.Left] = (StickInputId)_configuration.LeftJoyconStick.Joystick;
                _stickUserMapping[(int)StickInputId.Right] = (StickInputId)_configuration.RightJoyconStick.Joystick;

                switch (_joyConType)
                {
                    case JoyConType.Left:
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.LeftStick, (GamepadButtonInputId)_configuration.LeftJoyconStick.StickButton));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.DpadUp, (GamepadButtonInputId)_configuration.LeftJoycon.DpadUp));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.DpadDown, (GamepadButtonInputId)_configuration.LeftJoycon.DpadDown));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.DpadLeft, (GamepadButtonInputId)_configuration.LeftJoycon.DpadLeft));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.DpadRight, (GamepadButtonInputId)_configuration.LeftJoycon.DpadRight));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.Minus, (GamepadButtonInputId)_configuration.LeftJoycon.ButtonMinus));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.LeftShoulder, (GamepadButtonInputId)_configuration.LeftJoycon.ButtonL));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.LeftTrigger, (GamepadButtonInputId)_configuration.LeftJoycon.ButtonZl));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.SingleRightTrigger0, (GamepadButtonInputId)_configuration.LeftJoycon.ButtonSr));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.SingleLeftTrigger0, (GamepadButtonInputId)_configuration.LeftJoycon.ButtonSl));
                        break;
                    case JoyConType.Right:
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.RightStick, (GamepadButtonInputId)_configuration.RightJoyconStick.StickButton));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.A, (GamepadButtonInputId)_configuration.RightJoycon.ButtonA));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.B, (GamepadButtonInputId)_configuration.RightJoycon.ButtonB));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.X, (GamepadButtonInputId)_configuration.RightJoycon.ButtonX));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.Y, (GamepadButtonInputId)_configuration.RightJoycon.ButtonY));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.Plus, (GamepadButtonInputId)_configuration.RightJoycon.ButtonPlus));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.RightShoulder, (GamepadButtonInputId)_configuration.RightJoycon.ButtonR));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.RightTrigger, (GamepadButtonInputId)_configuration.RightJoycon.ButtonZr));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.SingleRightTrigger1, (GamepadButtonInputId)_configuration.RightJoycon.ButtonSr));
                        _buttonsUserMapping.Add(new ButtonMappingEntry(GamepadButtonInputId.SingleLeftTrigger1, (GamepadButtonInputId)_configuration.RightJoycon.ButtonSl));
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported JoyCon type: {_joyConType}");
                }

                SetTriggerThreshold(_configuration.TriggerThreshold);
            }
        }

        public void SetLed(uint packedRgb)
        {
        }

        public GamepadStateSnapshot GetStateSnapshot()
        {
            return IGamepad.GetStateSnapshot(this);
        }

        public GamepadStateSnapshot GetMappedStateSnapshot()
        {
            GamepadStateSnapshot rawState = GetStateSnapshot();
            GamepadStateSnapshot result = default;

            lock (_userMappingLock)
            {
                if (_buttonsUserMapping.Count == 0)
                    return rawState;

                // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
                foreach (ButtonMappingEntry entry in _buttonsUserMapping)
                {
                    if (!entry.IsValid)
                        continue;

                    // Do not touch state of button already pressed
                    if (!result.IsPressed(entry.To))
                    {
                        result.SetPressed(entry.To, rawState.IsPressed(entry.From));
                    }
                }

                (float leftStickX, float leftStickY) = rawState.GetStick(_stickUserMapping[(int)StickInputId.Left]);
                (float rightStickX, float rightStickY) = rawState.GetStick(_stickUserMapping[(int)StickInputId.Right]);

                result.SetStick(StickInputId.Left, leftStickX, leftStickY);
                result.SetStick(StickInputId.Right, rightStickX, rightStickY);
            }

            return result;
        }

        private static float ConvertRawStickValue(short value)
        {
            const float ConvertRate = 1.0f / (short.MaxValue + 0.5f);

            return value * ConvertRate;
        }

        private JoyconConfigControllerStick<GamepadInputId, Common.Configuration.Hid.Controller.StickInputId>
            GetLogicalJoyStickConfig(StickInputId inputId)
        {
            switch (inputId)
            {
                case StickInputId.Left:
                    if (_configuration.RightJoyconStick.Joystick ==
                        Common.Configuration.Hid.Controller.StickInputId.Left)
                        return _configuration.RightJoyconStick;
                    else
                        return _configuration.LeftJoyconStick;
                case StickInputId.Right:
                    if (_configuration.LeftJoyconStick.Joystick ==
                        Common.Configuration.Hid.Controller.StickInputId.Right)
                        return _configuration.LeftJoyconStick;
                    else
                        return _configuration.RightJoyconStick;
            }

            return null;
        }

        public (float, float) GetStick(StickInputId inputId)
        {
            if (inputId == StickInputId.Unbound)
                return (0.0f, 0.0f);

            if (inputId == StickInputId.Left && _joyConType == JoyConType.Right || inputId == StickInputId.Right && _joyConType == JoyConType.Left)
            {
                return (0.0f, 0.0f);
            }

            (short stickX, short stickY) = GetStickXY();

            float resultX = ConvertRawStickValue(stickX);
            float resultY = -ConvertRawStickValue(stickY);

            if (HasConfiguration)
            {
                JoyconConfigControllerStick<GamepadInputId, Common.Configuration.Hid.Controller.StickInputId> joyconStickConfig = GetLogicalJoyStickConfig(inputId);

                if (joyconStickConfig != null)
                {
                    if (joyconStickConfig.InvertStickX)
                        resultX = -resultX;

                    if (joyconStickConfig.InvertStickY)
                        resultY = -resultY;

                    if (joyconStickConfig.Rotate90CW)
                    {
                        float temp = resultX;
                        resultX = resultY;
                        resultY = -temp;
                    }
                }
            }

            return inputId switch
            {
                StickInputId.Left when _joyConType == JoyConType.Left => (resultY, -resultX),
                StickInputId.Right when _joyConType == JoyConType.Right => (-resultY, resultX),
                _ => (0.0f, 0.0f)
            };
        }

        private (short, short) GetStickXY()
        {
            return (
                SDL_GetGamepadAxis(_gamepadHandle, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX),
                SDL_GetGamepadAxis(_gamepadHandle, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY));
        }

        public bool IsPressed(GamepadButtonInputId inputId)
        {
            if (!_buttonsDriverMapping.TryGetValue(inputId, out SDL_GamepadButton button))
            {
                return false;
            }

            return SDL_GetGamepadButton(_gamepadHandle, button);
        }
        
        public static bool IsJoyCon(SDL_JoystickID gamepadsId)
        {
            return SDL_GetGamepadNameForID(gamepadsId) is LeftName or RightName;
        }
        
        public static bool IsLeftJoyCon(SDL_JoystickID gamepadsId)
        {
            return SDL_GetGamepadNameForID(gamepadsId) is LeftName;
        }
    }
}
