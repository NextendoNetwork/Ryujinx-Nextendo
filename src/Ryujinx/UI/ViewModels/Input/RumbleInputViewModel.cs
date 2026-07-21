using CommunityToolkit.Mvvm.ComponentModel;

namespace Ryujinx.Ava.UI.ViewModels.Input
{
    public partial class RumbleInputViewModel : BaseModel
    {
        [ObservableProperty]
        public partial float StrongRumble { get; set; }

        [ObservableProperty]
        public partial float WeakRumble { get; set; }

        [ObservableProperty]
        public partial bool EnableHDRumble { get; set; }
    }
}
