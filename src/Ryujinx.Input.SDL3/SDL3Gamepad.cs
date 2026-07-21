using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Common.Configuration.Hid.Controller;
using Ryujinx.Common.Logging;
using Ryujinx.Common.Utilities;
using Ryujinx.HLE.HOS.Services.Hid;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using SDL;
using static SDL.SDL3;

namespace Ryujinx.Input.SDL3
{
    public unsafe class SDL3Gamepad : IGamepad
    {
        private bool HasConfiguration => _configuration != null;

        private readonly record struct ButtonMappingEntry(GamepadButtonInputId To, GamepadButtonInputId From)
        {
            public bool IsValid => To is not GamepadButtonInputId.Unbound && From is not GamepadButtonInputId.Unbound;
        }

        private StandardControllerInputConfig _configuration;

        private readonly SDL_GamepadButton[] _buttonsDriverMapping =
        [
            // Unbound, ignored.
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_INVALID,

            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_STICK,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER,

            // NOTE: The left and right trigger are axis, we handle those differently
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_INVALID,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_INVALID,

            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_GUIDE,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_MISC1,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_PADDLE1,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_PADDLE1,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_PADDLE2,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_PADDLE2,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_TOUCHPAD,

            // Virtual buttons are invalid, ignored.
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_INVALID,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_INVALID,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_INVALID,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_INVALID,
        ];

        private readonly Lock _userMappingLock = new();

        private readonly List<ButtonMappingEntry> _buttonsUserMapping;

        private readonly StickInputId[] _stickUserMapping =
        [
            StickInputId.Unbound,
            StickInputId.Left,
            StickInputId.Right
        ];

        public GamepadFeaturesFlag Features { get; }

        private SDL_Gamepad* _gamepadHandle;

        private NpadHdRumble _hdRumble;

        private float _triggerThreshold;

        public SDL3Gamepad(SDL_Gamepad* gamepadHandle, string driverId)
        {
            _gamepadHandle = gamepadHandle;
            _hdRumble = NpadHdRumble.Create(gamepadHandle);
            _buttonsUserMapping = new List<ButtonMappingEntry>(20);

            Name = SDL_GetGamepadName(_gamepadHandle);
            Id = driverId;
            Features = GetFeaturesFlag();
            _triggerThreshold = 0.0f;

            // Face button mapping
            SDL_GamepadButton[] faceButtons = _buttonsDriverMapping[1..5];
            foreach (SDL_GamepadButton btn in faceButtons) {
                int mapId = SDL_GetGamepadButtonLabel(_gamepadHandle, btn) switch {
                    SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_A or SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_CROSS => 1,
                    SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_B or SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_CIRCLE => 2,
                    SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_X or SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_SQUARE => 3,
                    SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_Y or SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_TRIANGLE => 4,
                    _ => -1
                };
                if (mapId == -1) { continue; }
                _buttonsDriverMapping[mapId] = btn;
            }

            // Enable motion tracking
            if ((Features & GamepadFeaturesFlag.Motion) != 0)
            {
                if (!SDL_SetGamepadSensorEnabled(_gamepadHandle, SDL_SensorType.SDL_SENSOR_ACCEL, true))
                {
                    Logger.Error?.Print(LogClass.Hid, $"Could not enable data reporting for SensorType {SDL_SensorType.SDL_SENSOR_ACCEL}.");
                }

                if (!SDL_SetGamepadSensorEnabled(_gamepadHandle, SDL_SensorType.SDL_SENSOR_GYRO, true))
                {
                    Logger.Error?.Print(LogClass.Hid, $"Could not enable data reporting for SensorType {SDL_SensorType.SDL_SENSOR_GYRO}.");
                }
            }
        }

        public void SetLed(uint packedRgb)
        {
            if ((Features & GamepadFeaturesFlag.Led) == 0)
                return;

            byte red = packedRgb > 0 ? (byte)(packedRgb >> 16) : (byte)0;
            byte green = packedRgb > 0 ? (byte)(packedRgb >> 8) : (byte)0;
            byte blue = packedRgb > 0 ? (byte)(packedRgb % 256) : (byte)0;

            if (!SDL_SetGamepadLED(_gamepadHandle, red, green, blue))
                Logger.Debug?.Print(LogClass.Hid, "LED setting failed; probably in the middle of disconnecting.");
        }

        private GamepadFeaturesFlag GetFeaturesFlag()
        {
            GamepadFeaturesFlag result = GamepadFeaturesFlag.None;

            if (SDL_GamepadHasSensor(_gamepadHandle, SDL_SensorType.SDL_SENSOR_ACCEL) &&
                SDL_GamepadHasSensor(_gamepadHandle, SDL_SensorType.SDL_SENSOR_GYRO))
            {
                result |= GamepadFeaturesFlag.Motion;
            }
            SDL_PropertiesID propID = SDL_GetGamepadProperties(_gamepadHandle);
            SDL_LockProperties(propID);
            if (SDL_GetBooleanProperty(propID, SDL_PROP_GAMEPAD_CAP_RUMBLE_BOOLEAN, false))
            {
                result |= GamepadFeaturesFlag.Rumble;
            }

            if (SDL_GetBooleanProperty(propID, SDL_PROP_GAMEPAD_CAP_MONO_LED_BOOLEAN, false))
            {
                result |= GamepadFeaturesFlag.Led;
            }
            SDL_UnlockProperties(propID);
            SDL_DestroyProperties(propID);

            return result;
        }

        public string Id { get; }
        public string Name { get; }

        // Expose vendor id for higher-fidelity device detection in UI
        public ushort VendorId => _gamepadHandle != null ? SDL_GetGamepadVendor(_gamepadHandle) : (ushort)0;

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
            GC.SuppressFinalize(this);
        }

        public void SetTriggerThreshold(float triggerThreshold)
        {
            _triggerThreshold = triggerThreshold;
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

            if (!Features.HasFlag(GamepadFeaturesFlag.Motion) || sensorType is SDL_SensorType.SDL_SENSOR_INVALID)
                return Vector3.Zero;

            const int ElementCount = 3;

            float[] values = new float[3];

            fixed (float* pValues = &values[0]) {

                if (!SDL_GetGamepadSensorData(_gamepadHandle, sensorType, pValues, ElementCount))
                    return Vector3.Zero;

                Vector3 value = new(values[0], values[1], values[2]);

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

                if ((Features & GamepadFeaturesFlag.Led) != 0 && _configuration.Led?.EnableLed == true)
                {
                    if (_configuration.Led.TurnOffLed)
                        (this as IGamepad).ClearLed();
                    else if (_configuration.Led.UseRainbow)
                        SetLed((uint)Rainbow.Color.ToArgb());

                    if (!_configuration.Led.TurnOffLed && !_configuration.Led.UseRainbow)
                        SetLed(_configuration.Led.LedColor);
                }

                _buttonsUserMapping.Clear();

                // First update sticks
                _stickUserMapping[(int)StickInputId.Left] = (StickInputId)_configuration.LeftJoyconStick.Joystick;
                _stickUserMapping[(int)StickInputId.Right] = (StickInputId)_configuration.RightJoyconStick.Joystick;

                // Then left joycon
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

                // Finally right joycon
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

                SetTriggerThreshold(_configuration.TriggerThreshold);
            }
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

        private JoyconConfigControllerStick<GamepadInputId, Common.Configuration.Hid.Controller.StickInputId> GetLogicalJoyStickConfig(StickInputId inputId)
        {
            switch (inputId)
            {
                case StickInputId.Left:
                    if (_configuration.RightJoyconStick.Joystick == Common.Configuration.Hid.Controller.StickInputId.Left)
                        return _configuration.RightJoyconStick;
                    else
                        return _configuration.LeftJoyconStick;
                case StickInputId.Right:
                    if (_configuration.LeftJoyconStick.Joystick == Common.Configuration.Hid.Controller.StickInputId.Right)
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

            (short stickX, short stickY) = GetStickXY(inputId);

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

            return (resultX, resultY);
        }

        // ReSharper disable once InconsistentNaming
        private (short, short) GetStickXY(StickInputId inputId) =>
            inputId switch
            {
                StickInputId.Left => (
                    SDL_GetGamepadAxis(_gamepadHandle, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX),
                    SDL_GetGamepadAxis(_gamepadHandle, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY)),
                StickInputId.Right => (
                    SDL_GetGamepadAxis(_gamepadHandle, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX),
                    SDL_GetGamepadAxis(_gamepadHandle, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY)),
                _ => throw new NotSupportedException($"Unsupported stick {inputId}")
            };

        public bool IsPressed(GamepadButtonInputId inputId)
        {
            switch (inputId)
            {
                case GamepadButtonInputId.LeftTrigger:
                    return ConvertRawStickValue(SDL_GetGamepadAxis(_gamepadHandle, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER)) > _triggerThreshold;
                case GamepadButtonInputId.RightTrigger:
                    return ConvertRawStickValue(SDL_GetGamepadAxis(_gamepadHandle, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER)) > _triggerThreshold;
            }

            if (_buttonsDriverMapping[(int)inputId] == SDL_GamepadButton.SDL_GAMEPAD_BUTTON_INVALID)
            {
                return false;
            }

            return SDL_GetGamepadButton(_gamepadHandle, _buttonsDriverMapping[(int)inputId]);
        }
    }
}
