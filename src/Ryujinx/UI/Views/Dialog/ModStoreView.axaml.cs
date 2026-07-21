using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Ryujinx.Ava.UI.Controls;
using Ryujinx.Ava.UI.Models;
using Ryujinx.Ava.UI.ViewModels;
using System.Threading.Tasks;
using Button = Avalonia.Controls.Button;

namespace Ryujinx.Ava.UI.Views.Dialog
{
    public partial class ModStoreView : RyujinxControl<ModStoreViewModel>
    {
        public ModStoreView()
        {
            InitializeComponent();
        }

        public static async Task Show(ulong titleId, string titleName)
        {
            ContentDialog contentDialog = new()
            {
                PrimaryButtonText = string.Empty,
                SecondaryButtonText = string.Empty,
                CloseButtonText = string.Empty,
                Content = new ModStoreView
                {
                    ViewModel = new ModStoreViewModel(titleId),
                },
                Title = $"Magasin des Mods — {titleName}",
            };

            await contentDialog.ShowAsync();
        }

        private async void Download(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: ModStoreItem item })
            {
                await ViewModel.DownloadAsync(item);
            }
        }

        private async void Delete(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: ModStoreItem item })
            {
                await ViewModel.DeleteAsync(item);
            }
        }

        private void CloseDialog(object sender, RoutedEventArgs e)
        {
            (Parent as ContentDialog)?.Hide();
        }
    }
}
