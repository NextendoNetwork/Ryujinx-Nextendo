using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Gommon;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Systems.Configuration;
using System;

namespace Ryujinx.Ava.UI.ViewModels
{
    public partial class AboutWindowViewModel : BaseModel, IDisposable
    {
        [ObservableProperty] public partial Bitmap ForgejoLogo { get; set; }

        [ObservableProperty] public partial Bitmap DiscordLogo { get; set; }

        [ObservableProperty] public partial string Version { get; set; }

        public static string Developers => "GreemDev, LotP";

        public static string FormerDevelopers => LocaleManager.Instance.UpdateAndGetDynamicValue(
            LocaleKeys.AboutPageDeveloperListMore,
            "gdkchan, Ac_K, marysaka, rip in peri peri, LDj3SNuD, emmaus, Thealexbarney, GoffyDude, TSRBerry, IsaacMarovitz");

        public AboutWindowViewModel()
        {
            Version = RyujinxApp.FullAppName + "\n" + Program.Version;
            UpdateLogoTheme(ConfigurationState.Instance.UI.BaseStyle.Value);

            RyujinxApp.ThemeChanged += Ryujinx_ThemeChanged;
        }

        private void Ryujinx_ThemeChanged()
        {
            Dispatcher.UIThread.Post(() => UpdateLogoTheme(ConfigurationState.Instance.UI.BaseStyle.Value));
        }

        private const string LogoPathFormat = "resm:Ryujinx.Assets.UIImages.Logo_{0}_{1}.png?assembly=Ryujinx";
        private const string UnthemedLogoPathFormat = "resm:Ryujinx.Assets.UIImages.Logo_{0}.png?assembly=Ryujinx";

        private void UpdateLogoTheme(string theme)
        {
            bool isDarkTheme = theme == "Dark" ||
                               (theme == "Auto" && RyujinxApp.DetectSystemTheme() == ThemeVariant.Dark);

            string themeName = isDarkTheme ? "Dark" : "Light";

            DiscordLogo = LoadBitmap(LogoPathFormat.Format("Discord", themeName));
            ForgejoLogo = LoadBitmap(UnthemedLogoPathFormat.Format("Forgejo"));
        }

        private static Bitmap LoadBitmap(string uri) => new(Avalonia.Platform.AssetLoader.Open(new Uri(uri)));

        public void Dispose()
        {
            RyujinxApp.ThemeChanged -= Ryujinx_ThemeChanged;

            ForgejoLogo.Dispose();
            DiscordLogo.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
