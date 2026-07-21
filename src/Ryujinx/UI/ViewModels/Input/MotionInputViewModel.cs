using CommunityToolkit.Mvvm.ComponentModel;

namespace Ryujinx.Ava.UI.ViewModels.Input
{
    public partial class MotionInputViewModel : BaseModel
    {
        [ObservableProperty]
        public partial int Slot { get; set; }

        [ObservableProperty]
        public partial int AltSlot { get; set; }

        [ObservableProperty]
        public partial string DsuServerHost { get; set; }

        [ObservableProperty]
        public partial int DsuServerPort { get; set; }

        [ObservableProperty]
        public partial bool MirrorInput { get; set; }

        [ObservableProperty]
        public partial int Sensitivity { get; set; }

        [ObservableProperty]
        public partial double GyroDeadzone { get; set; }

        [ObservableProperty]
        public partial bool EnableCemuHookMotion { get; set; }
    }
}
