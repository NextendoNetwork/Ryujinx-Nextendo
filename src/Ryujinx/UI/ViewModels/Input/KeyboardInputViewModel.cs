using Avalonia.Svg.Skia;
using CommunityToolkit.Mvvm.ComponentModel;
using Ryujinx.Ava.UI.Models.Input;

namespace Ryujinx.Ava.UI.ViewModels.Input
{
    public partial class KeyboardInputViewModel : BaseModel
    {
        public KeyboardInputConfig Config
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

        public readonly InputViewModel ParentModel;

        public KeyboardInputViewModel(InputViewModel model, KeyboardInputConfig config, StickVisualizer visualizer)
        {
            ParentModel = model;
            Visualizer = visualizer;
            model.NotifyChangesEvent += OnParentModelChanged;
            OnParentModelChanged();
            Config = config;
        }

        public void OnParentModelChanged()
        {
            IsLeft = ParentModel.IsLeft;
            IsRight = ParentModel.IsRight;
            Image = ParentModel.Image;
        }
    }
}
