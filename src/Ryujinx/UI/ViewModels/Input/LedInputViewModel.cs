using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Humanizer;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.UI.Helpers;
using System.Globalization;

namespace Ryujinx.Ava.UI.ViewModels.Input
{
    public partial class LedInputViewModel : BaseModel
    {
        public required InputViewModel ParentModel { get; init; }

        public RelayCommand LedDisabledChanged => Commands.Create(() =>
        {
            if (!EnableLedChanging)
                return;

            if (TurnOffLed)
                ParentModel.SelectedGamepad.ClearLed();
            else
                ParentModel.SelectedGamepad.SetLed(LedColor.ToUInt32());
        });

        [ObservableProperty]
        public partial bool EnableLedChanging { get; set; }

        [ObservableProperty]
        public partial Color LedColor { get; set; }

        public string RainbowSpeedText => RainbowSpeed.ToString(CultureInfo.CurrentCulture).Truncate(4, string.Empty);

        public float RainbowSpeed
        {
            get => ConfigurationState.Instance.Hid.RainbowSpeed;
            set
            {
                ConfigurationState.Instance.Hid.RainbowSpeed.Value = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RainbowSpeedText));
            }
        }

        public bool ShowLedColorPicker => !TurnOffLed && !UseRainbowLed;

        public bool TurnOffLed
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowLedColorPicker));
            }
        }

        public bool UseRainbowLed
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowLedColorPicker));
            }
        }
    }
}
