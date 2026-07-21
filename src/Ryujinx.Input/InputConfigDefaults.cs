using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Common.Configuration.Hid.Controller;
using Ryujinx.Common.Configuration.Hid.Controller.Motion;
using Ryujinx.Common.Configuration.Hid.Keyboard;

using ConfigGamepadInputId = Ryujinx.Common.Configuration.Hid.Controller.GamepadInputId;
using ConfigPhysicalKey = Ryujinx.Common.Configuration.Hid.PhysicalKey;
using ConfigStickInputId = Ryujinx.Common.Configuration.Hid.Controller.StickInputId;

namespace Ryujinx.Input
{
    public static class InputConfigDefaults
    {
        public static StandardKeyboardInputConfig CreateDefaultKeyboardConfiguration(
            string id,
            string name,
            ControllerType controllerType,
            PlayerIndex playerIndex)
        {
            return new StandardKeyboardInputConfig
            {
                Version = InputConfig.CurrentVersion,
                Backend = InputBackendType.WindowKeyboard,
                Id = id,
                Name = name,
                PlayerIndex = playerIndex,
                ControllerType = controllerType,
                LeftJoycon = new LeftJoyconCommonConfig<ConfigPhysicalKey>
                {
                    DpadUp = ConfigPhysicalKey.Up,
                    DpadDown = ConfigPhysicalKey.Down,
                    DpadLeft = ConfigPhysicalKey.Left,
                    DpadRight = ConfigPhysicalKey.Right,
                    ButtonMinus = ConfigPhysicalKey.Minus,
                    ButtonL = ConfigPhysicalKey.E,
                    ButtonZl = ConfigPhysicalKey.Q,
                    ButtonSl = ConfigPhysicalKey.Unbound,
                    ButtonSr = ConfigPhysicalKey.Unbound,
                },
                LeftJoyconStick = new JoyconConfigKeyboardStick<ConfigPhysicalKey>
                {
                    StickUp = ConfigPhysicalKey.W,
                    StickDown = ConfigPhysicalKey.S,
                    StickLeft = ConfigPhysicalKey.A,
                    StickRight = ConfigPhysicalKey.D,
                    StickButton = ConfigPhysicalKey.F,
                },
                RightJoycon = new RightJoyconCommonConfig<ConfigPhysicalKey>
                {
                    ButtonA = ConfigPhysicalKey.Z,
                    ButtonB = ConfigPhysicalKey.X,
                    ButtonX = ConfigPhysicalKey.C,
                    ButtonY = ConfigPhysicalKey.V,
                    ButtonPlus = ConfigPhysicalKey.Plus,
                    ButtonR = ConfigPhysicalKey.U,
                    ButtonZr = ConfigPhysicalKey.O,
                    ButtonSl = ConfigPhysicalKey.Unbound,
                    ButtonSr = ConfigPhysicalKey.Unbound,
                },
                RightJoyconStick = new JoyconConfigKeyboardStick<ConfigPhysicalKey>
                {
                    StickUp = ConfigPhysicalKey.I,
                    StickDown = ConfigPhysicalKey.K,
                    StickLeft = ConfigPhysicalKey.J,
                    StickRight = ConfigPhysicalKey.L,
                    StickButton = ConfigPhysicalKey.H,
                },
            };
        }

        public static StandardControllerInputConfig CreateDefaultControllerConfiguration(
            string id,
            string name,
            ControllerType controllerType,
            PlayerIndex playerIndex,
            bool isNintendoStyle)
        {
            return new StandardControllerInputConfig
            {
                Version = InputConfig.CurrentVersion,
                Backend = InputBackendType.GamepadSDL3,
                Id = id,
                Name = name,
                PlayerIndex = playerIndex,
                ControllerType = controllerType,
                DeadzoneLeft = 0.1f,
                DeadzoneRight = 0.1f,
                RangeLeft = 1.0f,
                RangeRight = 1.0f,
                TriggerThreshold = 0.5f,
                LeftJoycon = new LeftJoyconCommonConfig<ConfigGamepadInputId>
                {
                    DpadUp = ConfigGamepadInputId.DpadUp,
                    DpadDown = ConfigGamepadInputId.DpadDown,
                    DpadLeft = ConfigGamepadInputId.DpadLeft,
                    DpadRight = ConfigGamepadInputId.DpadRight,
                    ButtonMinus = ConfigGamepadInputId.Minus,
                    ButtonL = ConfigGamepadInputId.LeftShoulder,
                    ButtonZl = ConfigGamepadInputId.LeftTrigger,
                    ButtonSl = ConfigGamepadInputId.SingleLeftTrigger0,
                    ButtonSr = ConfigGamepadInputId.SingleRightTrigger0,
                },
                LeftJoyconStick = new JoyconConfigControllerStick<ConfigGamepadInputId, ConfigStickInputId>
                {
                    Joystick = ConfigStickInputId.Left,
                    StickButton = ConfigGamepadInputId.LeftStick,
                    InvertStickX = false,
                    InvertStickY = false,
                    Rotate90CW = false,
                },
                RightJoycon = new RightJoyconCommonConfig<ConfigGamepadInputId>
                {
                    ButtonA = isNintendoStyle ? ConfigGamepadInputId.A : ConfigGamepadInputId.B,
                    ButtonB = isNintendoStyle ? ConfigGamepadInputId.B : ConfigGamepadInputId.A,
                    ButtonX = isNintendoStyle ? ConfigGamepadInputId.X : ConfigGamepadInputId.Y,
                    ButtonY = isNintendoStyle ? ConfigGamepadInputId.Y : ConfigGamepadInputId.X,
                    ButtonPlus = ConfigGamepadInputId.Plus,
                    ButtonR = ConfigGamepadInputId.RightShoulder,
                    ButtonZr = ConfigGamepadInputId.RightTrigger,
                    ButtonSl = ConfigGamepadInputId.SingleLeftTrigger1,
                    ButtonSr = ConfigGamepadInputId.SingleRightTrigger1,
                },
                RightJoyconStick = new JoyconConfigControllerStick<ConfigGamepadInputId, ConfigStickInputId>
                {
                    Joystick = ConfigStickInputId.Right,
                    StickButton = ConfigGamepadInputId.RightStick,
                    InvertStickX = false,
                    InvertStickY = false,
                    Rotate90CW = false,
                },
                Motion = new StandardMotionConfigController
                {
                    MotionBackend = MotionInputBackendType.GamepadDriver,
                    EnableMotion = true,
                    Sensitivity = 100,
                    GyroDeadzone = 1,
                },
                Rumble = new RumbleConfigController
                {
                    StrongRumble = 1f,
                    WeakRumble = 1f,
                    EnableRumble = false,
                    UseHDRumble = false
                },
            };
        }
    }
}
