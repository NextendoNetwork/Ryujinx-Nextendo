using Avalonia.Interactivity;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Common.Models.Amiibo;
using Ryujinx.Ava.Systems;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.UI.ViewModels;
using Avalonia.Controls;
using System;
using Avalonia.Controls.Primitives;

namespace Ryujinx.Ava.UI.Windows
{
    public partial class AmiiboWindow : StyleableAppWindow
    {
        public AmiiboWindow(bool showAll, string lastScannedAmiiboId, string titleId) : base(true, 40)
        {
            DataContext = ViewModel = new AmiiboWindowViewModel(this, lastScannedAmiiboId, titleId)
            {
                ShowAllAmiibo = showAll,
            };

            InitializeComponent();

            FlushControls.IsVisible = !ConfigurationState.Instance.ShowOldUI;
            NormalControls.IsVisible = ConfigurationState.Instance.ShowOldUI;

            Title = RyujinxApp.FormatTitle(LocaleKeys.Amiibo_WindowTitle);

            if (ViewModel.PauseEmulationWhileScanningAmiibo && RyujinxApp.MainWindow?.ViewModel?.AppHost != null)
            {
                RyujinxApp.MainWindow.ViewModel.AppHost.Pause();
            }

            ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
            {
                FlyoutBase.ShowAttachedFlyout((Control)sender!);
            }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ViewModel.PauseEmulationWhileScanningAmiibo))
                return;

            AppHost host = RyujinxApp.MainWindow?.ViewModel?.AppHost;
            if (host == null) 
                return;

            if (ViewModel.PauseEmulationWhileScanningAmiibo)
                host.Pause();
            else
                host.Resume();
        }

        private void AlwaysResumeOnClose()
        {
            if (RyujinxApp.MainWindow?.ViewModel?.AppHost != null)
            {
                RyujinxApp.MainWindow.ViewModel.AppHost.Resume();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            AlwaysResumeOnClose();
            base.OnClosed(e);
        }

        public AmiiboWindow()
        {
            DataContext = ViewModel = new AmiiboWindowViewModel(this, string.Empty, string.Empty);

            InitializeComponent();

            if (Program.PreviewerDetached)
            {
                Title = RyujinxApp.FormatTitle(LocaleKeys.Amiibo_WindowTitle);
            }
        }

        public void Sort_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton { Tag: string tag } && ViewModel != null)
            {
                if (Enum.TryParse<AmiiboWindowViewModel.AmiiboSortField>(tag, out var sortField))
                {
                    ViewModel.SortingField = sortField;
                }
            }
        }

        public void Order_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton { Tag: string tag } && ViewModel != null)
            {
                ViewModel.SortingAscending = tag == "Ascending";
            }
        }

        public bool IsScanned { get; set; }
        public AmiiboApi ScannedAmiibo { get; set; }
        public AmiiboWindowViewModel ViewModel;

        private void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            AlwaysResumeOnClose();
            ViewModel.Scan();
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            AlwaysResumeOnClose();
            ViewModel.Cancel();
            Close();
        }
    }
}
