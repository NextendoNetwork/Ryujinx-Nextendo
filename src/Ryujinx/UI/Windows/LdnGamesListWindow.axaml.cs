using Avalonia.Controls;
using Avalonia.Interactivity;
using Gommon;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.ViewModels;
using Ryujinx.Common;
using Ryujinx.Common.Helper;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.Windows
{
    public partial class LdnGamesListWindow : StyleableAppWindow
    {
        public static async Task Show(string searchTerm = null)
        {
            using LdnGamesListViewModel ldnGamesListVm = new(RyujinxApp.MainWindow.ViewModel);

            await ShowAsync(new LdnGamesListWindow
            {
                DataContext = ldnGamesListVm,
                SearchBoxFlush = { Text = searchTerm ?? string.Empty },
                SearchBoxNormal = { Text = searchTerm ?? string.Empty }
            });
        }

        public LdnGamesListWindow() : base(useCustomTitleBar: true, 37)
        {
            Title = RyujinxApp.FormatTitle(LocaleKeys.LdnGameListTitle);

            InitializeComponent();

            FlushControls.IsVisible = !ConfigurationState.Instance.ShowOldUI;
            NormalControls.IsVisible = ConfigurationState.Instance.ShowOldUI;

            RefreshFlush.Command = RefreshNormal.Command =
                Commands.Create(() => (DataContext as LdnGamesListViewModel)?.RefreshAsync().OrCompleted());

            InfoFlush.Command = InfoNormal.Command = 
                Commands.Create(() => OpenHelper.OpenUrl(SharedConstants.MultiplayerWikiUrl));
        }

        // ReSharper disable once UnusedMember.Local
        // its referenced in the axaml but rider keeps yelling at me that its unused so
        private void TextBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is not LdnGamesListViewModel cvm)
                return;

            if (sender is not TextBox searchBox)
                return;

            cvm.Search(searchBox.Text);
        }

        public void Sort_Name_Checked(object sender, RoutedEventArgs args)
        {
            if (sender is RadioButton { Tag: string sortStrategy })
            {
                if (DataContext is not LdnGamesListViewModel cvm)
                    return;

                cvm.NameSorting(int.Parse(sortStrategy));
            }
        }

        public void Sort_PlayerCount_Checked(object sender, RoutedEventArgs args)
        {
            if (sender is RadioButton { Tag: string sortStrategy })
            {
                if (DataContext is not LdnGamesListViewModel cvm)
                    return;

                cvm.StatusSorting(int.Parse(sortStrategy));
            }
        }
    }
}
