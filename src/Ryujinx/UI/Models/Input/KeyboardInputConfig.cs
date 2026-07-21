using CommunityToolkit.Mvvm.ComponentModel;
using Ryujinx.Ava.UI.ViewModels;
using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Common.Configuration.Hid.Keyboard;

namespace Ryujinx.Ava.UI.Models.Input
{
    public partial class KeyboardInputConfig : BaseModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public ControllerType ControllerType { get; set; }
        public PlayerIndex PlayerIndex { get; set; }

        [ObservableProperty]
        public partial PhysicalKey LeftStickUp { get; set; }

        [ObservableProperty]
        public partial PhysicalKey LeftStickDown { get; set; }

        [ObservableProperty]
        public partial PhysicalKey LeftStickLeft { get; set; }

        [ObservableProperty]
        public partial PhysicalKey LeftStickRight { get; set; }

        [ObservableProperty]
        public partial PhysicalKey LeftStickButton { get; set; }

        [ObservableProperty]
        public partial PhysicalKey RightStickUp { get; set; }

        [ObservableProperty]
        public partial PhysicalKey RightStickDown { get; set; }

        [ObservableProperty]
        public partial PhysicalKey RightStickLeft { get; set; }

        [ObservableProperty]
        public partial PhysicalKey RightStickRight { get; set; }

        [ObservableProperty]
        public partial PhysicalKey RightStickButton { get; set; }

        [ObservableProperty]
        public partial PhysicalKey DpadUp { get; set; }

        [ObservableProperty]
        public partial PhysicalKey DpadDown { get; set; }

        [ObservableProperty]
        public partial PhysicalKey DpadLeft { get; set; }

        [ObservableProperty]
        public partial PhysicalKey DpadRight { get; set; }

        [ObservableProperty]
        public partial PhysicalKey ButtonMinus { get; set; }

        [ObservableProperty]
        public partial PhysicalKey ButtonPlus { get; set; }

        [ObservableProperty]
        public partial PhysicalKey ButtonA { get; set; }

        [ObservableProperty]
        public partial PhysicalKey ButtonB { get; set; }

        [ObservableProperty]
        public partial PhysicalKey ButtonX { get; set; }

        [ObservableProperty]
        public partial PhysicalKey ButtonY { get; set; }

        [ObservableProperty]
        public partial PhysicalKey ButtonL { get; set; }

        [ObservableProperty]
        public partial PhysicalKey ButtonR { get; set; }

        [ObservableProperty]
        public partial PhysicalKey ButtonZl { get; set; }

        [ObservableProperty]
        public partial PhysicalKey ButtonZr { get; set; }

        [ObservableProperty]
        public partial PhysicalKey LeftButtonSl { get; set; }

        [ObservableProperty]
        public partial PhysicalKey LeftButtonSr { get; set; }

        [ObservableProperty]
        public partial PhysicalKey RightButtonSl { get; set; }

        [ObservableProperty]
        public partial PhysicalKey RightButtonSr { get; set; }

        public KeyboardInputConfig(InputConfig config)
        {
            if (config != null)
            {
                Id = config.Id;
                Name = config.Name;
                ControllerType = config.ControllerType;
                PlayerIndex = config.PlayerIndex;

                if (config is not StandardKeyboardInputConfig keyboardConfig)
                {
                    return;
                }

                LeftStickUp = keyboardConfig.LeftJoyconStick.StickUp;
                LeftStickDown = keyboardConfig.LeftJoyconStick.StickDown;
                LeftStickLeft = keyboardConfig.LeftJoyconStick.StickLeft;
                LeftStickRight = keyboardConfig.LeftJoyconStick.StickRight;
                LeftStickButton = keyboardConfig.LeftJoyconStick.StickButton;

                RightStickUp = keyboardConfig.RightJoyconStick.StickUp;
                RightStickDown = keyboardConfig.RightJoyconStick.StickDown;
                RightStickLeft = keyboardConfig.RightJoyconStick.StickLeft;
                RightStickRight = keyboardConfig.RightJoyconStick.StickRight;
                RightStickButton = keyboardConfig.RightJoyconStick.StickButton;

                DpadUp = keyboardConfig.LeftJoycon.DpadUp;
                DpadDown = keyboardConfig.LeftJoycon.DpadDown;
                DpadLeft = keyboardConfig.LeftJoycon.DpadLeft;
                DpadRight = keyboardConfig.LeftJoycon.DpadRight;
                ButtonL = keyboardConfig.LeftJoycon.ButtonL;
                ButtonMinus = keyboardConfig.LeftJoycon.ButtonMinus;
                LeftButtonSl = keyboardConfig.LeftJoycon.ButtonSl;
                LeftButtonSr = keyboardConfig.LeftJoycon.ButtonSr;
                ButtonZl = keyboardConfig.LeftJoycon.ButtonZl;

                ButtonA = keyboardConfig.RightJoycon.ButtonA;
                ButtonB = keyboardConfig.RightJoycon.ButtonB;
                ButtonX = keyboardConfig.RightJoycon.ButtonX;
                ButtonY = keyboardConfig.RightJoycon.ButtonY;
                ButtonR = keyboardConfig.RightJoycon.ButtonR;
                ButtonPlus = keyboardConfig.RightJoycon.ButtonPlus;
                RightButtonSl = keyboardConfig.RightJoycon.ButtonSl;
                RightButtonSr = keyboardConfig.RightJoycon.ButtonSr;
                ButtonZr = keyboardConfig.RightJoycon.ButtonZr;
            }
        }

        public InputConfig GetConfig()
        {
            StandardKeyboardInputConfig config = new()
            {
                Id = Id,
                Name = Name,
                Backend = InputBackendType.WindowKeyboard,
                PlayerIndex = PlayerIndex,
                ControllerType = ControllerType,
                LeftJoycon = new LeftJoyconCommonConfig<PhysicalKey>
                {
                    DpadUp = DpadUp,
                    DpadDown = DpadDown,
                    DpadLeft = DpadLeft,
                    DpadRight = DpadRight,
                    ButtonL = ButtonL,
                    ButtonMinus = ButtonMinus,
                    ButtonZl = ButtonZl,
                    ButtonSl = LeftButtonSl,
                    ButtonSr = LeftButtonSr,
                },
                RightJoycon = new RightJoyconCommonConfig<PhysicalKey>
                {
                    ButtonA = ButtonA,
                    ButtonB = ButtonB,
                    ButtonX = ButtonX,
                    ButtonY = ButtonY,
                    ButtonPlus = ButtonPlus,
                    ButtonSl = RightButtonSl,
                    ButtonSr = RightButtonSr,
                    ButtonR = ButtonR,
                    ButtonZr = ButtonZr,
                },
                LeftJoyconStick = new JoyconConfigKeyboardStick<PhysicalKey>
                {
                    StickUp = LeftStickUp,
                    StickDown = LeftStickDown,
                    StickRight = LeftStickRight,
                    StickLeft = LeftStickLeft,
                    StickButton = LeftStickButton,
                },
                RightJoyconStick = new JoyconConfigKeyboardStick<PhysicalKey>
                {
                    StickUp = RightStickUp,
                    StickDown = RightStickDown,
                    StickLeft = RightStickLeft,
                    StickRight = RightStickRight,
                    StickButton = RightStickButton,
                },
                Version = InputConfig.CurrentVersion,
            };

            return config;
        }

        public void NotifyKeyLabelsChanged()
        {
            OnPropertiesChanged(nameof(LeftStickUp),
                nameof(LeftStickDown),
                nameof(LeftStickLeft),
                nameof(LeftStickRight),
                nameof(LeftStickButton),
                nameof(RightStickUp),
                nameof(RightStickDown),
                nameof(RightStickLeft),
                nameof(RightStickRight),
                nameof(RightStickButton),
                nameof(DpadUp),
                nameof(DpadDown),
                nameof(DpadLeft),
                nameof(DpadRight),
                nameof(ButtonMinus),
                nameof(ButtonPlus),
                nameof(ButtonA),
                nameof(ButtonB),
                nameof(ButtonX),
                nameof(ButtonY),
                nameof(ButtonL),
                nameof(ButtonR),
                nameof(ButtonZl),
                nameof(ButtonZr),
                nameof(LeftButtonSl),
                nameof(LeftButtonSr),
                nameof(RightButtonSl),
                nameof(RightButtonSr));
        }
    }
}
