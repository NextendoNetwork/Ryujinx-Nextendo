using Avalonia.Svg.Skia;
using CommunityToolkit.Mvvm.ComponentModel;
using Ryujinx.Ava.UI.Models.Input;
using Ryujinx.Ava.UI.Views.Input;
using Ryujinx.Common.Helper;
using Ryujinx.Common.Utilities;
using Ryujinx.UI.Views.Input;

namespace Ryujinx.Ava.UI.ViewModels.Input
{
    public partial class ControllerInputViewModel : BaseModel
    {
        public GamepadInputConfig Config
        {
            get;
            set
            {
                field = value;

                OnPropertyChanged();
            }
        }

        public StickVisualizer Visualizer
        {
            get;
            set
            {
                field = value;

                OnPropertyChanged();
            }
        }

        public bool IsLeft
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSides));
            }
        }

        public bool IsRight
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSides));
            }
        }

        public bool HasSides => IsLeft ^ IsRight;

        [ObservableProperty]
        public partial SvgImage Image { get; set; }
        public InputViewModel ParentModel { get; }

        private readonly RefEvent<System.Drawing.Color>.Handler _rainbowLedHandler;

        public ControllerInputViewModel(InputViewModel model, GamepadInputConfig config, StickVisualizer visualizer)
        {
            ParentModel = model;
            Visualizer = visualizer;
            _rainbowLedHandler = SetRainbowLed;

            model.NotifyChangesEvent += OnParentModelChanged;
            OnParentModelChanged();
            config.PropertyChanged += OnConfigPropertyChanged;
            Config = config;
        }

        private void OnConfigPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(Config.UseRainbowLed))
            {
                if (Config is { UseRainbowLed: true, TurnOffLed: false, EnableLedChanging: true })
                {
                    Rainbow.Updated -= _rainbowLedHandler;
                    Rainbow.Updated += _rainbowLedHandler;
                }
                else
                {
                    Rainbow.Reset();

                    if (Config.TurnOffLed)
                        ParentModel.SelectedGamepad.ClearLed();
                    else
                        ParentModel.SelectedGamepad.SetLed(Config.LedColor.ToUInt32());
                }
            }
        }

        private void SetRainbowLed(ref System.Drawing.Color color)
        {
            ParentModel.SelectedGamepad.SetLed((uint)color.ToArgb());
        }

        public async void ShowMotionConfig()
        {
            await MotionInputView.Show(this);
            ParentModel.RefreshModifiedState();
        }

        public async void ShowRumbleConfig()
        {
            await RumbleInputView.Show(this);
            ParentModel.RefreshModifiedState();
        }

        public async void ShowLedConfig()
        {
            await LedInputView.Show(this);
            ParentModel.RefreshModifiedState();
        }

        public void OnParentModelChanged()
        {
            IsLeft = ParentModel.IsLeft;
            IsRight = ParentModel.IsRight;
            Image = ParentModel.Image;
        }
    }
}
