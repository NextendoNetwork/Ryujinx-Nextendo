using Ryujinx.Common.Configuration.Hid.Controller;
using Ryujinx.Common.Configuration.Hid.Keyboard;
using System.Numerics;

using ConfigPhysicalKey = Ryujinx.Common.Configuration.Hid.PhysicalKey;

namespace Ryujinx.Input
{
    public static class KeyboardInputMappingHelper
    {
        public readonly record struct KeyboardButtonMapping(GamepadButtonInputId To, ConfigPhysicalKey From)
        {
            public bool IsValid => To is not GamepadButtonInputId.Unbound && From is not ConfigPhysicalKey.Unknown and not ConfigPhysicalKey.Unbound;
        }

        public static KeyboardButtonMapping[] BuildButtonMappings(StandardKeyboardInputConfig configuration) =>
        [
            // Left JoyCon
            new(GamepadButtonInputId.LeftStick,           configuration.LeftJoyconStick.StickButton),
            new(GamepadButtonInputId.DpadUp,              configuration.LeftJoycon.DpadUp),
            new(GamepadButtonInputId.DpadDown,            configuration.LeftJoycon.DpadDown),
            new(GamepadButtonInputId.DpadLeft,            configuration.LeftJoycon.DpadLeft),
            new(GamepadButtonInputId.DpadRight,           configuration.LeftJoycon.DpadRight),
            new(GamepadButtonInputId.Minus,               configuration.LeftJoycon.ButtonMinus),
            new(GamepadButtonInputId.LeftShoulder,        configuration.LeftJoycon.ButtonL),
            new(GamepadButtonInputId.LeftTrigger,         configuration.LeftJoycon.ButtonZl),
            new(GamepadButtonInputId.SingleRightTrigger0, configuration.LeftJoycon.ButtonSr),
            new(GamepadButtonInputId.SingleLeftTrigger0,  configuration.LeftJoycon.ButtonSl),

            // Right JoyCon
            new(GamepadButtonInputId.RightStick,          configuration.RightJoyconStick.StickButton),
            new(GamepadButtonInputId.A,                   configuration.RightJoycon.ButtonA),
            new(GamepadButtonInputId.B,                   configuration.RightJoycon.ButtonB),
            new(GamepadButtonInputId.X,                   configuration.RightJoycon.ButtonX),
            new(GamepadButtonInputId.Y,                   configuration.RightJoycon.ButtonY),
            new(GamepadButtonInputId.Plus,                configuration.RightJoycon.ButtonPlus),
            new(GamepadButtonInputId.RightShoulder,       configuration.RightJoycon.ButtonR),
            new(GamepadButtonInputId.RightTrigger,        configuration.RightJoycon.ButtonZr),
            new(GamepadButtonInputId.SingleRightTrigger1, configuration.RightJoycon.ButtonSr),
            new(GamepadButtonInputId.SingleLeftTrigger1,  configuration.RightJoycon.ButtonSl),
        ];

        public static (short X, short Y) GetStickValues(ref KeyboardStateSnapshot snapshot, JoyconConfigKeyboardStick<ConfigPhysicalKey> stickConfig)
        {
            short stickX = 0;
            short stickY = 0;

            if (snapshot.IsPressed(stickConfig.StickUp))
            {
                stickY += 1;
            }

            if (snapshot.IsPressed(stickConfig.StickDown))
            {
                stickY -= 1;
            }

            if (snapshot.IsPressed(stickConfig.StickRight))
            {
                stickX += 1;
            }

            if (snapshot.IsPressed(stickConfig.StickLeft))
            {
                stickX -= 1;
            }

            if (stickX == 0 && stickY == 0)
            {
                return (0, 0);
            }

            Vector2 stick = Vector2.Normalize(new Vector2(stickX, stickY));

            return ((short)(stick.X * short.MaxValue), (short)(stick.Y * short.MaxValue));
        }
    }
}
