using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Common.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.Windows
{
    /// <summary>
    /// [Nextendo beta] First-launch quick-start. Walks a new user through the few things they
    /// need to play online: pick the games folder, add prod.keys, (optionally) firmware, and
    /// create a no-account online (guest) profile — with plain-language explanations of how the
    /// beta works. Shown once from <see cref="MainWindow"/> when the install looks fresh.
    /// </summary>
    public sealed class NextendoFirstRunWindow : StyleableAppWindow
    {
        private const string FlagFileName = "nextendo_setup_done";

        private int _step;
        private readonly List<Func<Control>> _stepBuilders;
        private readonly ContentControl _body;
        private readonly Button _backButton;
        private readonly Button _nextButton;
        private readonly TextBlock _stepIndicator;

        // Collected/onboarding state (persists across step rebuilds).
        private string _gamesFolder = "";
        private string _updatesFolder = "";
        private bool _keysInstalled;
        private string _nickname = "";
        private bool _profileCreated;

        // Per-step controls we update after async work.
        private TextBlock _gamesStatus;
        private TextBlock _updatesStatus;
        private TextBlock _keysStatus;
        private TextBox _nicknameBox;
        private TextBlock _profileStatus;

        public NextendoFirstRunWindow()
        {
            Title = L(LocaleKeys.Dialog_Nextendo_WizardWindowTitle);
            Width = 660;
            Height = 580;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _stepBuilders = new List<Func<Control>>
            {
                BuildWelcome,
                BuildGames,
                BuildUpdates,
                BuildKeys,
                BuildFirmware,
                BuildProfile,
                BuildFinish,
            };

            _body = new ContentControl { Margin = new Thickness(30, 22) };

            ScrollViewer scroll = new()
            {
                Content = _body,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            };

            _stepIndicator = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.6,
                FontSize = 12,
            };

            _backButton = new Button { Content = L(LocaleKeys.Dialog_Nextendo_WizardBack), Padding = new Thickness(18, 8) };
            _nextButton = new Button { Content = L(LocaleKeys.Dialog_Nextendo_WizardNext), Padding = new Thickness(18, 8) };
            _backButton.Click += (_, _) => Go(-1);
            _nextButton.Click += async (_, _) => await OnNextAsync();

            Grid bottomBar = new()
            {
                Margin = new Thickness(30, 12, 30, 18),
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            };
            Grid.SetColumn(_stepIndicator, 0);
            StackPanel navButtons = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
            navButtons.Children.Add(_backButton);
            navButtons.Children.Add(_nextButton);
            Grid.SetColumn(navButtons, 3);
            bottomBar.Children.Add(_stepIndicator);
            bottomBar.Children.Add(navButtons);

            DockPanel root = new();
            DockPanel.SetDock(bottomBar, Dock.Bottom);
            root.Children.Add(bottomBar);
            root.Children.Add(scroll);

            Content = root;
            ShowStep();
        }

        private static string L(LocaleKeys key) => LocaleManager.Instance[key];

        // ---- step navigation ----

        private void ShowStep()
        {
            // Drop refs to the previous step's controls so a late async callback (picker / guest
            // creation) can't write to an orphaned, off-screen control. The builder for the new
            // step reassigns whichever it needs.
            _gamesStatus = null;
            _updatesStatus = null;
            _keysStatus = null;
            _nicknameBox = null;
            _profileStatus = null;

            _body.Content = _stepBuilders[_step]();
            _stepIndicator.Text = $"{_step + 1} / {_stepBuilders.Count}";
            _backButton.IsEnabled = _step > 0;
            _nextButton.Content = _step == _stepBuilders.Count - 1
                ? L(LocaleKeys.Dialog_Nextendo_WizardFinish)
                : L(LocaleKeys.Dialog_Nextendo_WizardNext);
        }

        private void Go(int delta)
        {
            int next = _step + delta;
            if (next < 0 || next >= _stepBuilders.Count)
            {
                return;
            }

            _step = next;
            ShowStep();
        }

        private async Task OnNextAsync()
        {
            if (_step == _stepBuilders.Count - 1)
            {
                MarkDone();
                Close();
                return;
            }

            await Task.CompletedTask;
            Go(1);
        }

        // ---- step builders ----

        private static TextBlock Heading(string text) => new()
        {
            Text = text,
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };

        private static TextBlock Subheading(string text) => new()
        {
            Text = text,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 6),
        };

        private static TextBlock Para(string text) => new()
        {
            Text = text,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.9,
            Margin = new Thickness(0, 0, 0, 8),
        };

        private static StackPanel Stack(params Control[] children)
        {
            StackPanel sp = new() { Spacing = 2 };
            foreach (Control c in children)
            {
                sp.Children.Add(c);
            }

            return sp;
        }

        private static Button ActionButton(string text) => new()
        {
            Content = text,
            Padding = new Thickness(16, 8),
            Margin = new Thickness(0, 8, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        private Control BuildWelcome() => Stack(
            Heading(L(LocaleKeys.Dialog_Nextendo_WizardWelcomeTitle)),
            Para(L(LocaleKeys.Dialog_Nextendo_WizardWelcomeBody)),
            Subheading(L(LocaleKeys.Dialog_Nextendo_WizardSupportedGames)));

        private Control BuildGames()
        {
            Button btn = ActionButton(L(LocaleKeys.Dialog_Nextendo_WizardGamesButton));
            btn.Click += async (_, _) => await PickGamesFolderAsync();

            _gamesStatus = new TextBlock
            {
                Text = string.IsNullOrEmpty(_gamesFolder) ? "" : L(LocaleKeys.Dialog_Nextendo_WizardGamesSelected) + " " + _gamesFolder,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
                FontSize = 13,
            };

            return Stack(
                Heading(L(LocaleKeys.Dialog_Nextendo_WizardGamesTitle)),
                Para(L(LocaleKeys.Dialog_Nextendo_WizardGamesBody)),
                btn,
                _gamesStatus);
        }

        private Control BuildUpdates()
        {
            Button btn = ActionButton(L(LocaleKeys.Dialog_Nextendo_WizardUpdatesButton));
            btn.Click += async (_, _) => await PickUpdatesFolderAsync();

            _updatesStatus = new TextBlock
            {
                Text = string.IsNullOrEmpty(_updatesFolder) ? "" : L(LocaleKeys.Dialog_Nextendo_WizardUpdatesSelected) + " " + _updatesFolder,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
                FontSize = 13,
            };

            return Stack(
                Heading(L(LocaleKeys.Dialog_Nextendo_WizardUpdatesTitle)),
                Para(L(LocaleKeys.Dialog_Nextendo_WizardUpdatesBody)),
                btn,
                _updatesStatus);
        }

        private Control BuildKeys()
        {
            Button btn = ActionButton(L(LocaleKeys.Dialog_Nextendo_WizardKeysButton));
            btn.Click += async (_, _) => await PickKeysAsync();

            _keysStatus = new TextBlock
            {
                Text = _keysInstalled ? L(LocaleKeys.Dialog_Nextendo_WizardKeysInstalled) : L(LocaleKeys.Dialog_Nextendo_WizardKeysMissing),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
                FontSize = 13,
            };

            return Stack(
                Heading(L(LocaleKeys.Dialog_Nextendo_WizardKeysTitle)),
                Para(L(LocaleKeys.Dialog_Nextendo_WizardKeysBody)),
                btn,
                _keysStatus);
        }

        private Control BuildFirmware() => Stack(
            Heading(L(LocaleKeys.Dialog_Nextendo_WizardFirmwareTitle)),
            Para(L(LocaleKeys.Dialog_Nextendo_WizardFirmwareBody)));

        private Control BuildProfile()
        {
            // [Nextendo] Un compte Nextendo est OBLIGATOIRE pour jouer en ligne (le mode invité,
            // qui n'existait que pour la beta, a été retiré). On oriente vers la création/connexion
            // du compte sur nextendo.network.
            Button openSite = ActionButton(L(LocaleKeys.Dialog_Nextendo_GuestRegisterFullAccount));
            openSite.Click += (_, _) =>
            {
                try { Ryujinx.Common.Helper.OpenHelper.OpenUrl("https://nextendo.network/register"); }
                catch { /* best-effort */ }
            };

            return Stack(
                Heading(L(LocaleKeys.Dialog_Nextendo_WizardProfileTitle)),
                Para(L(LocaleKeys.Dialog_Nextendo_WizardProfileBody)),
                openSite);
        }

        private Control BuildFinish() => Stack(
            Heading(L(LocaleKeys.Dialog_Nextendo_WizardFinishTitle)),
            Para(L(LocaleKeys.Dialog_Nextendo_WizardFinishBody)));

        // ---- actions ----

        private async Task PickGamesFolderAsync()
        {
            if (StorageProvider is null)
            {
                return;
            }

            try
            {
                IReadOnlyList<IStorageFolder> result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = L(LocaleKeys.Dialog_Nextendo_WizardGamesButton),
                    AllowMultiple = false,
                });

                if (result is not { Count: > 0 })
                {
                    return;
                }

                string path = result[0].Path.LocalPath;
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                _gamesFolder = path;

                List<string> dirs = new(ConfigurationState.Instance.UI.GameDirs.Value ?? new List<string>());
                if (!dirs.Contains(path))
                {
                    dirs.Add(path);
                }

                ConfigurationState.Instance.UI.GameDirs.Value = dirs;
                SaveConfig();

                try { RyujinxApp.MainWindow?.LoadApplications(); } catch { /* refresh is best-effort */ }

                if (_gamesStatus != null)
                {
                    _gamesStatus.Text = L(LocaleKeys.Dialog_Nextendo_WizardGamesSelected) + " " + path;
                }
            }
            catch { /* picker cancelled / unavailable */ }
        }

        private async Task PickUpdatesFolderAsync()
        {
            if (StorageProvider is null)
            {
                return;
            }

            try
            {
                IReadOnlyList<IStorageFolder> result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = L(LocaleKeys.Dialog_Nextendo_WizardUpdatesButton),
                    AllowMultiple = false,
                });

                if (result is not { Count: > 0 })
                {
                    return;
                }

                string path = result[0].Path.LocalPath;
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                _updatesFolder = path;

                List<string> dirs = new(ConfigurationState.Instance.UI.AutoloadDirs.Value ?? new List<string>());
                if (!dirs.Contains(path))
                {
                    dirs.Add(path);
                }

                ConfigurationState.Instance.UI.AutoloadDirs.Value = dirs;
                SaveConfig();

                try { RyujinxApp.MainWindow?.LoadApplications(); } catch { /* refresh is best-effort */ }

                if (_updatesStatus != null)
                {
                    _updatesStatus.Text = L(LocaleKeys.Dialog_Nextendo_WizardUpdatesSelected) + " " + path;
                }
            }
            catch { /* picker cancelled / unavailable */ }
        }

        private async Task PickKeysAsync()
        {
            if (StorageProvider is null)
            {
                return;
            }

            try
            {
                IReadOnlyList<IStorageFile> result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = L(LocaleKeys.Dialog_Nextendo_WizardKeysButton),
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("prod.keys") { Patterns = new[] { "prod.keys", "*.keys" } },
                    },
                });

                if (result is not { Count: > 0 })
                {
                    return;
                }

                string src = result[0].Path.LocalPath;
                if (string.IsNullOrEmpty(src) || !File.Exists(src))
                {
                    return;
                }

                // A real prod.keys is a sizeable list of "name = hex" lines; reject obviously wrong files.
                if (new FileInfo(src).Length < 1024)
                {
                    if (_keysStatus != null)
                    {
                        _keysStatus.Text = L(LocaleKeys.Dialog_Nextendo_WizardKeysInvalid);
                    }

                    return;
                }

                Directory.CreateDirectory(AppDataManager.KeysDirPath);
                File.Copy(src, Path.Combine(AppDataManager.KeysDirPath, "prod.keys"), overwrite: true);
                _keysInstalled = true;

                if (_keysStatus != null)
                {
                    _keysStatus.Text = L(LocaleKeys.Dialog_Nextendo_WizardKeysInstalled);
                }
            }
            catch (Exception ex)
            {
                if (_keysStatus != null)
                {
                    _keysStatus.Text = ex.Message;
                }
            }
        }

        // Live "available / already taken" feedback when the nickname field loses focus.
        private async Task CheckNicknameAvailability()
        {
            string nick = (_nicknameBox?.Text ?? "").Trim();
            if (nick.Length < 3 || nick.Length > 16)
            {
                SetProfileStatus(nick.Length > 0 ? L(LocaleKeys.Dialog_Nextendo_GuestProfileNicknameInvalid) : "", ok: false);
                return;
            }

            SetProfileStatus(L(LocaleKeys.Dialog_Nextendo_GuestProfileChecking), ok: true);
            bool? avail = await Ryujinx.Ava.Common.NextendoApi.CheckNicknameAvailableAsync(nick);
            if (avail == true)
            {
                SetProfileStatus(L(LocaleKeys.Dialog_Nextendo_GuestProfileNicknameAvailable), ok: true);
            }
            else if (avail == false)
            {
                SetProfileStatus(L(LocaleKeys.Dialog_Nextendo_GuestProfileNicknameTaken), ok: false);
            }
            else
            {
                SetProfileStatus("", ok: true);
            }
        }

        private async Task CreateProfileAsync()
        {
            string nick = (_nicknameBox?.Text ?? "").Trim();
            if (nick.Length < 3 || nick.Length > 16)
            {
                SetProfileStatus(L(LocaleKeys.Dialog_Nextendo_GuestProfileNicknameInvalid), ok: false);
                return;
            }

            _nickname = nick;
            SetProfileStatus(L(LocaleKeys.Dialog_Nextendo_GuestProfileChecking), ok: true);

            // Explicit availability check first: a taken name is reported clearly and NOT created.
            bool? avail = await Ryujinx.Ava.Common.NextendoApi.CheckNicknameAvailableAsync(nick);
            if (avail == false)
            {
                SetProfileStatus(L(LocaleKeys.Dialog_Nextendo_GuestProfileNicknameTaken), ok: false);
                return;
            }

            (bool ok, string err) = await Views.Main.MainMenuBarView.CreateGuestProfileAsync(nick);
            if (ok)
            {
                _profileCreated = true;
                SetProfileStatus("✓ " + NextendoAccount.Username, ok: true);
            }
            else
            {
                SetProfileStatus(string.IsNullOrEmpty(err) ? L(LocaleKeys.Dialog_Nextendo_GuestProfileCreateFailed) : err, ok: false);
            }
        }

        private void SetProfileStatus(string text, bool ok)
        {
            if (_profileStatus != null)
            {
                _profileStatus.Text = text;
                _profileStatus.Foreground = new SolidColorBrush(ok ? Color.Parse("#3EE8C8") : Color.Parse("#FF6B6B"));
            }
        }

        private static void SaveConfig()
        {
            try
            {
                ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.ConfigurationPath);
            }
            catch { /* best effort */ }
        }

        // ---- first-run detection ----

        /// <summary>True when the install looks fresh: no game folders configured and no Nextendo
        /// identity yet, and the wizard hasn't already been completed.</summary>
        public static bool IsFirstRun()
        {
            try
            {
                // Driven solely by the completion flag: a fresh install has none (wizard shows),
                // finishing writes it, and deleting nextendo_setup_done re-triggers the wizard.
                return !File.Exists(Path.Combine(AppDataManager.BaseDirPath, FlagFileName));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Opens the quick-start wizard on demand (e.g. a "re-run setup" button), regardless
        /// of the completion flag.</summary>
        public static System.Threading.Tasks.Task ShowAsync(Window owner)
        {
            return new NextendoFirstRunWindow().ShowDialog(owner);
        }

        private static void MarkDone()
        {
            try
            {
                File.WriteAllText(Path.Combine(AppDataManager.BaseDirPath, FlagFileName), "1");
            }
            catch { /* best effort */ }
        }
    }
}
