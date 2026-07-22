using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Ryujinx.Ava.Common;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Models;
using Gommon;
using Ryujinx.Common;
using Ryujinx.Common.Configuration;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AccAccountManager = Ryujinx.HLE.HOS.Services.Account.Acc.AccountManager;
using AccUserProfile = Ryujinx.HLE.HOS.Services.Account.Acc.UserProfile;

namespace Ryujinx.Ava.UI.Windows
{
    /// <summary>
    /// [Nextendo] The friends list as its own window, reachable from the menu bar or Ctrl+F.
    ///
    /// It used to live inside Settings, which meant that checking who was online forced the player
    /// through a modal settings dialog mid-session. Everything social now lives here: presence,
    /// requests, and the connection check that answers "is it me or is it them?".
    /// </summary>
    public partial class NextendoFriendsWindow : StyleableAppWindow
    {
        private readonly ObservableCollection<NextendoFriendModel> _friends = [];
        private readonly ObservableCollection<NextendoFriendModel> _requests = [];

        private readonly DispatcherTimer _refreshTimer;
        private CancellationTokenSource _checkCancel;
        private bool _checking;

        /// <summary>Only one friends window at a time; a second Ctrl+F just raises the first.</summary>
        private static NextendoFriendsWindow _current;

        public static void Open()
        {
            if (_current is not null)
            {
                _current.Activate();

                return;
            }

            _current = new NextendoFriendsWindow();
            _current.Closed += (_, _) => _current = null;
            _current.Show(RyujinxApp.MainWindow);
        }

        public NextendoFriendsWindow() : base(useCustomTitleBar: true, 37)
        {
            Title = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_FriendsWindowTitle];

            InitializeComponent();

            FriendsList.ItemsSource = _friends;
            RequestsList.ItemsSource = _requests;

            CopyCodeButton.Click += CopyCode_Click;
            AddFriendButton.Click += async (_, _) => await AddFriend();
            CheckButton.Click += async (_, _) => await RunCheck();

            // Presence goes stale fast — a friend list that lies about who is online is worse than
            // no list at all. 20s matches what the account server itself considers fresh.
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _refreshTimer.Tick += async (_, _) =>
            {
                // Le detail de jeu change sans changement d'application (nouveau play report),
                // donc l'abonnement a CurrentApplication seul laisserait ma ligne perimee.
                RefreshOwnStatus();

                await LoadSocial();
            };
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            MyName.Text = NextendoAccount.Username;
            MyFriendCode.Text = NextendoAccount.FriendCode;

            _ = LoadAvatarAsync();
            RefreshOwnStatus();

            // Launching or closing a game changes what friends see of you; reflect it live rather
            // than showing a status that is only true until you start playing.
            TitleIDs.CurrentApplication.Event += OnCurrentApplicationChanged;

            _refreshTimer.Start();

            _ = LoadSocial();
            _ = RunCheck();
        }

        protected override void OnClosed(EventArgs e)
        {
            TitleIDs.CurrentApplication.Event -= OnCurrentApplicationChanged;

            _refreshTimer.Stop();
            _checkCancel?.Cancel();
            _checkCancel?.Dispose();

            base.OnClosed(e);
        }

        private void OnCurrentApplicationChanged(object sender, ReactiveEventArgs<Optional<string>> e)
            => Dispatcher.UIThread.Post(RefreshOwnStatus);

        /// <summary>
        /// Mirrors back the presence your friends actually see of you.
        ///
        /// Must agree with what NextendoFriends.PublishGame posts: with the emulator open and an
        /// account linked you are Online (1) even with no game running, and OnlinePlay (2) once a
        /// game starts. Showing yourself "Offline" here while friends see you Online would make
        /// the window contradict itself.
        /// </summary>
        private void RefreshOwnStatus()
        {
            bool linked = NextendoAccount.IsLinked;

            Optional<string> current = TitleIDs.CurrentApplication.Value;
            string game = current.HasValue ? NextendoGameNames.Resolve(current.Value) : null;

            MyStatusDot.Fill = Brush.Parse(linked ? "#33E86B" : "#55808080");

            if (!linked)
            {
                MyStatusText.Text = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_FriendOffline];

                return;
            }

            if (game is null)
            {
                MyStatusText.Text = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_FriendOnline];

                return;
            }

            string detail = NextendoFriends.CurrentAppDetail ?? "";

            MyStatusText.Text = string.IsNullOrEmpty(detail)
                ? LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.Dialog_Nextendo_FriendPlayingFormat, game)
                : LocaleManager.Instance.UpdateAndGetDynamicValue(
                    LocaleKeys.Dialog_Nextendo_FriendPlayingDetailFormat, game, detail);
        }

        /// <summary>
        /// Loads the avatar from the ACCOUNT, which is the same picture the website shows and the
        /// same one friends see of you.
        ///
        /// Reading the local Switch profile instead looks equivalent and is not: the two are only
        /// linked by a uuid, and any install whose Profiles.json lacks that uuid — a fresh portable
        /// folder, a copied install — shows somebody else's picture, silently and confidently.
        /// The account is the single source of truth here.
        /// </summary>
        private async Task LoadAvatarAsync()
        {
            try
            {
                (string _, byte[] image) = await NextendoApi.GetProfileSyncAsync();

                if (image is { Length: > 0 })
                {
                    MyAvatar.Source = new Bitmap(new MemoryStream(image));

                    return;
                }

                // No picture on the account: fall back to the bound local profile rather than an
                // empty square. Bound, not "last opened" — that is what makes it ours.
                AccUserProfile profile = FindBoundProfile();
                if (profile?.Image is { Length: > 0 })
                {
                    MyAvatar.Source = new Bitmap(new MemoryStream(profile.Image));
                }
            }
            catch
            {
                // No avatar is a cosmetic loss; never let it stop the window from opening.
            }
        }

        /// <summary>The local profile this Nextendo account is linked to, or null when none matches.</summary>
        private static AccUserProfile FindBoundProfile()
        {
            AccAccountManager am = RyujinxApp.MainWindow?.AccountManager;
            string bound = NextendoAccount.ProfileUserId;

            if (am is null || string.IsNullOrEmpty(bound))
            {
                return null;
            }

            return am.GetAllUsers().FirstOrDefault(p => p.UserId.ToString() == bound);
        }

        private async Task LoadSocial()
        {
            (List<NextendoApi.Friend> friends, List<NextendoApi.Friend> requests) = await NextendoApi.GetSocialAsync();

            // Online first, then by name: the people you can actually play with right now belong at
            // the top, and stable alphabetical ordering keeps the list from jumping around on each
            // refresh.
            // Favorites first, then online, then alphabetical: the people you care about most and
            // can play with right now belong at the top.
            Fill(_friends, friends.OrderByDescending(f => f.Favorite).ThenByDescending(f => f.IsOnline).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
            Fill(_requests, requests);

            // Tell the background watcher what is on screen: a request the player is looking at
            // right now must not pop a notification behind the window.
            NextendoFriendRequestWatcher.Sync(_requests.Select(r => r.Pid));

            int online = _friends.Count(f => f.IsOnline);

            NoFriendsText.IsVisible = _friends.Count == 0;
            RequestsPanel.IsVisible = _requests.Count > 0;
            OnlineCountText.Text = online > 0
                ? LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.Dialog_Nextendo_FriendsOnlineCountFormat, online)
                : "";
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
                    Favorite = f.Favorite,
                });
            }
        }

        private async Task RunCheck()
        {
            if (_checking)
            {
                return;
            }

            _checking = true;
            CheckButton.IsEnabled = false;
            CheckButton.Content = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NetworkCheckTesting];

            try
            {
                _checkCancel?.Dispose();
                _checkCancel = new CancellationTokenSource();

                NextendoNetworkCheck.Result result = await NextendoNetworkCheck.CheckAsync(_checkCancel.Token);

                CheckResults.IsVisible = true;

                LatencyValue.Text = result.Reachable
                    ? $"{result.LatencyMs} ms"
                    : LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_LatencyUnreachable];
                LatencyValue.Foreground = Brush.Parse(result.LatencyColor);

                (LocaleKeys label, LocaleKeys hint) = result.Nat switch
                {
                    NextendoNetworkCheck.NatType.Open =>
                        (LocaleKeys.Dialog_Nextendo_NatOpen, LocaleKeys.Dialog_Nextendo_NatOpenTooltip),
                    NextendoNetworkCheck.NatType.Strict =>
                        (LocaleKeys.Dialog_Nextendo_NatStrict, LocaleKeys.Dialog_Nextendo_NatStrictTooltip),
                    _ => (LocaleKeys.Dialog_Nextendo_NatUnknown, LocaleKeys.Dialog_Nextendo_NatUnknownTooltip),
                };

                NatValue.Text = LocaleManager.Instance[label];
                NatValue.Foreground = Brush.Parse(result.NatColor);
                NatDot.Fill = Brush.Parse(result.NatColor);

                NatHint.Text = LocaleManager.Instance[hint];
                NatHint.IsVisible = true;

                ExternalValue.Text = string.IsNullOrEmpty(result.ExternalEndpoint)
                    ? ""
                    : $"{LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ExternalAddressLabel]}: {result.ExternalEndpoint}";
            }
            finally
            {
                _checking = false;
                CheckButton.IsEnabled = true;
                CheckButton.Content = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NetworkCheckButton];
            }
        }

        private async void CopyCode_Click(object sender, RoutedEventArgs e)
        {
            if (Clipboard is not null && !string.IsNullOrEmpty(NextendoAccount.FriendCode))
            {
                await Clipboard.SetTextAsync(NextendoAccount.FriendCode);
            }
        }

        private async Task AddFriend()
        {
            string code = AddFriendBox.Text?.Trim();
            if (string.IsNullOrEmpty(code))
            {
                return;
            }

            (bool ok, string message) = await NextendoApi.AddFriendAsync(code);

            ShowStatus(message, ok);

            if (ok)
            {
                AddFriendBox.Text = "";

                await LoadSocial();
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

        private async void Favorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: ulong pid })
            {
                // Toggle from the current state; the server persists it and both the emulator and
                // the website read it back from /api/friends, so the star stays in sync everywhere.
                bool newState = _friends.FirstOrDefault(f => f.Pid == pid) is not { Favorite: true };
                await NextendoApi.SetFavoriteAsync(pid, newState);
                await LoadSocial();
            }
        }

        private void ShowStatus(string text, bool ok)
        {
            StatusText.Text = text;
            StatusText.Foreground = Brush.Parse(ok ? "#3EE8C8" : "#E8333E");
            StatusText.IsVisible = !string.IsNullOrEmpty(text);
        }
    }
}
