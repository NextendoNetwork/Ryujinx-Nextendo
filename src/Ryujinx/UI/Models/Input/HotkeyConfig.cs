using CommunityToolkit.Mvvm.ComponentModel;
using Ryujinx.Ava.UI.ViewModels;
using Ryujinx.Common.Configuration.Hid;

namespace Ryujinx.Ava.UI.Models.Input
{
    public partial class HotkeyConfig : BaseModel
    {
        [ObservableProperty]
        public partial Key ToggleVSyncMode { get; set; }

        [ObservableProperty]
        public partial Key Screenshot { get; set; }

        [ObservableProperty]
        public partial Key ShowUI { get; set; }

        [ObservableProperty]
        public partial Key Pause { get; set; }

        [ObservableProperty]
        public partial Key ToggleMute { get; set; }

        [ObservableProperty]
        public partial Key ResScaleUp { get; set; }

        [ObservableProperty]
        public partial Key ResScaleDown { get; set; }

        [ObservableProperty]
        public partial Key VolumeUp { get; set; }

        [ObservableProperty]
        public partial Key VolumeDown { get; set; }

        [ObservableProperty]
        public partial Key CustomVSyncIntervalIncrement { get; set; }

        [ObservableProperty]
        public partial Key CustomVSyncIntervalDecrement { get; set; }

        [ObservableProperty]
        public partial Key TurboMode { get; set; }

        [ObservableProperty]
        public partial bool TurboModeWhileHeld { get; set; }

        public HotkeyConfig(KeyboardHotkeys config)
        {
            if (config == null)
                return;

            ToggleVSyncMode = config.ToggleVSyncMode;
            Screenshot = config.Screenshot;
            ShowUI = config.ShowUI;
            Pause = config.Pause;
            ToggleMute = config.ToggleMute;
            ResScaleUp = config.ResScaleUp;
            ResScaleDown = config.ResScaleDown;
            VolumeUp = config.VolumeUp;
            VolumeDown = config.VolumeDown;
            CustomVSyncIntervalIncrement = config.CustomVSyncIntervalIncrement;
            CustomVSyncIntervalDecrement = config.CustomVSyncIntervalDecrement;
            TurboMode = config.TurboMode;
            TurboModeWhileHeld = config.TurboModeWhileHeld;
        }

        public KeyboardHotkeys GetConfig() =>
            new()
            {
                ToggleVSyncMode = ToggleVSyncMode,
                Screenshot = Screenshot,
                ShowUI = ShowUI,
                Pause = Pause,
                ToggleMute = ToggleMute,
                ResScaleUp = ResScaleUp,
                ResScaleDown = ResScaleDown,
                VolumeUp = VolumeUp,
                VolumeDown = VolumeDown,
                CustomVSyncIntervalIncrement = CustomVSyncIntervalIncrement,
                CustomVSyncIntervalDecrement = CustomVSyncIntervalDecrement,
                TurboMode = TurboMode,
                TurboModeWhileHeld = TurboModeWhileHeld
            };
    }
}
