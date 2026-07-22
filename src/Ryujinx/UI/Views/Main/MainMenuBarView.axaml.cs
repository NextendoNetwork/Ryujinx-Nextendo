using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Gommon;
using LibHac.Common;
using LibHac.Ns;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Systems.AppLibrary;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.UI.Controls;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.ViewModels;
using Ryujinx.Ava.UI.Views.Dialog;
using Ryujinx.Ava.UI.Windows;
using Ryujinx.Ava.Utilities;
using FluentAvalonia.UI.Controls;
using SkiaSharp;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using Ryujinx.Common;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Helper;
using Ryujinx.Common.Utilities;
using Ryujinx.HLE.HOS.Services.Nfc.AmiiboDecryption;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.Views.Main
{
    public partial class MainMenuBarView : RyujinxControl<MainWindowViewModel>
    {
        public MainWindow Window { get; private set; }

        public MainMenuBarView()
        {
            InitializeComponent();

            ToggleFileTypesMenuItem.ItemsSource = GenerateToggleFileTypeItems();
            ChangeLanguageMenuItem.ItemsSource = GenerateLanguageMenuItems();
            MiiEditorMenuItem.Command = Commands.Create(OpenMiiEditor);
            CloseRyujinxMenuItem.Command = Commands.Create(() => Window?.Close());
            OpenSettingsMenuItem.Command = Commands.Create(OpenSettings);
            PauseEmulationMenuItem.Command = Commands.Create(() => ViewModel.AppHost?.Pause());
            ResumeEmulationMenuItem.Command = Commands.Create(() => ViewModel.AppHost?.Resume());
            StopEmulationMenuItem.Command = Commands.Create(() => ViewModel.AppHost?.ShowExitPrompt().OrCompleted());
            RestartEmulationMenuItem.Command = Commands.Create(() => ViewModel.RestartEmulation());
            XCITrimmerMenuItem.Command = Commands.Create(XciTrimmerView.Show);
            NextendoLoginMenuItem.Command = Commands.Create(OpenNextendoAccount);
            NextendoFriendsMenuItem.Command = Commands.Create(NextendoFriendsWindow.Open);
            NextendoReportMenuItem.Command = Commands.Create(() => NextendoReportWindow.Open());
            AboutWindowMenuItem.Command = Commands.Create(AboutView.Show);
            CompatibilityListMenuItem.Command = Commands.Create(() => CompatibilityListWindow.Show());
            LdnGameListMenuItem.Command = Commands.Create(() => LdnGamesListWindow.Show());

            UpdateMenuItem.Command = MainWindowViewModel.UpdateCommand;

            FaqMenuItem.Command =
                SetupGuideMenuItem.Command =
                    LdnGuideMenuItem.Command = Commands.Create<string>(OpenHelper.OpenUrl);

            WindowSize720PMenuItem.Command =
                WindowSize1080PMenuItem.Command =
                    WindowSize1440PMenuItem.Command =
                        WindowSize2160PMenuItem.Command = Commands.Create<string>(ChangeWindowSize);

            LocaleManager.Instance.LocaleChanged += OnLocaleChanged;
        }

        private void OnLocaleChanged()
        {
            ChangeLanguageMenuItem.ItemsSource = GenerateLanguageMenuItems();
            Menu.Close();
        }

        private IEnumerable<CheckBox> GenerateToggleFileTypeItems() =>
            Enum.GetValues<FileTypes>()
                .Select(it => (FileName: Enum.GetName(it)!, FileType: it))
                .Select(it =>
                    new CheckBox
                    {
                        Margin = new Thickness(10, 0, 0, 0),
                        Content = $".{it.FileName}",
                        IsChecked = it.FileType.GetConfigValue(ConfigurationState.Instance.UI.ShownFileTypes),
                        Command = Commands.Create(() => Window.ToggleFileType(it.FileName))
                    }
                );

        private static IEnumerable<MenuItem> GenerateLanguageMenuItems()
        {
            const string LanguagesPath = "Ryujinx/Assets/Languages.json";

            string languageJson = EmbeddedResources.ReadAllText(LanguagesPath);
            string currentLanguageCode = LocaleManager.Instance.CurrentLanguageCode;

            LanguagesJson languages = JsonHelper.Deserialize(languageJson, LanguagesJsonContext.Default.LanguagesJson);

            foreach ((string code, string language) in languages.Languages)
            {
                string languageName = string.IsNullOrEmpty(language) ? code : language;

                MenuItem menuItem = new()
                {
                    Padding = new Thickness(10, 0, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Header = code == currentLanguageCode ? $"{languageName}  ✔" : languageName,
                    Command = Commands.Create(() => MainWindowViewModel.ChangeLanguage(code))
                };

                yield return menuItem;
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            if (VisualRoot is MainWindow window)
            {
                Window = window;
                DataContext = ViewModel = window.ViewModel;
            }
        }

        public async Task OpenSettings()
        {
            Window.SettingsWindow = new(Window.VirtualFileSystem, Window.ContentManager);

            Rainbow.Enable();

            if (ViewModel.SelectedApplication is null) // Checks if game data exists
            {
                await StyleableAppWindow.ShowAsync(Window.SettingsWindow);
            }
            else
            {
                bool customConfigExists = File.Exists(Program.GetDirGameUserConfig(ViewModel.SelectedApplication.IdString));

                if (!ViewModel.IsGameRunning || !customConfigExists)
                {
                    await Window.SettingsWindow.ShowDialog(Window); // The game is not running, or if the user configuration does not exist
                }
                else
                {
                    // If there is a custom configuration in the folder
                    await StyleableAppWindow.ShowAsync(new GameSpecificSettingsWindow(ViewModel, customConfigExists));
                }
            }

            Rainbow.Disable();
            Rainbow.Reset();

            Window.SettingsWindow = null;

            ViewModel.LoadConfigurableHotKeys();
        }

        public AppletMetadata MiiEditor => new(ViewModel.ContentManager, LocaleManager.Instance[LocaleKeys.MenuBar_Actions_MiiEditorButton], 0x0100000000001009);

        public async Task OpenMiiEditor()
        {
            if (!MiiEditor.CanStart(out ApplicationData appData, out BlitStruct<ApplicationControlProperty> nacpData))
                return;

            await ViewModel.LoadApplication(appData, ViewModel.IsFullScreen || ViewModel.StartGamesInFullscreen, nacpData);
        }

        private void ScanAmiiboMenuItem_AttachedToVisualTree(object sender, VisualTreeAttachmentEventArgs e)
        {
            if (sender is MenuItem)
                ViewModel.IsAmiiboRequested = ViewModel.AppHost.Device.System.SearchingForAmiibo(out _);
        }

        private void ScanBinAmiiboMenuItem_AttachedToVisualTree(object sender, VisualTreeAttachmentEventArgs e)
        {
            if (sender is MenuItem)
                ViewModel.IsAmiiboBinRequested = ViewModel.IsAmiiboRequested && AmiiboBinReader.HasAmiiboKeyFile;
        }

        private void SkylanderMenuItem_AttachedToVisualTree(object sender, VisualTreeAttachmentEventArgs e)
        {
            if (sender is MenuItem)
            {
                ViewModel.IsSkylanderRequested = ViewModel.AppHost.Device.System.SearchingForSkylander(out _);
                ViewModel.HasSkylander = ViewModel.AppHost.Device.System.HasSkylander(out _);
            }
        }

        private void ChangeWindowSize(string resolution)
        {
            (int resolutionWidth, int resolutionHeight) = resolution.Split(' ', 2)
                .Into(parts =>
                    (int.Parse(parts[0]), int.Parse(parts[1]))
                );

            // Correctly size window when 'TitleBar' is enabled (Nov. 14, 2024)
            double barsHeight = ((Window.StatusBarHeight + Window.MenuBarHeight) +
                (ConfigurationState.Instance.ShowOldUI ? (int)Window.TitleBar.Height : 0));

            double windowWidthScaled = (resolutionWidth * Program.WindowScaleFactor);
            double windowHeightScaled = ((resolutionHeight + barsHeight) * Program.WindowScaleFactor);

            Dispatcher.UIThread.Post(() =>
            {
                ViewModel.WindowState = WindowState.Normal;

                Window.Arrange(new Rect(Window.Position.X, Window.Position.Y, windowWidthScaled, windowHeightScaled));
            });
        }

        // [Nextendo beta] Creates a no-account GUEST online profile: mints the identity on the
        // server (a real persistent PID so online/friends/sync work) and creates/binds the local
        // Ryujinx profile so the player visibly plays as that profile. Shared by the first-run
        // wizard and the Settings ▸ Nextendo page. Returns (ok, errorMessage).
        private static bool _creatingGuest;

        public static async Task<(bool ok, string message)> CreateGuestProfileAsync(string nickname)
        {
            // Serialize creation so a double-click can't mint two server-side guest identities
            // (the second Save would orphan the first PID). Clicks are on the UI thread.
            if (_creatingGuest)
            {
                return (false, "");
            }

            _creatingGuest = true;
            try
            {
                (bool ok, string err) = await Ryujinx.Ava.Common.NextendoApi.CreateGuestAsync(nickname);
                if (!ok)
                {
                    return (false, err);
                }

                try
                {
                    // reuseActiveProfile: rename the active local profile to the pseudo (so the
                    // Profile Manager + in-game show the guest identity, not the default "RyuPlayer").
                    await ApplyNextendoIdentity(
                        RyujinxApp.MainWindow?.AccountManager,
                        RyujinxApp.MainWindow?.ViewModel?.LibHacHorizonManager?.RyujinxClient,
                        NextendoAccount.Username,
                        NextendoAccount.NexToken,
                        reuseActiveProfile: true);
                }
                catch (Exception ex)
                {
                    // Identity is minted and saved; binding the local Ryujinx profile is best-effort.
                    Ryujinx.Common.Logging.Logger.Warning?.Print(
                        Ryujinx.Common.Logging.LogClass.Application,
                        $"[Nextendo] guest local profile bind failed: {ex.Message}");
                }

                // The guest is now a linked profile — clear any stale online block so it can play
                // online in THIS session without a restart (the launch gate also re-evaluates).
                NextendoAccount.OnlineBlocked = false;

                return (true, "");
            }
            finally
            {
                _creatingGuest = false;
            }
        }

        // ApplyNextendoIdentity ensures a dedicated Ryujinx user-profile exists for the
        // Nextendo account: created on first login (account pseudo + a random avatar, or
        // the profile saved on the account), reused afterwards, and set as the default /
        // current user — so launching Ryujinx shows your Nextendo identity — in this model:
        // the account is authoritative, so a profile saved on it is restored here.
        private static async Task<string> ApplyNextendoIdentity(Ryujinx.HLE.HOS.Services.Account.Acc.AccountManager am, LibHac.HorizonClient client, string username, string sessionToken, bool reuseActiveProfile = false)
        {
            if (am == null)
                return "";

            // 1. Pull the identity stored on the account (name + avatar + Mii), if any.
            string srvName = "", srvImage = "", srvMii = "";
            if (!string.IsNullOrEmpty(sessionToken))
            {
                try
                {
                    using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(12) };
                    http.DefaultRequestHeaders.Add("Authorization", "Bearer " + sessionToken);
                    HttpResponseMessage resp = await http.GetAsync(NextendoBaseUrl() + "/api/profile");
                    using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                    if (doc.RootElement.TryGetProperty("profile", out JsonElement prof) && prof.ValueKind == JsonValueKind.Object)
                    {
                        srvName = prof.TryGetProperty("name", out JsonElement n) ? (n.GetString() ?? "") : "";
                        srvImage = prof.TryGetProperty("image", out JsonElement im) ? (im.GetString() ?? "") : "";
                        srvMii = prof.TryGetProperty("mii", out JsonElement mi) ? (mi.GetString() ?? "") : "";
                    }
                }
                catch { /* offline or no profile yet — fall back to creating one */ }
            }

            // [Nextendo] Le pseudo du COMPTE (username) est l'unique source de vérité du nom
            // affiché, en jeu comme sur le site. On n'utilise plus Profile.Name (qui pouvait
            // diverger du pseudo) ; srvName n'est gardé que pour le backfill ci-dessous.
            string name = string.IsNullOrEmpty(username) ? "Nextendo" : username;

            byte[] image = null;
            if (!string.IsNullOrEmpty(srvImage))
            {
                try { image = Convert.FromBase64String(srvImage); } catch { image = null; }
            }
            image ??= GenerateRandomAvatar(name);

            // The account's Mii: use the one stored on the account, or generate a fresh
            // Mii on first login (it is then saved up to the account below).
            byte[] miiBytes = null;
            if (!string.IsNullOrEmpty(srvMii))
            {
                try { miiBytes = Convert.FromBase64String(srvMii); } catch { miiBytes = null; }
            }
            miiBytes ??= Ryujinx.HLE.HOS.Services.Mii.NextendoMiiSync.CreateMii();

            // 2. Find the Ryujinx profile bound to this account. For guests (reuseActiveProfile),
            //    RENAME the currently-active local profile instead of spawning a duplicate, so the
            //    Profile Manager + in-game identity become the chosen pseudo (e.g. "RyuPlayer" -> "Player").
            Ryujinx.HLE.HOS.Services.Account.Acc.UserProfile bound = FindBoundProfile(am);
            if (bound == null && reuseActiveProfile)
            {
                bound = am.LastOpenedUser;
            }

            Ryujinx.HLE.HOS.Services.Account.Acc.UserId uid;
            bool created = false;
            if (bound != null)
            {
                uid = bound.UserId;
                am.SetUserName(uid, name);
                am.SetUserImage(uid, image);
                NextendoAccount.SetProfileUserId(uid.ToString()); // bind (idempotent if already bound)
            }
            else
            {
                uid = new Ryujinx.HLE.HOS.Services.Account.Acc.UserId(Guid.NewGuid().ToString().Replace("-", string.Empty));
                am.AddUser(name, image, uid);
                NextendoAccount.SetProfileUserId(uid.ToString());
                created = true;
            }

            // 3. Make it the default / current user (persists to Profiles.json).
            am.OpenUser(uid);

            // 4. Inject the account's Mii into the local Mii database, and remember it
            //    locally so it can be removed again on logout.
            bool miiOk = false;
            if (client != null && miiBytes != null)
            {
                try
                {
                    miiOk = Ryujinx.HLE.HOS.Services.Mii.NextendoMiiSync.InjectMii(client, miiBytes);
                    if (miiOk)
                    {
                        NextendoAccount.SetMiiData(Convert.ToBase64String(miiBytes));
                    }
                }
                catch { /* best effort */ }
            }

            // 5. Save anything the account was missing (name / avatar / Mii) back up to it.
            bool needPush = string.IsNullOrEmpty(srvName) || string.IsNullOrEmpty(srvImage) || string.IsNullOrEmpty(srvMii);
            if (needPush && !string.IsNullOrEmpty(sessionToken))
            {
                try
                {
                    string miiB64 = miiBytes != null ? Convert.ToBase64String(miiBytes) : "";
                    using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(12) };
                    http.DefaultRequestHeaders.Add("Authorization", "Bearer " + sessionToken);
                    string putPayload = $"{{\"name\":{JsonSerializer.Serialize(name)},\"image\":{JsonSerializer.Serialize(Convert.ToBase64String(image))},\"mii\":{JsonSerializer.Serialize(miiB64)}}}";
                    using StringContent putBody = new(putPayload, Encoding.UTF8, "application/json");
                    await http.PutAsync(NextendoBaseUrl() + "/api/profile", putBody);
                }
                catch { /* best effort */ }
            }

            string miiMsg = miiOk ? "\n" + LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ApplyIdentityMiiSyncedMessage] : "";
            return ("\n" + (created
                ? LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.Dialog_Nextendo_ApplyIdentityProfileCreatedFormat, name)
                : LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.Dialog_Nextendo_ApplyIdentityProfileSyncedFormat, name))) + miiMsg;
        }

        private static string NextendoBaseUrl()
        {
            // [Nextendo] Une seule decision, dans NextendoEndpoint : c'est elle qui choisit qui
            // recoit le jeton du compte. Cette logique etait dupliquee ici et acceptait
            // n'importe quelle valeur de NEXTENDO_API.
            return NextendoEndpoint.BaseUrl();
        }

        // GenerateRandomAvatar builds a pleasant random profile picture (a diagonal
        // colour gradient + a soft "head" highlight) as 256×256 JPEG bytes — fully
        // self-contained (no firmware needed). SkiaSharp ships with Ryujinx.
        private static byte[] GenerateRandomAvatar(string name)
        {
            const int size = 256;
            int seed = (name ?? string.Empty).GetHashCode() ^ Environment.TickCount;
            Random rnd = new(seed);

            float h = (float)rnd.NextDouble() * 360f;
            SKColor top = SKColor.FromHsv(h, 60f, 80f);
            SKColor bottom = SKColor.FromHsv((h + 28f) % 360f, 72f, 55f);

            using SKSurface surface = SKSurface.Create(new SKImageInfo(size, size));
            SKCanvas canvas = surface.Canvas;

            using (SKPaint grad = new())
            {
                grad.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0), new SKPoint(size, size),
                    new[] { top, bottom }, null, SKShaderTileMode.Clamp);
                canvas.DrawRect(new SKRect(0, 0, size, size), grad);
            }

            using (SKPaint silhouette = new() { Color = new SKColor(255, 255, 255, 46), IsAntialias = true })
            {
                canvas.DrawCircle(size / 2f, size * 0.40f, size * 0.18f, silhouette);              // head
                canvas.DrawOval(new SKRect(size * 0.24f, size * 0.60f, size * 0.76f, size * 1.06f), silhouette); // shoulders
            }

            using SKImage img = surface.Snapshot();
            using SKData data = img.Encode(SKEncodedImageFormat.Jpeg, 90);
            return data.ToArray();
        }

        private static Ryujinx.HLE.HOS.Services.Account.Acc.UserProfile FindBoundProfile(Ryujinx.HLE.HOS.Services.Account.Acc.AccountManager am)
        {
            string stored = NextendoAccount.ProfileUserId;
            if (string.IsNullOrEmpty(stored))
                return null;

            foreach (Ryujinx.HLE.HOS.Services.Account.Acc.UserProfile p in am.GetAllUsers())
            {
                if (p.UserId.ToString() == stored)
                    return p;
            }
            return null;
        }

        // [Nextendo] "Connexion Nextendo Network": log into a Nextendo account via the
        // account service, store it (NextendoAccount), so the next game launch presents
        // this account's persistent PID to the private NEX server (= play online as you).
        // Entry point for the "Compte Nextendo Network" menu item: shows the account
        // settings panel when linked, or the login dialog when not.
        public async Task OpenNextendoAccount()
        {
            if (NextendoAccount.IsLinked)
            {
                await ShowNextendoAccountPanel();
                return;
            }

            // [Nextendo] Sign in through the browser (OAuth loopback + PKCE) — the emulator never
            // handles the e-mail/password. Opens nextendo.network, waits for the loopback callback,
            // then links the account. Password login on /api/login stays website-only (Turnstile).
            Task<(bool ok, string error)> signIn = Ryujinx.Ava.Common.NextendoApi.SignInWithBrowserAsync();

            // Tell the user where the flow went: a page just opened in their browser, and that is
            // where they finish — without this popup people just see the emulator "do nothing".
            ContentDialog waiting = new()
            {
                Title = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_LoginDialogTitle],
                Content = new TextBlock
                {
                    Text = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_OAuthBrowserOpened],
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    MaxWidth = 360,
                },
                CloseButtonText = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_DialogCloseButton],
            };
            // Close the waiting popup automatically once the browser flow finishes.
            _ = signIn.ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(waiting.Hide), TaskScheduler.Default);
            await ContentDialogHelper.ShowAsync(waiting);

            if (!signIn.IsCompleted)
            {
                return; // user closed the popup before finishing in the browser
            }

            (bool ok, string error) = await signIn;
            if (ok)
            {
                await ShowNextendoAccountPanel();
            }
            else if (!string.IsNullOrEmpty(error))
            {
                await ContentDialogHelper.CreateErrorDialog(error);
            }
        }

        // Settings panel shown when a Nextendo account is already linked.
        private async Task ShowNextendoAccountPanel()
        {
            // Profile photo — the same picture the website and your friends see of you.
            byte[] avatar = null;
            try { (_, avatar) = await Ryujinx.Ava.Common.NextendoApi.GetProfileSyncAsync(); } catch { /* cosmetic */ }

            TextBlock status = new() { Text = "", TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 360, IsVisible = false };

            StackPanel panel = new()
            {
                Spacing = 10,
                MinWidth = 360,
                Children =
                {
                    new TextBlock { Text = LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.Dialog_Nextendo_AccountPanelConnectedAsFormat, NextendoAccount.Username), FontWeight = Avalonia.Media.FontWeight.Bold, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new TextBlock { Text = LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.Dialog_Nextendo_AccountPanelFriendCodeFormat, NextendoAccount.FriendCode), TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new TextBlock { Text = LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.Dialog_Nextendo_AccountPanelNetworkIdFormat, NextendoAccount.Pid), Foreground = Avalonia.Media.Brushes.Gray, FontSize = 12 },
                    MakeOpenSiteButton(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_AccountPanelOpenAccountButton], "/dashboard"),
                    status,
                },
            };

            if (avatar is { Length: > 0 })
            {
                panel.Children.Insert(0, new Avalonia.Controls.Border
                {
                    Width = 76,
                    Height = 76,
                    CornerRadius = new Avalonia.CornerRadius(38),
                    ClipToBounds = true,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Child = new Avalonia.Controls.Image
                    {
                        Source = new Avalonia.Media.Imaging.Bitmap(new System.IO.MemoryStream(avatar)),
                        Stretch = Avalonia.Media.Stretch.UniformToFill,
                    },
                });
            }

            ContentDialog dialog = new()
            {
                Title = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_AccountPanelDialogTitle],
                Content = panel,
                PrimaryButtonText = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_DialogCloseButton],
                SecondaryButtonText = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_DialogSignOutButton],
                DefaultButton = ContentDialogButton.Primary,
            };

            // Disconnect inline (a second dialog after this one closes does not render
            // reliably): clear the link, confirm in-place, keep the dialog open.
            dialog.SecondaryButtonClick += (sender, args) =>
            {
                args.Cancel = true;

                // Remove the Nextendo-created profile (its data lives on the account and
                // is re-downloaded on the next login). Never touch the default profile.
                try
                {
                    var am = Window?.AccountManager;
                    if (am != null)
                    {
                        var bound = FindBoundProfile(am);
                        if (bound != null && bound.UserId != Ryujinx.HLE.HOS.Services.Account.Acc.AccountManager.DefaultUserId)
                        {
                            am.DeleteUser(bound.UserId);
                        }
                    }
                }
                catch { /* best effort */ }

                // Remove the account's Mii from the local Mii database.
                try
                {
                    var client = ViewModel?.LibHacHorizonManager?.RyujinxClient;
                    string miiB64 = NextendoAccount.MiiData;
                    if (client != null && !string.IsNullOrEmpty(miiB64))
                    {
                        Ryujinx.HLE.HOS.Services.Mii.NextendoMiiSync.RemoveMii(client, Convert.FromBase64String(miiB64));
                    }
                }
                catch { /* best effort */ }

                NextendoAccount.Clear();
                status.IsVisible = true;
                status.Foreground = Avalonia.Media.Brushes.LightGreen;
                status.Text = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_AccountSignedOutMessage];
                sender.IsSecondaryButtonEnabled = false;
            };

            await ContentDialogHelper.ShowAsync(dialog);
        }

        // Login dialog shown when no Nextendo account is linked.
        private async Task ShowNextendoLoginDialog()
        {
            TextBox loginBox = new() { Watermark = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_LoginDialogUsernameOrEmailWatermark], Width = 320 };
            Control passRow = MakePasswordRow(out TextBox passBox);
            StackPanel panel = new()
            {
                Spacing = 12,
                MinWidth = 340,
                Children =
                {
                    new TextBlock { Text = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_LoginDialogInstructions], TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 360 },
                    loginBox,
                    passRow,
                    MakeOpenSiteButton(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_LoginDialogCreateAccountButton], "/register"),
                },
            };

            TextBlock status = new()
            {
                Text = "",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                MaxWidth = 360,
                IsVisible = false,
            };
            panel.Children.Add(status);

            ContentDialog dialog = new()
            {
                Title = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_LoginDialogTitle],
                Content = panel,
                PrimaryButtonText = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_LoginDialogSignInButton],
                CloseButtonText = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_LoginDialogCloseButton],
                DefaultButton = ContentDialogButton.Primary,
            };

            // Log in on the primary button WITHOUT closing, showing the result INLINE.
            // (A second ContentDialog opened after this one closes does not render
            // reliably — that was the "no confirmation/no error" bug.) The deferral
            // keeps the dialog open during the async login.
            dialog.PrimaryButtonClick += async (sender, args) =>
            {
                var deferral = args.GetDeferral();
                args.Cancel = true; // keep the dialog open to show the result

                status.IsVisible = true;
                status.Foreground = Avalonia.Media.Brushes.Gray;
                status.Text = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_LoginDialogConnecting];

                try
                {
                    (bool ok, string name, string friendCode, string sessionToken, string error) =
                        await NextendoApiLogin(loginBox.Text?.Trim() ?? "", passBox.Text ?? "");

                    if (ok)
                    {
                        string idMsg = "";
                        try { idMsg = await ApplyNextendoIdentity(Window?.AccountManager, ViewModel?.LibHacHorizonManager?.RyujinxClient, name, sessionToken); }
                        catch (Exception ex) { idMsg = " (profil : " + ex.Message + ")"; }

                        status.Foreground = Avalonia.Media.Brushes.LightGreen;
                        status.Text = LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.Dialog_Nextendo_LoginDialogSuccessFormat, name, friendCode, idMsg);
                        sender.IsPrimaryButtonEnabled = false;
                    }
                    else
                    {
                        status.Foreground = Avalonia.Media.Brushes.IndianRed;
                        status.Text = "✗ " + error;
                    }
                }
                catch (Exception ex)
                {
                    status.Foreground = Avalonia.Media.Brushes.IndianRed;
                    status.Text = LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.Dialog_Nextendo_LoginDialogServiceUnreachableFormat, ex.Message);
                }

                deferral.Complete();
            };

            await ContentDialogHelper.ShowAsync(dialog);
        }

        // A button (placed inside a dialog) that opens the Nextendo website at a path.
        private static Button MakeOpenSiteButton(string text, string path)
        {
            Button btn = new() { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch };
            btn.Click += (_, _) =>
            {
                try { OpenHelper.OpenUrl(NextendoBaseUrl() + path); } catch { /* ignore */ }
            };
            return btn;
        }

        // Password field + an eye toggle to reveal/hide the typed password.
        private static Control MakePasswordRow(out TextBox passBox)
        {
            TextBox box = new() { Watermark = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_LoginDialogPasswordWatermark], PasswordChar = '•', Width = 320 };
            Grid.SetColumn(box, 0);

            PathIcon eyeIcon = new()
            {
                Width = 18,
                Height = 18,
                Opacity = 0.55,
                Data = Avalonia.Media.Geometry.Parse("M12 4.5C7 4.5 2.7 7.6 1 12c1.7 4.4 6 7.5 11 7.5s9.3-3.1 11-7.5C21.3 7.6 17 4.5 12 4.5zm0 12.5a5 5 0 1 1 0-10 5 5 0 0 1 0 10zm0-8a3 3 0 1 0 0 6 3 3 0 0 0 0-6z"),
            };

            Button eyeBtn = new()
            {
                Content = eyeIcon,
                Width = 40,
                Margin = new Thickness(6, 0, 0, 0),
                Background = Avalonia.Media.Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            Grid.SetColumn(eyeBtn, 1);

            bool shown = false;
            eyeBtn.Click += (_, _) =>
            {
                shown = !shown;
                box.PasswordChar = shown ? '\0' : '•';
                eyeIcon.Opacity = shown ? 1.0 : 0.55;
            };

            Grid row = new() { ColumnDefinitions = new ColumnDefinitions("Auto,Auto") };
            row.Children.Add(box);
            row.Children.Add(eyeBtn);

            passBox = box;

            return row;
        }

        private static async Task<(bool ok, string name, string friendCode, string sessionToken, string error)> NextendoApiLogin(string loginId, string password)
        {
            if (loginId.Length == 0 || password.Length == 0)
                return (false, "", "", "", LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_LoginDialogEmptyFieldsError]);

            using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(12) };
            string payload = $"{{\"login\":{JsonSerializer.Serialize(loginId)},\"password\":{JsonSerializer.Serialize(password)}}}";
            using StringContent body = new(payload, Encoding.UTF8, "application/json");

            HttpResponseMessage resp = await http.PostAsync(NextendoBaseUrl() + "/api/login", body);
            string json = await resp.Content.ReadAsStringAsync();

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (!resp.IsSuccessStatusCode)
            {
                string err = root.TryGetProperty("error", out JsonElement e) ? (e.GetString() ?? LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_LoginDialogInvalidCredentialsError]) : LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_LoginDialogInvalidCredentialsError];
                return (false, "", "", "", err);
            }

            JsonElement acct = root.GetProperty("account");
            ulong pid = acct.GetProperty("pid").GetUInt64();
            string username = acct.TryGetProperty("username", out JsonElement u) ? (u.GetString() ?? "") : "";
            string friendCode = acct.TryGetProperty("friend_code", out JsonElement f) ? (f.GetString() ?? "") : "";
            string nexToken = root.TryGetProperty("nex_token", out JsonElement nt) ? (nt.GetString() ?? "") : "";
            string sessionToken = root.TryGetProperty("token", out JsonElement st) ? (st.GetString() ?? "") : "";

            NextendoAccount.Save(pid, username, friendCode, nexToken);
            return (true, username, friendCode, sessionToken, "");
        }
    }
}
