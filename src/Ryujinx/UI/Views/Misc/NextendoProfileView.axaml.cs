using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Ryujinx.Ava.Common;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.Models;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.Views.Misc
{
    /// <summary>
    /// [Nextendo] App-style profile panel opened from the Switch launcher's circular profile
    /// button. Identity header (avatar / name / friend code / presence + session controls on
    /// top) and three social tabs mirroring the companion app: Friends, Activity and Play
    /// history. Reuses the same API + models as the settings and friends-window views.
    /// </summary>
    public partial class NextendoProfileView : UserControl
    {
        private readonly ObservableCollection<NextendoFriendModel> _friends = [];
        private readonly ObservableCollection<NextendoFriendModel> _requests = [];
        private readonly ObservableCollection<NextendoFriendModel> _playingNow = [];
        private readonly ObservableCollection<NextendoLobbyPlayerModel> _recent = [];
        private readonly ObservableCollection<NextendoHistoryModel> _history = [];
        private readonly Dictionary<ulong, string> _recentCodes = [];

        // Estado de la modale de reporte (0 / vacío cuando no hay reporte abierto).
        private ulong _reportTarget;
        private string _reportReason = "";

        /// <summary>Motivos de reporte y la pista que acompaña al cuadro de texto.</summary>
        private static readonly (string Id, string Title, string Desc, string Hint)[] _motifs =
        [
            ("cheating",         LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ReportReasonCheating],                      LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileCheatsEmuladorDeTecladoMacros],              LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileDescribeLaTrampaCuandoLa]),
            ("name",             LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ReportReasonName],           LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileElNombreIncluyeInsultosContenido],    LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileQueNombreConcretoMuestra]),
            ("name_mismatch",    LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileNombreIncoherente],           LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileElNombreNoCoincideCon],     LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileQueNombreMuestraQueDeberias]),
            ("avatar",           LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileImagenDePerfilInapropiada], LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileAvatarOfensivoFueraDeLa],           LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileDescribeLaImagenPorQue]),
            ("harassment",       LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileAcosoInsultos],             LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileMensajesOfensivosAmenazasAcoso],             LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileQueTeEscribioDonde]),
            ("griefing",         LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileSabotaje],                     LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileArruinaElJuegoPropositoMolesta],   LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileCuentanosQueHizo]),
            ("impersonation",    LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileSuplantacion],                 LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileSeHacePasarPorOtra],    LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileQuienSuplanta]),
            ("other",            LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileOtro],                         LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileEligeEsteMotivoDetallaloEn],       LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileQuePaso]),
        ];

        private readonly DispatcherTimer _refreshTimer;

        public NextendoProfileView()
        {
            InitializeComponent();

            FriendsList.ItemsSource = _friends;
            RequestsList.ItemsSource = _requests;
            PlayingNowList.ItemsSource = _playingNow;
            RecentList.ItemsSource = _recent;
            HistoryList.ItemsSource = _history;

            CopyCodeButton.Click += CopyCode_Click;
            SignOutButton.Click += SignOut_Click;
            ConnectButton.Click += async (_, _) => await ConnectAccount();
            AddFriendButton.Click += async (_, _) => await AddFriend();
            AcceptAllButton.Click += async (_, _) => await AcceptAll();

            // Presence goes stale fast; 20s matches the account server's own freshness window.
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _refreshTimer.Tick += async (_, _) =>
            {
                RefreshOwnStatus();
                _ = LoadFriends();
                _ = LoadActivity();
            };
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            RefreshOwnStatus();

            _ = LoadProfileAsync();
            _ = LoadFriends();
            _ = LoadActivity();
            _ = LoadHistory();

            _refreshTimer.Start();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _refreshTimer.Stop();
            base.OnDetachedFromVisualTree(e);
        }

        private async Task LoadProfileAsync()
        {
            try
            {
                (string name, byte[] image) = await NextendoApi.GetProfileSyncAsync();

                if (!string.IsNullOrEmpty(name))
                {
                    ProfileName.Text = name;
                }

                if (image is { Length: > 0 })
                {
                    AvatarImage.Source = new Bitmap(new MemoryStream(image));
                }
            }
            catch
            {
                // Cosmetic only.
            }
        }

        private void RefreshOwnStatus()
        {
            bool linked = NextendoAccount.IsLinked;

            ProfileName.Text = string.IsNullOrEmpty(NextendoAccount.Username)
                ? (linked ? LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxCarouselPerfil] : LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileNoConectado])
                : NextendoAccount.Username;
            ProfileFriendCode.Text = string.IsNullOrEmpty(NextendoAccount.FriendCode)
                ? "SW-…"
                : NextendoAccount.FriendCode;

            ProfileStatusDot.Fill = Brush.Parse(linked ? "#33E86B" : "#55808080");
            ProfileStatusText.Text = linked ? LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileEnLinea] : LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileSinCuentaNextendo];

            SignOutButton.IsVisible = linked;
            ConnectButton.IsVisible = !linked;
        }

        // Tabs: only the selected panel is visible; pill buttons reflect the active tab.
        private void SelectFriendsTab(object sender, RoutedEventArgs e)
        {
            FriendsTab.IsVisible = true;
            ActivityTab.IsVisible = false;
            HistoryTab.IsVisible = false;
        }

        private void SelectActivityTab(object sender, RoutedEventArgs e)
        {
            FriendsTab.IsVisible = false;
            ActivityTab.IsVisible = true;
            HistoryTab.IsVisible = false;

            _ = LoadActivity();
        }

        private void SelectHistoryTab(object sender, RoutedEventArgs e)
        {
            FriendsTab.IsVisible = false;
            ActivityTab.IsVisible = false;
            HistoryTab.IsVisible = true;

            _ = LoadHistory();
        }

        private async Task ConnectAccount()
        {
            // Full OAuth flow opens the browser; reuse the existing sign-in from the API.
            (bool ok, string error) = await NextendoApi.SignInWithBrowserAsync();
            if (ok)
            {
                ShowStatus(FriendsStatusText, LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileCuentaConectada], true);
                RefreshOwnStatus();
                _ = LoadProfileAsync();
                _ = LoadFriends();
                _ = LoadActivity();
                _ = LoadHistory();
            }
            else if (!string.IsNullOrEmpty(error))
            {
                ShowStatus(FriendsStatusText, error, false);
            }
        }

        private async void SignOut_Click(object sender, RoutedEventArgs e)
        {
            bool confirm = await ContentDialogHelper.CreateConfirmationDialog(
                LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileSeCerraraTuSesionDe],
                LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileContinuar],
                LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_DialogSignOutButton],
                LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_CancelButton],
                LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_DialogSignOutButton]) == UserResult.Yes;

            if (!confirm)
            {
                return;
            }

            NextendoAccount.Clear();

            _friends.Clear();
            _requests.Clear();
            _history.Clear();
            _recent.Clear();
            _playingNow.Clear();

            RefreshOwnStatus();
        }

        private async Task LoadFriends()
        {
            (List<NextendoApi.Friend> friends, List<NextendoApi.Friend> requests) = await NextendoApi.GetSocialAsync();

            Fill(_friends, friends.OrderByDescending(f => f.Favorite).ThenByDescending(f => f.IsOnline).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
            Fill(_requests, requests);

            NoFriendsText.IsVisible = _friends.Count == 0;
            RequestsPanel.IsVisible = _requests.Count > 0;

            OnlineCountText.Text = _friends.Count > 0
                ? $"{_friends.Count(f => f.IsOnline)} / {_friends.Count} en línea"
                : "";
        }

        private async Task LoadActivity()
        {
            // Who is playing right now: online friends with a game, most recently active first.
            (List<NextendoApi.Friend> friends, _) = await NextendoApi.GetSocialAsync();

            var inGame = friends
                .Where(f => f.IsOnline && !string.IsNullOrEmpty(f.AppId))
                .OrderByDescending(f => f.OnlineStatus)
                .ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            _playingNow.Clear();
            foreach (var f in inGame)
            {
                byte[] img = null;
                if (!string.IsNullOrEmpty(f.ImageBase64))
                {
                    try { img = Convert.FromBase64String(f.ImageBase64); } catch { /* ignore */ }
                }

                _playingNow.Add(new NextendoFriendModel
                {
                    Pid = f.Pid,
                    Name = f.Name,
                    Image = img,
                    OnlineStatus = f.OnlineStatus,
                    AppId = f.AppId,
                    AppDetail = f.AppDetail,
                });
            }

            PlayingNowText.Text = inGame.Count == 0
                ? LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileNingunoDeTusAmigosEsta]
                : "";

            // Recent encounters: people met online, with avatar fetched separately.
            List<NextendoApi.NextendoPlayer> recent = await NextendoApi.GetRecentPlayersAsync();

            _recentCodes.Clear();
            _recent.Clear();
            foreach (NextendoApi.NextendoPlayer p in recent)
            {
                byte[] avatar = await NextendoApi.GetAvatarAsync(p.Pid, p.AvatarUrl);

                if (!string.IsNullOrEmpty(p.FriendCode))
                {
                    _recentCodes[p.Pid] = p.FriendCode;
                }

                _recent.Add(new NextendoLobbyPlayerModel
                {
                    Pid = p.Pid,
                    Name = p.Name,
                    Image = avatar,
                    Known = p.Known,
                    IsFriend = p.Known && friends.Any(f => f.Pid == p.Pid),
                    GameName = ResolveGame(p.TitleId),
                    SeenAt = p.SeenAt,
                });
            }

            RecentText.IsVisible = _recent.Count == 0;
        }

        private async Task LoadHistory()
        {
            List<NextendoApi.HistoryItem> merged = await NextendoApi.SyncHistoryAsync(NextendoHistorySync.CollectLocalHistory());

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

        private static string ResolveGame(string titleId)
        {
            if (string.IsNullOrEmpty(titleId))
            {
                return "";
            }

            return NextendoGameNames.Resolve(titleId) ?? "";
        }

        private async Task AddFriend()
        {
            string code = AddFriendBox.Text?.Trim();
            if (string.IsNullOrEmpty(code))
            {
                return;
            }

            (bool ok, string message) = await NextendoApi.AddFriendAsync(code);
            ShowStatus(FriendsStatusText, message, ok);

            if (ok)
            {
                AddFriendBox.Text = "";
                await LoadFriends();
            }
        }

        private async Task AcceptAll()
        {
            AcceptAllButton.IsEnabled = false;
            try
            {
                int n = await NextendoApi.AcceptAllRequestsAsync();
                await LoadFriends();
                ShowStatus(FriendsStatusText, n > 0 ? LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.Dialog_Nextendo_NxProfileAceptadasSolicitudes, n) : LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileNoHaySolicitudesQueAceptar], n > 0);
            }
            finally
            {
                AcceptAllButton.IsEnabled = true;
            }
        }

        private async void AcceptRequest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: ulong pid })
            {
                await NextendoApi.AcceptFriendAsync(pid);
                await LoadFriends();
            }
        }

        private async void DeclineRequest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: ulong pid })
            {
                await NextendoApi.DeclineFriendAsync(pid);
                await LoadFriends();
            }
        }

        private async void RemoveFriend_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: ulong pid })
            {
                await NextendoApi.RemoveFriendAsync(pid);
                await LoadFriends();
            }
        }

        private async void Favorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: ulong pid })
            {
                bool newState = _friends.FirstOrDefault(f => f.Pid == pid) is not { Favorite: true };
                await NextendoApi.SetFavoriteAsync(pid, newState);
                await LoadFriends();
            }
        }

        private async void CopyCode_Click(object sender, RoutedEventArgs e)
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard is not null && !string.IsNullOrEmpty(NextendoAccount.FriendCode))
            {
                await top.Clipboard.SetTextAsync(NextendoAccount.FriendCode);
            }
        }

        // ============================================================ [Nextendo]
        // Recientes → añadir amigo + reportar (réplica de LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileNextendoRecentlyMet])
        // ============================================================

        private async void AddRecentFriend_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: ulong pid })
            {
                return;
            }

            if (!_recentCodes.TryGetValue(pid, out string code) || string.IsNullOrEmpty(code))
            {
                ShowStatus(RecentStatusText, LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileNoHayUnCodigoDe], false);

                return;
            }

            (bool ok, string message) = await NextendoApi.AddFriendAsync(code);
            ShowStatus(RecentStatusText, message, ok);

            if (ok)
            {
                _ = LoadFriends();
            }
        }

        private void Report_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: ulong pid })
            {
                return;
            }

            NextendoLobbyPlayerModel jugador = _recent.FirstOrDefault(p => p.Pid == pid);

            _reportTarget = pid;
            _reportReason = "";

            ReportTargetText.Text = jugador?.Name ?? $"#{pid}";
            ReportTargetSubText.Text = jugador?.SeenLine ?? "";
            ReportInitialText.Text = jugador?.Initial ?? "?";
            PoseAvatar(jugador?.Image);

            ReportCommentBox.Text = "";
            MontreEtape1();

            ShowStatus(RecentStatusText, "", true);
            ReportOverlay.IsVisible = true;
        }

        /// <summary>Carga el avatar del reportado en la modale, o cae en la inicial.</summary>
        private void PoseAvatar(byte[] octets)
        {
            if (octets is not { Length: > 0 })
            {
                ReportAvatarImage.Source = null;
                ReportAvatarImage.IsVisible = false;
                ReportInitialText.IsVisible = true;

                return;
            }

            try
            {
                using MemoryStream flujo = new(octets);
                ReportAvatarImage.Source = new Bitmap(flujo);
                ReportAvatarImage.IsVisible = true;
                ReportInitialText.IsVisible = false;
            }
            catch (Exception ex)
            {
                // Una foto ilegible no debe impedir reportar — a veces es el motivo.
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] avatar decode failed: {ex.Message}");
                ReportAvatarImage.IsVisible = false;
                ReportInitialText.IsVisible = true;
            }
        }

        private void MontreEtape1()
        {
            ReportModalSubtitleText.IsVisible = true;
            ReportReasonScroll.IsVisible = true;
            ReportChosenBox.IsVisible = false;
            ReportCommentArea.IsVisible = false;
            ReportBackButton.IsVisible = false;
            ReportSendButton.IsVisible = false;
            ShowStatus(ModalReportStatus, "", true);
        }

        private void ReportReason_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string motivo } || !_motifs.Any(m => m.Id == motivo))
            {
                return;
            }

            var infos = _motifs.First(m => m.Id == motivo);
            _reportReason = motivo;

            ReportChosenText.Text = infos.Title;
            ReportChosenDescText.Text = infos.Desc;
            ReportCommentBox.Watermark = infos.Hint;

            ReportModalSubtitleText.IsVisible = false;
            ReportReasonScroll.IsVisible = false;
            ReportChosenBox.IsVisible = true;
            ReportCommentArea.IsVisible = true;
            ReportBackButton.IsVisible = true;
            ReportSendButton.IsVisible = true;
            ReportCommentBox.Focus();
        }

        private void ReportBack_Click(object sender, RoutedEventArgs e)
        {
            _reportReason = "";
            MontreEtape1();
        }

        private void ReportCancel_Click(object sender, RoutedEventArgs e) => CerrarModale();

        /// <summary>
        /// Clic en el velo: se cierra. El chequeo de la fuente es imprescindible —
        /// sin él, un clic dentro de la tarjeta subiría hasta aquí y cerraría la
        /// modale a mitad de la redacción.
        /// </summary>
        private void ReportOverlay_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (ReferenceEquals(e.Source, ReportOverlay))
            {
                CerrarModale();
            }
        }

        private void CerrarModale()
        {
            _reportTarget = 0;
            _reportReason = "";
            ReportOverlay.IsVisible = false;
        }

        private async void ReportSend_Click(object sender, RoutedEventArgs e)
        {
            if (_reportTarget == 0 || string.IsNullOrEmpty(_reportReason))
            {
                return;
            }

            ulong cible = _reportTarget;

            ReportSendButton.IsEnabled = false;
            (bool ok, string error) = await NextendoApi.ReportPlayerAsync(cible, _reportReason, ReportCommentBox.Text ?? "");
            ReportSendButton.IsEnabled = true;

            if (ok)
            {
                CerrarModale();
                ShowStatus(RecentStatusText, LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileReporteEnviadoGracias], true);

                return;
            }

            // El servidor distingue sus rechazos: el jugador merece saber cuál.
            string mensaje = error switch
            {
                "not_encountered" => LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileNoSePudoConfirmarQue],
                "quota" => LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileYaHasEnviadoDemasiadosReportes],
                _ => LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.Dialog_Nextendo_NxProfileElReporteNoSePudo, error),
            };

            Logger.Info?.Print(LogClass.Application, $"[Nextendo] report refused: {error}");
            ShowStatus(ModalReportStatus, mensaje, false);
        }

        private static void ShowStatus(TextBlock target, string text, bool ok)
        {
            target.Text = text;
            target.Foreground = Brush.Parse(ok ? "#3EE8C8" : "#E8333E");
            target.IsVisible = !string.IsNullOrEmpty(text);
        }

        private static string FormatPlayed(long seconds)
        {
            if (seconds < 60)
            {
                return LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileUnInstante];
            }
            if (seconds < 3600)
            {
                return $"{seconds / 60} min";
            }
            long hours = seconds / 3600;
            return hours <= 1 ? LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileHoraMas] : LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.Dialog_Nextendo_NxProfileHeureCourte, hours);
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
                return LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileHoy];
            }
            if (days == 1)
            {
                return LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileAyer];
            }
            if (days < 30)
            {
                return LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.Dialog_Nextendo_NxProfileHaceDias, days);
            }
            int months = days / 30;
            return months == 1 ? LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NxProfileHaceMes] : LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.Dialog_Nextendo_NxProfileHaceMeses, months);
        }
    }
}
