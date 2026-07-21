using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using FluentAvalonia.UI.Controls;
using Ryujinx.Ava;
using Ryujinx.Ava.Common;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Systems.AppLibrary;
using Ryujinx.Ava.UI.Views.Main;
using Ryujinx.Ava.UI.Controls;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.Models;
using Ryujinx.Ava.UI.ViewModels;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Helper;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using AccAccountManager = Ryujinx.HLE.HOS.Services.Account.Acc.AccountManager;
using AccUserProfile = Ryujinx.HLE.HOS.Services.Account.Acc.UserProfile;

namespace Ryujinx.Ava.UI.Views.Settings
{
    public partial class SettingsNextendoView : RyujinxControl<SettingsViewModel>
    {
        private readonly ObservableCollection<NextendoFriendModel> _friends = [];
        private readonly ObservableCollection<NextendoFriendModel> _requests = [];
        private readonly ObservableCollection<NextendoHistoryModel> _history = [];
        private bool _loaded;

        public SettingsNextendoView()
        {
            InitializeComponent();

            FriendsList.ItemsSource = _friends;
            RequestsList.ItemsSource = _requests;
            HistoryList.ItemsSource = _history;

            OpenSiteButton.Click += (_, _) => TryOpenSite("/register");
            CreateGuestButton.Click += async (_, _) => await CreateGuest();
            GuestNicknameBox.LostFocus += async (_, _) => await CheckNicknameAvailability();
            RerunWizardButton.Click += async (_, _) =>
            {
                if (RyujinxApp.MainWindow is { } mw)
                {
                    await Ryujinx.Ava.UI.Windows.NextendoFirstRunWindow.ShowAsync(mw);
                }
            };
            ChangePseudoButton.Click += async (_, _) => await ChangePseudo();
            ChangePpButton.Click += async (_, _) => await ChangePp();
            AddFriendButton.Click += async (_, _) => await AddFriend();

            // [Nextendo] Credits, localised through the normal locale system. These used to be a
            // hand-rolled EN/FR toggle, so every other language fell back to English.
            CreditsDescText.Text = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_CreditsDesc];
            CreditsRoleText.Text = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_CreditsRole];
            CreditsRyujinxText.Text = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_CreditsRyujinx];
            NextendoVersionText.Text = $"Ryujinx-Nextendo — Version {Ryujinx.Common.ReleaseInformation.Version}";
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            if (_loaded)
            {
                return;
            }

            _loaded = true;
            Refresh();
        }

        private void Refresh()
        {
            bool linked = NextendoAccount.IsLinked;
            NotLinkedPanel.IsVisible = !linked;
            LinkedPanel.IsVisible = linked;

            if (!linked)
            {
                // No-account GUEST creation UI (localized EN/FR via the UI language).
                GuestExplainText.Text = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_GuestProfileExplain];
                GuestNicknameBox.Watermark = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_GuestProfileNicknameLabel];
                CreateGuestButton.Content = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_GuestProfileCreateButton];
                OpenSiteButton.Content = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_GuestRegisterFullAccount];
                return;
            }

            UsernameText.Text = NextendoAccount.Username;
            FriendCodeText.Text = NextendoAccount.FriendCode;
            PidText.Text = "PID " + NextendoAccount.Pid;
            PseudoBox.Text = NextendoAccount.Username;

            try
            {
                AccUserProfile prof = FindBoundProfile(RyujinxApp.MainWindow?.AccountManager);
                if (prof?.Image is { Length: > 0 })
                {
                    PpImage.Source = new Bitmap(new MemoryStream(prof.Image));
                }
            }
            catch { /* ignore */ }

            // [Nextendo] Le compte est la SOURCE DE VÉRITÉ : à l'ouverture on TIRE le pseudo + la
            // photo du compte vers le profil local, pour qu'un changement fait sur le site (ou
            // ailleurs) apparaisse SANS avoir à se déconnecter/reconnecter.
            _ = PullAccountProfile();

            _ = LoadSocial();
            _ = LoadHistory();
        }

        private async Task PullAccountProfile()
        {
            try
            {
                (string name, byte[] image) = await NextendoApi.GetProfileSyncAsync();
                var am = RyujinxApp.MainWindow?.AccountManager;
                AccUserProfile prof = FindBoundProfile(am);

                // Show what the account says FIRST, and unconditionally. This used to return early
                // when no local profile matched the bound uuid, which threw away the name and
                // picture it had just fetched — so a portable folder whose Profiles.json lacks that
                // uuid showed a blank avatar while the website showed the real one.
                if (!string.IsNullOrEmpty(name))
                {
                    UsernameText.Text = name;
                    NextendoAccount.Save(NextendoAccount.Pid, name, NextendoAccount.FriendCode, NextendoAccount.NexToken, isGuest: NextendoAccount.IsGuest);
                }

                if (image is { Length: > 0 })
                {
                    PpImage.Source = new Bitmap(new MemoryStream(image));
                }

                // Mirroring onto the local Switch profile is a bonus, not a prerequisite: it only
                // applies when this install actually has the linked profile.
                if (am == null || prof == null)
                {
                    return;
                }

                if (!string.IsNullOrEmpty(name))
                {
                    am.SetUserName(prof.UserId, name);
                }

                if (image is { Length: > 0 })
                {
                    am.SetUserImage(prof.UserId, image);
                }
            }
            catch { /* best effort */ }
        }

        private async Task LoadSocial()
        {
            (List<NextendoApi.Friend> friends, List<NextendoApi.Friend> requests) = await NextendoApi.GetSocialAsync();

            Fill(_friends, friends);
            Fill(_requests, requests);

            NoFriendsText.IsVisible = _friends.Count == 0;
            RequestsPanel.IsVisible = _requests.Count > 0;
        }

        private static void Fill(ObservableCollection<NextendoFriendModel> target, List<NextendoApi.Friend> source)
        {
            target.Clear();
            foreach (NextendoApi.Friend f in source)
            {
                byte[] img = null;
                if (!string.IsNullOrEmpty(f.ImageBase64))
                {
                    try { img = Convert.FromBase64String(f.ImageBase64); } catch { /* ignore */ }
                }

                target.Add(new NextendoFriendModel
                {
                    Pid = f.Pid,
                    Name = f.Name,
                    FriendCode = f.FriendCode,
                    Image = img,
                    OnlineStatus = f.OnlineStatus,
                    AppId = f.AppId,
                    AppDetail = f.AppDetail,
                });
            }
        }

        private async Task LoadHistory()
        {
            List<NextendoApi.HistoryItem> merged = await NextendoApi.SyncHistoryAsync(CollectLocalHistory());

            // [Nextendo] Réinjecte le temps de jeu du COMPTE dans la liste locale de Ryujinx
            // (sinon un nouvel install affiche "Jamais joué" alors que le compte a l'historique).
            ApplyAccountTimeToLibrary(merged);

            _history.Clear();
            foreach (NextendoApi.HistoryItem h in merged)
            {
                byte[] icon = null;
                if (!string.IsNullOrEmpty(h.IconBase64))
                {
                    try { icon = Convert.FromBase64String(h.IconBase64); } catch { /* ignore */ }
                }

                _history.Add(new NextendoHistoryModel
                {
                    Name = h.Name,
                    Icon = icon,
                    PlayedText = FormatPlayed(h.Seconds),
                    LastText = FormatLast(h.LastPlayed),
                });
            }

            NoHistoryText.IsVisible = _history.Count == 0;
        }

        // [Nextendo] Applique le temps de jeu du COMPTE (historique serveur) aux métadonnées
        // locales de Ryujinx : un nouvel install affiche alors le VRAI temps de jeu au lieu de
        // "Jamais joué". Ne rétrograde jamais un temps local supérieur.
        private void ApplyAccountTimeToLibrary(List<NextendoApi.HistoryItem> merged)
        {
            var apps = RyujinxApp.MainWindow?.ViewModel?.Applications;
            if (apps == null)
            {
                return;
            }

            bool changed = false;
            foreach (NextendoApi.HistoryItem h in merged)
            {
                if (string.IsNullOrEmpty(h.TitleId) || h.Seconds <= 0)
                {
                    continue;
                }

                ApplicationData app = null;
                foreach (ApplicationData a in apps)
                {
                    if (string.Equals(a.IdString, h.TitleId, StringComparison.OrdinalIgnoreCase))
                    {
                        app = a;
                        break;
                    }
                }
                if (app == null)
                {
                    continue;
                }

                TimeSpan accountTime = TimeSpan.FromSeconds(h.Seconds);
                if (accountTime <= app.TimePlayed)
                {
                    continue; // le temps local est déjà >= au compte
                }

                DateTime? last = null;
                if (!string.IsNullOrEmpty(h.LastPlayed) &&
                    DateTime.TryParse(h.LastPlayed, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime dt))
                {
                    last = dt.ToUniversalTime();
                }

                app.TimePlayed = accountTime;
                if (last.HasValue)
                {
                    app.LastPlayed = last;
                }

                ApplicationLibrary.LoadAndSaveMetaData(app.IdString, m =>
                {
                    m.TimePlayed = accountTime;
                    if (last.HasValue)
                    {
                        m.LastPlayed = last;
                    }
                });
                changed = true;
            }

            if (changed)
            {
                RyujinxApp.MainWindow?.ViewModel?.RefreshView();
            }
        }

        // Collects every game ever played in this Ryujinx (all titles, not just
        // Nextendo-compatible ones) to push to the account.
        private static List<NextendoApi.HistoryItem> CollectLocalHistory()
        {
            List<NextendoApi.HistoryItem> list = [];
            var apps = RyujinxApp.MainWindow?.ViewModel?.Applications;
            if (apps == null)
            {
                return list;
            }

            foreach (ApplicationData app in apps)
            {
                if (app.LastPlayed == null && app.TimePlayed.TotalSeconds < 1)
                {
                    continue; // never played
                }

                list.Add(new NextendoApi.HistoryItem
                {
                    TitleId = app.IdString,
                    Name = app.Name,
                    IconBase64 = app.Icon is { Length: > 0 } ? Convert.ToBase64String(app.Icon) : "",
                    Seconds = (long)app.TimePlayed.TotalSeconds,
                    LastPlayed = app.LastPlayed?.ToUniversalTime().ToString("o") ?? "",
                });
            }

            return list;
        }

        private static string FormatPlayed(long seconds)
        {
            if (seconds < 60)
            {
                return LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_PlayedBriefly];
            }
            if (seconds < 3600)
            {
                return LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_PlayedMinutesFormat, seconds / 60);
            }
            long hours = seconds / 3600;
            return hours <= 1 ? LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_PlayedOneHourOrMore] : LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_PlayedHoursFormat, hours);
        }

        private static string FormatLast(string iso)
        {
            if (string.IsNullOrEmpty(iso) ||
                !DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime dt))
            {
                return "";
            }

            int days = (int)(DateTime.UtcNow.Date - dt.ToUniversalTime().Date).TotalDays;
            if (days <= 0)
            {
                return LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_PlayedToday];
            }
            if (days == 1)
            {
                return LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_PlayedYesterday];
            }
            if (days < 30)
            {
                return LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_PlayedDaysAgoFormat, days);
            }
            int months = days / 30;
            return months == 1 ? LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_PlayedOneMonthAgo] : LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_PlayedMonthsAgoFormat, months);
        }

        // [Nextendo beta] Create a no-account GUEST online profile from a nickname. Mirrors the
        // first-run wizard so existing installs (which skip the wizard) can still go online
        // without a full account.
        // Live "available / already taken" feedback when the nickname field loses focus.
        private async Task CheckNicknameAvailability()
        {
            string nick = GuestNicknameBox.Text?.Trim() ?? "";
            if (nick.Length < 3 || nick.Length > 16)
            {
                if (nick.Length > 0)
                {
                    ShowGuestStatus(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_GuestProfileNicknameInvalid], ok: false);
                }
                else
                {
                    ShowGuestStatus("", ok: true);
                }

                return;
            }

            ShowGuestStatus(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_GuestProfileChecking], ok: true);
            bool? avail = await NextendoApi.CheckNicknameAvailableAsync(nick);
            if (avail == true)
            {
                ShowGuestStatus(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_GuestProfileNicknameAvailable], ok: true);
            }
            else if (avail == false)
            {
                ShowGuestStatus(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_GuestProfileNicknameTaken], ok: false);
            }
            else
            {
                ShowGuestStatus("", ok: true); // couldn't reach the server — let the create attempt decide
            }
        }

        private async Task CreateGuest()
        {
            string nick = GuestNicknameBox.Text?.Trim() ?? "";
            if (nick.Length < 3 || nick.Length > 16)
            {
                ShowGuestStatus(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_GuestProfileNicknameInvalid], ok: false);
                return;
            }

            CreateGuestButton.IsEnabled = false;
            ShowGuestStatus(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_GuestProfileChecking], ok: true);

            // Explicit availability check first: a taken name is reported clearly and NOT created.
            bool? avail = await NextendoApi.CheckNicknameAvailableAsync(nick);
            if (avail == false)
            {
                ShowGuestStatus(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_GuestProfileNicknameTaken], ok: false);
                CreateGuestButton.IsEnabled = true;
                return;
            }

            (bool ok, string err) = await MainMenuBarView.CreateGuestProfileAsync(nick);
            CreateGuestButton.IsEnabled = true;

            if (ok)
            {
                Refresh(); // re-render as a linked profile
            }
            else
            {
                ShowGuestStatus(string.IsNullOrEmpty(err)
                    ? LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_GuestProfileCreateFailed]
                    : err, ok: false);
            }
        }

        private void ShowGuestStatus(string text, bool ok)
        {
            GuestStatusText.Text = text;
            GuestStatusText.IsVisible = !string.IsNullOrEmpty(text);
            GuestStatusText.Foreground = new SolidColorBrush(ok ? Color.Parse("#3EE8C8") : Color.Parse("#FF6B6B"));
        }

        private async Task ChangePseudo()
        {
            string name = PseudoBox.Text?.Trim() ?? "";
            if (name.Length == 0)
            {
                return;
            }

            (bool ok, string msg) = await NextendoApi.SetUsernameAsync(name);
            if (ok)
            {
                NextendoAccount.Save(NextendoAccount.Pid, name, NextendoAccount.FriendCode, NextendoAccount.NexToken, isGuest: NextendoAccount.IsGuest);
                UsernameText.Text = name;

                try
                {
                    AccAccountManager am = RyujinxApp.MainWindow?.AccountManager;
                    AccUserProfile prof = FindBoundProfile(am);
                    if (am != null && prof != null)
                    {
                        am.SetUserName(prof.UserId, name);
                    }
                }
                catch { /* ignore */ }

                ShowStatus(LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_NicknameChangedFormat, name), true);
            }
            else
            {
                ShowStatus("✗ " + msg, false);
            }
        }

        private async Task AddFriend()
        {
            string code = FriendCodeBox.Text?.Trim() ?? "";
            if (code.Length == 0)
            {
                return;
            }

            (bool ok, string msg) = await NextendoApi.AddFriendAsync(code);
            if (ok)
            {
                FriendCodeBox.Text = "";
                ShowStatus(msg, true);
                await LoadSocial();
            }
            else
            {
                ShowStatus("✗ " + msg, false);
            }
        }

        private async void AcceptRequest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: ulong pid })
            {
                await NextendoApi.AcceptFriendAsync(pid);
                await LoadSocial();
            }
        }

        private async void DeclineRequest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: ulong pid })
            {
                await NextendoApi.DeclineFriendAsync(pid);
                await LoadSocial();
            }
        }

        private async void RemoveFriend_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: ulong pid })
            {
                await NextendoApi.RemoveFriendAsync(pid);
                await LoadSocial();
            }
        }

        private async Task ChangePp()
        {
            if (RyujinxApp.MainWindow?.ViewModel == null)
            {
                return;
            }

            IReadOnlyDictionary<string, byte[]> avatars;
            try
            {
                avatars = UserFirmwareAvatarSelectorViewModel.GetAvatars(RyujinxApp.MainWindow.ContentManager, RyujinxApp.MainWindow.VirtualFileSystem);
            }
            catch
            {
                avatars = null;
            }

            if (avatars == null || avatars.Count == 0)
            {
                ShowStatus(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_AvatarsUnavailableMessage], false);
                return;
            }

            SKColor background = SKColors.White;
            byte[] chosenPng = null;
            ContentDialog dialog = null;

            // Full background-colour picker (the exact control from Ryujinx's profile image
            // editor): a colour button that opens the full picker (ANY colour), instead of a
            // handful of fixed swatches. The inline preview grid is hidden so only the button
            // shows, opening the picker in a flyout.
            ColorPicker colorPicker = new()
            {
                IsAlphaEnabled = false,
                Color = Colors.White,
            };
            Style hideInline = new(s => s.OfType<Grid>().Name("Root").Child().OfType<DockPanel>().Child().OfType<Grid>());
            hideInline.Setters.Add(new Setter(Avalonia.Visual.IsVisibleProperty, false));
            colorPicker.Styles.Add(hideInline);

            WrapPanel grid = new();
            foreach (KeyValuePair<string, byte[]> kv in avatars)
            {
                byte[] png = kv.Value;
                Button btn = new()
                {
                    Margin = new Thickness(3),
                    Padding = new Thickness(2),
                    Content = new Image { Width = 64, Height = 64, Source = new Bitmap(new MemoryStream(png)) },
                };
                btn.Click += (_, _) =>
                {
                    chosenPng = png;
                    Color c = colorPicker.Color;
                    background = new SKColor(c.R, c.G, c.B);
                    dialog?.Hide();
                };
                grid.Children.Add(btn);
            }

            StackPanel content = new()
            {
                MinWidth = 440,
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_BackgroundColorLabel] },
                    colorPicker,
                    new ScrollViewer { Content = grid, MaxHeight = 340 },
                },
            };

            dialog = new ContentDialog
            {
                Title = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ChoosePictureDialogTitle],
                Content = content,
                CloseButtonText = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_CancelButton],
            };

            await ContentDialogHelper.ShowAsync(dialog);

            if (chosenPng == null)
            {
                return;
            }

            byte[] jpeg = PngToJpeg(chosenPng, background);

            bool ok = await NextendoApi.SetProfileImageAsync(jpeg);
            if (ok)
            {
                PpImage.Source = new Bitmap(new MemoryStream(jpeg));

                try
                {
                    AccAccountManager am = RyujinxApp.MainWindow?.AccountManager;
                    AccUserProfile prof = FindBoundProfile(am);
                    if (am != null && prof != null)
                    {
                        am.SetUserImage(prof.UserId, jpeg);
                    }
                }
                catch { /* ignore */ }

                ShowStatus(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_PictureUpdatedMessage], true);
            }
            else
            {
                ShowStatus(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_PictureUpdateFailedMessage], false);
            }
        }

        private static byte[] PngToJpeg(byte[] png, SKColor background)
        {
            using SKBitmap bmp = SKBitmap.Decode(png);
            using SKBitmap flat = new(bmp.Width, bmp.Height);
            using (SKCanvas canvas = new(flat))
            {
                canvas.Clear(background);
                canvas.DrawBitmap(bmp, 0, 0);
            }

            using SKImage image = SKImage.FromBitmap(flat);
            using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 90);

            return data.ToArray();
        }

        private void ShowStatus(string text, bool ok)
        {
            StatusText.Text = text;
            StatusText.Foreground = ok ? Brushes.LightGreen : Brushes.IndianRed;
            StatusText.IsVisible = true;
        }

        private static void TryOpenSite(string path)
        {
            // Ouvre le VRAI site (nextendo.network), pas l'hôte de l'API.
            try { OpenHelper.OpenUrl(NextendoApi.SiteUrl() + path); } catch { /* ignore */ }
        }

        private static AccUserProfile FindBoundProfile(AccAccountManager am)
        {
            string stored = NextendoAccount.ProfileUserId;
            if (am == null || string.IsNullOrEmpty(stored))
            {
                return null;
            }

            foreach (AccUserProfile p in am.GetAllUsers())
            {
                if (p.UserId.ToString() == stored)
                {
                    return p;
                }
            }

            return null;
        }
    }
}
