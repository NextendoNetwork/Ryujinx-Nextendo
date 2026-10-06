using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ryujinx.Ava.Common;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.Models;
using Ryujinx.Ava.Systems.AppLibrary;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Applets.MyPage;
using FluentAvalonia.UI.Controls;
using IGamepad = Ryujinx.Input.IGamepad;
using GamepadStateSnapshot = Ryujinx.Input.GamepadStateSnapshot;
using GamepadButtonInputId = Ryujinx.Input.GamepadButtonInputId;
using StickInputId = Ryujinx.Input.StickInputId;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.Views.Misc
{
    /// <summary>
    /// [Nextendo] Central dashboard hosted in the main emulator window. It reuses the existing
    /// account, friends, activity and history APIs while keeping the game running behind it.
    /// </summary>
    public partial class NextendoProfileView : UserControl
    {
        private readonly ObservableCollection<NextendoFriendModel> _friends = [];
        private readonly ObservableCollection<NextendoFriendModel> _requests = [];
        private readonly ObservableCollection<NextendoLobbyPlayerModel> _recent = [];
        private readonly ObservableCollection<NextendoLobbyPlayerModel> _lobby = [];
        private readonly ObservableCollection<NextendoHistoryModel> _history = [];
        private readonly ObservableCollection<NextendoHistoryModel> _selectedFriendHistory = [];
        private readonly ObservableCollection<NextendoGameInviteModel> _invites = [];
        private readonly ObservableCollection<NextendoFriendModel> _gameInviteFriends = [];
        private readonly HashSet<ulong> _selectedGameInviteRecipients = [];
        private readonly Dictionary<ulong, string> _recentCodes = [];
        private List<NextendoApi.HistoryItem> _syncedHistory = [];
        private FriendInvitationRequest _gameInvitationRequest;
        private Action<bool> _gameInvitationCompleted;
        private IGamepad _navigationGamepad;
        private string _navigationGamepadId;
        private bool _navigationUpDown;
        private bool _navigationDownDown;
        private bool _navigationRightDown;
        private bool _navigationLeftDown;
        private bool _navigationConfirmDown;
        private bool _navigationBackDown;
        private bool _closeDashboardOnBackRelease;
        private int _contentFocusIndex;
        private int _selectedNavigationIndex;
        private Control _selectedPanel;
        private Control _reportReturnPanel;
        private Control _problemReturnPanel;
        private bool _problemSending;
        private bool _accountNetworkCheckRunning;
        private bool _navigatingSidebar = true;
        private readonly bool _isGameRunningContext;

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
        private readonly DispatcherTimer _navigationTimer;

        public event EventHandler CloseRequested;

        public NextendoProfileView() : this(false)
        {
        }

        public NextendoProfileView(bool isGameRunning)
        {
            InitializeComponent();

            _isGameRunningContext = isGameRunning;
            EmulationTabButton.IsVisible = isGameRunning;

            FriendsList.ItemsSource = _friends;
            RequestsList.ItemsSource = _requests;
            RecentList.ItemsSource = _recent;
            LobbyList.ItemsSource = _lobby;
            HistoryList.ItemsSource = _history;
            AccountHistoryList.ItemsSource = _history;
            SelectedFriendHistoryList.ItemsSource = _selectedFriendHistory;
            InvitesList.ItemsSource = _invites;
            GameInviteFriendsList.ItemsSource = _gameInviteFriends;
            _selectedPanel = AccountTab;
            SelectedFriendFavoriteButton.Tag = 0UL;
            SelectedFriendRemoveButton.Tag = 0UL;

            CopyCodeButton.Click += CopyCode_Click;
            SignOutButton.Click += SignOut_Click;
            ConnectButton.Click += async (_, _) => await ConnectAccount();
            AddFriendButton.Click += async (_, _) => await AddFriend();
            ProfileName.PointerPressed += (_, _) => OpenAccountPage();
            AvatarImage.PointerPressed += (_, _) => OpenAccountPage();
            NextendoGameInvites.Changed += RefreshInvites;

            // Presence goes stale fast; 20s matches the account server's own freshness window.
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _refreshTimer.Tick += async (_, _) =>
            {
                RefreshOwnStatus();
                _ = LoadFriends();
                _ = LoadActivity();
                _ = LoadLobby();
            };
            _navigationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(75) };
            _navigationTimer.Tick += (_, _) => PollDashboardGamepad();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            RefreshOwnStatus();

            _ = LoadProfileAsync();
            _ = LoadFriends();
            _ = LoadActivity();
            _ = LoadHistory();
            _ = LoadLobby();
            _ = NextendoGameInvites.RefreshAsync();
            RefreshInvites();
            _ = CheckAccountNetworkAsync();

            _refreshTimer.Start();
            _navigationTimer.Start();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _refreshTimer.Stop();
            _navigationTimer.Stop();
            _navigationGamepad?.Dispose();
            _navigationGamepad = null;
            _navigationGamepadId = null;
            NextendoGameInvites.Changed -= RefreshInvites;
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

        private void OpenAccountCard(object sender, PointerPressedEventArgs e) => OpenAccountPage();

        private void OpenAccountPage()
        {
            try { Ryujinx.Common.Helper.OpenHelper.OpenUrl("https://nextendo.network/compte"); }
            catch { /* The system browser is optional. */ }
        }

        private void SelectAccountTab(object sender, RoutedEventArgs e)
        {
            _navigatingSidebar = false;
            SetSelectedPanel(AccountTab);
        }

        private void SelectFriendsTab(object sender, RoutedEventArgs e)
        {
            _navigatingSidebar = false;
            SetSelectedPanel(FriendsTab);
        }

        private void SelectRequestsTab(object sender, RoutedEventArgs e)
        {
            _navigatingSidebar = false;
            SetSelectedPanel(RequestsTab);
        }

        public void ShowInvitesTab()
        {
            SelectRequestsTab(this, null);
            Dispatcher.UIThread.Post(FocusFirstContentControl);
        }

        /// <summary>Shows the game's MyPage invite picker inside the Invites dashboard category.</summary>
        public void BeginGameInvitation(FriendInvitationRequest request, Action<bool> completed)
        {
            _gameInvitationRequest = request;
            _gameInvitationCompleted = completed;
            _selectedGameInviteRecipients.Clear();
            GameInviteSection.IsVisible = true;
            GameInviteStatusText.Text = $"Choose up to {Math.Min(request.RecipientLimit, 15)} friends to invite.";
            SendGameInviteButton.IsEnabled = false;
            _navigatingSidebar = false;
            SetSelectedPanel(RequestsTab);
            Dispatcher.UIThread.Post(FocusFirstContentControl);
            _ = LoadFriends();
        }

        public void CancelPendingGameInvitation()
        {
            CompleteGameInvitation(false);
        }

        private void CompleteGameInvitation(bool sent)
        {
            Action<bool> completed = _gameInvitationCompleted;
            if (_gameInvitationRequest == null)
            {
                return;
            }

            _gameInvitationRequest = null;
            _gameInvitationCompleted = null;
            _selectedGameInviteRecipients.Clear();
            _gameInviteFriends.Clear();
            GameInviteSection.IsVisible = false;
            completed?.Invoke(sent);
        }

        private void GameInviteSelection_Click(object sender, RoutedEventArgs e)
        {
            if (_gameInvitationRequest == null || sender is not CheckBox { Tag: ulong pid } checkBox)
            {
                return;
            }

            if (checkBox.IsChecked == true)
            {
                if (_selectedGameInviteRecipients.Count >= Math.Min(_gameInvitationRequest.RecipientLimit, 15))
                {
                    checkBox.IsChecked = false;
                    GameInviteStatusText.Text = $"You can invite up to {Math.Min(_gameInvitationRequest.RecipientLimit, 15)} friends.";
                    return;
                }

                _selectedGameInviteRecipients.Add(pid);
            }
            else
            {
                _selectedGameInviteRecipients.Remove(pid);
            }

            SendGameInviteButton.IsEnabled = _selectedGameInviteRecipients.Count > 0;
        }

        private async void SendGameInvite_Click(object sender, RoutedEventArgs e)
        {
            if (_gameInvitationRequest == null || _selectedGameInviteRecipients.Count == 0)
            {
                return;
            }

            SendGameInviteButton.IsEnabled = false;
            GameInviteStatusText.Text = "Sending invitation…";
            (bool ok, string message) = await NextendoApi.SendGameInvitationAsync(
                _gameInvitationRequest.TitleId,
                _selectedGameInviteRecipients.ToList(),
                _gameInvitationRequest.UserData,
                _gameInvitationRequest.Description);

            if (ok)
            {
                CompleteGameInvitation(true);
            }
            else
            {
                GameInviteStatusText.Text = message;
                SendGameInviteButton.IsEnabled = _selectedGameInviteRecipients.Count > 0;
            }
        }

        private void CancelGameInvite_Click(object sender, RoutedEventArgs e)
        {
            CompleteGameInvitation(false);
        }

        private void SelectActivityTab(object sender, RoutedEventArgs e)
        {
            _navigatingSidebar = false;
            SetSelectedPanel(ActivityTab);

            _ = LoadActivity();
        }

        private void SelectHistoryTab(object sender, RoutedEventArgs e)
        {
            _navigatingSidebar = false;
            SetSelectedPanel(HistoryTab);

            _ = LoadHistory();
        }

        private void ReportProblem_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPanel != ReportProblemTab)
            {
                _problemReturnPanel = _selectedPanel;
            }

            ProblemErrorCodeBox.Text = "";
            ProblemCommentBox.Text = "";
            ProblemAttachLogCheck.IsChecked = true;
            ShowStatus(ProblemReportStatusText, "", true);
            SetSelectedPanel(ReportProblemTab);
            _navigatingSidebar = false;
            Dispatcher.UIThread.Post(FocusFirstContentControl);
        }

        private void CancelProblemReport_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedPanel(_problemReturnPanel ?? AccountTab);
            _navigatingSidebar = false;
            Dispatcher.UIThread.Post(FocusFirstContentControl);
        }

        private async void SendProblemReport_Click(object sender, RoutedEventArgs e)
        {
            if (_problemSending)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(ProblemErrorCodeBox.Text) && string.IsNullOrWhiteSpace(ProblemCommentBox.Text))
            {
                ShowStatus(ProblemReportStatusText, LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ReportEmpty], false);
                return;
            }

            if (!NextendoAccount.IsLinked)
            {
                ShowStatus(ProblemReportStatusText, LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ReportNeedsAccount], false);
                return;
            }

            _problemSending = true;
            ProblemReportSendButton.IsEnabled = false;
            ShowStatus(ProblemReportStatusText, LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ReportSending], true);

            (bool ok, string message) = ProblemAttachLogCheck.IsChecked == true
                ? await NextendoApi.SendReportAsync(ProblemErrorCodeBox.Text?.Trim(), ProblemCommentBox.Text?.Trim())
                : await NextendoApi.SendReportAsync(ProblemErrorCodeBox.Text?.Trim(), ProblemCommentBox.Text?.Trim(), attachLog: false);

            _problemSending = false;
            ProblemReportSendButton.IsEnabled = true;
            ShowStatus(ProblemReportStatusText, ok ? LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ReportSent] : message, ok);
        }

        private void SelectLobbyTab(object sender, RoutedEventArgs e)
        {
            _navigatingSidebar = false;
            SetSelectedPanel(LobbyTab);
            _ = LoadLobby();
        }

        private void SelectEmulationTab(object sender, RoutedEventArgs e)
        {
            if (!_isGameRunningContext)
            {
                return;
            }

            _navigatingSidebar = false;
            SetSelectedPanel(EmulationTab);
        }

        private static Ryujinx.Ava.UI.ViewModels.MainWindowViewModel RunningViewModel => RyujinxApp.MainWindow?.ViewModel;

        private void ToggleFullscreen_Click(object sender, RoutedEventArgs e)
        {
            Ryujinx.Ava.UI.Windows.MainWindow mainWindow = RyujinxApp.MainWindow;
            RunningViewModel?.ToggleFullscreen();
            CloseRequested?.Invoke(this, EventArgs.Empty);
            Dispatcher.UIThread.Post(() => mainWindow?.Activate());
        }

        private void PauseGame_Click(object sender, RoutedEventArgs e)
        {
            if (RunningViewModel?.AppHost is not { } host)
            {
                return;
            }

            if (host.Device.System.IsPaused)
            {
                host.Resume();
            }
            else
            {
                host.Pause();
            }

            PauseGameButton.Content = host.Device.System.IsPaused ? "Resume Game" : "Pause Game";
        }

        private void RestartGame_Click(object sender, RoutedEventArgs e) => RunningViewModel?.RestartEmulation();

        private async void StopGame_Click(object sender, RoutedEventArgs e)
        {
            if (RunningViewModel?.AppHost is { } host)
            {
                if (!ConfigurationState.Instance.ShowConfirmExit)
                {
                    host.Stop();
                    CloseRequested?.Invoke(this, EventArgs.Empty);
                    return;
                }

                ContentDialog dialog = new()
                {
                    Title = LocaleManager.Instance[LocaleKeys.DialogStopEmulationTitle],
                    Content = LocaleManager.Instance[LocaleKeys.DialogStopEmulationMessage],
                    PrimaryButtonText = LocaleManager.Instance[LocaleKeys.InputDialogYes],
                    SecondaryButtonText = LocaleManager.Instance[LocaleKeys.InputDialogNo],
                    DefaultButton = ContentDialogButton.None,
                };

                Style gamepadSelectedButtonStyle = new(x => x.OfType<Button>().Class("gamepad-selected"));
                gamepadSelectedButtonStyle.Setters.Add(new Setter(Button.BackgroundProperty, Brush.Parse("#FF3EE8C8")));
                gamepadSelectedButtonStyle.Setters.Add(new Setter(Button.ForegroundProperty, Brush.Parse("#FF17191D")));
                gamepadSelectedButtonStyle.Setters.Add(new Setter(Button.BorderBrushProperty, Brush.Parse("#FF3EE8C8")));
                gamepadSelectedButtonStyle.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(2)));
                gamepadSelectedButtonStyle.Setters.Add(new Setter(Button.FontWeightProperty, FontWeight.SemiBold));
                dialog.Styles.Add(gamepadSelectedButtonStyle);

                bool confirmed = false;
                dialog.PrimaryButtonClick += (_, _) => confirmed = true;

                // Let the dashboard's gamepad poll drive this choice while the confirmation is
                // open. In game context A accepts and B cancels, matching the dashboard controls.
                _navigationTimer.Stop();
                bool selectedPrimary = true;
                void UpdateDialogSelection()
                {
                    Button[] buttons = dialog.GetVisualDescendants().OfType<Button>().ToArray();
                    Button primaryButton = buttons.FirstOrDefault(button =>
                        string.Equals(button.Content?.ToString(), dialog.PrimaryButtonText, StringComparison.Ordinal));
                    Button secondaryButton = buttons.FirstOrDefault(button =>
                        string.Equals(button.Content?.ToString(), dialog.SecondaryButtonText, StringComparison.Ordinal));

                    primaryButton?.Classes.Set("gamepad-selected", selectedPrimary);
                    secondaryButton?.Classes.Set("gamepad-selected", !selectedPrimary);
                }

                dialog.Opened += (_, _) => UpdateDialogSelection();
                bool upWasDown = false;
                bool downWasDown = false;
                bool leftWasDown = false;
                bool rightWasDown = false;
                bool confirmWasDown = false;
                bool backWasDown = false;
                IGamepad initialGamepad = GetNavigationGamepad();
                if (initialGamepad != null)
                {
                    GamepadStateSnapshot initialSnapshot = initialGamepad.GetMappedStateSnapshot();
                    (float initialStickX, float initialStickY) = initialSnapshot.GetStick(StickInputId.Left);
                    upWasDown = initialSnapshot.IsPressed(GamepadButtonInputId.DpadUp) || initialStickY > 0.5f;
                    downWasDown = initialSnapshot.IsPressed(GamepadButtonInputId.DpadDown) || initialStickY < -0.5f;
                    leftWasDown = initialSnapshot.IsPressed(GamepadButtonInputId.DpadLeft) || initialStickX < -0.5f;
                    rightWasDown = initialSnapshot.IsPressed(GamepadButtonInputId.DpadRight) || initialStickX > 0.5f;
                    confirmWasDown = initialSnapshot.IsPressed(GamepadButtonInputId.A);
                    backWasDown = initialSnapshot.IsPressed(GamepadButtonInputId.B);
                }

                DispatcherTimer dialogNavigationTimer = new() { Interval = TimeSpan.FromMilliseconds(75) };
                dialogNavigationTimer.Tick += (_, _) =>
                {
                    IGamepad gamepad = GetNavigationGamepad();
                    if (gamepad == null)
                    {
                        return;
                    }

                    GamepadStateSnapshot snapshot = gamepad.GetMappedStateSnapshot();
                    (float stickX, float stickY) = snapshot.GetStick(StickInputId.Left);
                    bool up = snapshot.IsPressed(GamepadButtonInputId.DpadUp) || stickY > 0.5f;
                    bool down = snapshot.IsPressed(GamepadButtonInputId.DpadDown) || stickY < -0.5f;
                    bool left = snapshot.IsPressed(GamepadButtonInputId.DpadLeft) || stickX < -0.5f;
                    bool right = snapshot.IsPressed(GamepadButtonInputId.DpadRight) || stickX > 0.5f;
                    bool confirm = snapshot.IsPressed(GamepadButtonInputId.A);
                    bool back = snapshot.IsPressed(GamepadButtonInputId.B);

                    if ((up && !upWasDown) || (down && !downWasDown) ||
                        (left && !leftWasDown) || (right && !rightWasDown))
                    {
                        selectedPrimary = !selectedPrimary;
                        UpdateDialogSelection();
                    }

                    if (confirm && !confirmWasDown)
                    {
                        confirmed = selectedPrimary;
                        dialog.Hide(selectedPrimary ? ContentDialogResult.Primary : ContentDialogResult.Secondary);
                    }
                    else if (back && !backWasDown)
                    {
                        dialog.Hide(ContentDialogResult.Secondary);
                    }

                    upWasDown = up;
                    downWasDown = down;
                    leftWasDown = left;
                    rightWasDown = right;
                    confirmWasDown = confirm;
                    backWasDown = back;
                };

                try
                {
                    Task<ContentDialogResult> dialogTask = ContentDialogHelper.ShowAsync(dialog);
                    dialogNavigationTimer.Start();
                    await dialogTask;

                    if (confirmed)
                    {
                        host.Stop();
                    }
                }
                finally
                {
                    dialogNavigationTimer.Stop();
                    _navigationTimer.Start();
                }

                if (confirmed)
                {
                    CloseRequested?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private void SetSelectedPanel(Control selected)
        {
            if (ReportOverlay.IsVisible)
            {
                ReportOverlay.IsVisible = false;
                _reportTarget = 0;
                _reportReason = "";
            }

            _selectedPanel = selected;
            _selectedNavigationIndex = selected == AccountTab ? 0 :
                selected == FriendsTab ? 1 :
                selected == RequestsTab ? 2 :
                selected == LobbyTab ? 3 :
                selected == ActivityTab ? 4 :
                selected == HistoryTab ? 5 :
                selected == ReportProblemTab ? 6 : 7;
            AccountTab.IsVisible = selected == AccountTab;
            FriendsTab.IsVisible = selected == FriendsTab;
            RequestsTab.IsVisible = selected == RequestsTab;
            LobbyTab.IsVisible = selected == LobbyTab;
            ActivityTab.IsVisible = selected == ActivityTab;
            HistoryTab.IsVisible = selected == HistoryTab;
            ReportProblemTab.IsVisible = selected == ReportProblemTab;
            EmulationTab.IsVisible = selected == EmulationTab && _isGameRunningContext;

            SetNavigationSelection(AccountTabButton, selected == AccountTab);
            SetNavigationSelection(FriendsTabButton, selected == FriendsTab);
            SetNavigationSelection(RequestsTabButton, selected == RequestsTab);
            SetNavigationSelection(LobbyTabButton, selected == LobbyTab);
            SetNavigationSelection(ActivityTabButton, selected == ActivityTab);
            SetNavigationSelection(HistoryTabButton, selected == HistoryTab);
            SetNavigationSelection(ReportProblemButton, selected == ReportProblemTab);
            SetNavigationSelection(EmulationTabButton, selected == EmulationTab);
        }

        private void FriendCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: ulong pid })
            {
                ShowFriendProfile(pid);
                Dispatcher.UIThread.Post(FocusFirstContentControl);
            }
        }

        private void ShowFriendProfile(ulong pid)
        {
            NextendoFriendModel friend = _friends.FirstOrDefault(item => item.Pid == pid);
            if (friend is null)
            {
                return;
            }

            SelectedFriendName.Text = friend.Name;
            SelectedFriendCode.Text = friend.FriendCode;
            SelectedFriendStatus.Text = friend.StatusText;
            SelectedFriendImage.Source = friend.Image is { Length: > 0 } ? new Bitmap(new MemoryStream(friend.Image)) : null;
            SelectedFriendPresence.Text = friend.IsOnline ? "Online" : "Offline";
            SelectedFriendPresence.Foreground = friend.StatusTextColor;
            SelectedFriendStatus.Foreground = friend.StatusTextColor;
            SelectedFriendGameInitial.Text = "";
            SelectedFriendGameImage.Source = null;
            SelectedFriendGameImage.IsVisible = false;

            string gameName = NextendoGameNames.Resolve(friend.AppId);
            if (friend.IsOnline && !string.IsNullOrWhiteSpace(friend.AppId))
            {
                SelectedFriendStatus.Text = gameName ?? friend.StatusText;
                SelectedFriendGameInitial.Text = string.IsNullOrEmpty(gameName) ? "?" : gameName[..1].ToUpperInvariant();
                SetSelectedFriendGameCover(friend.AppId);
            }
            else
            {
                SelectedFriendStatus.Text = friend.IsOnline ? "Main Menu" : "Offline";
                SelectedFriendGameInitial.Text = "-";
            }
            SelectedFriendFavoriteButton.Tag = pid;
            SelectedFriendFavoriteButton.Content = friend.Favorite ? "★ Favorite" : "☆ Favorite";
            SelectedFriendFavoriteButton.Foreground = friend.Favorite ? Brush.Parse("#F5C518") : Brush.Parse("#FFCCCCCC");
            SelectedFriendRemoveButton.Tag = pid;
            FriendsListScroll.IsVisible = false;
            FriendProfileScroll.IsVisible = true;
            _ = LoadSelectedFriendHistory(pid);
        }

        private async Task LoadSelectedFriendHistory(ulong pid)
        {
            _selectedFriendHistory.Clear();
            NoSelectedFriendHistoryText.IsVisible = false;

            List<NextendoApi.HistoryItem> history = await NextendoApi.GetFriendHistoryAsync(pid);
            if (!FriendProfileScroll.IsVisible || SelectedFriendRemoveButton.Tag is not ulong selectedPid || selectedPid != pid)
            {
                return;
            }

            foreach (NextendoApi.HistoryItem item in history)
            {
                byte[] icon = null;
                if (!string.IsNullOrEmpty(item.IconBase64))
                {
                    try { icon = Convert.FromBase64String(item.IconBase64); } catch { /* Ignore a missing or invalid cover. */ }
                }

                _selectedFriendHistory.Add(new NextendoHistoryModel
                {
                    Name = string.IsNullOrWhiteSpace(item.Name) ? NextendoGameNames.Resolve(item.TitleId) ?? item.TitleId : item.Name,
                    Icon = icon,
                    PlayedText = FormatPlayed(item.Seconds),
                    LastText = FormatLast(item.LastPlayed),
                });
            }

            NoSelectedFriendHistoryText.IsVisible = _selectedFriendHistory.Count == 0;
        }

        private async void AccountNetworkCheck_Click(object sender, RoutedEventArgs e)
        {
            await CheckAccountNetworkAsync();
        }

        private async Task CheckAccountNetworkAsync()
        {
            if (_accountNetworkCheckRunning)
            {
                return;
            }

            _accountNetworkCheckRunning = true;
            AccountNetworkCheckButton.IsEnabled = false;
            AccountNetworkCheckButton.Content = "Checking…";

            try
            {
                NextendoNetworkCheck.Result result = await NextendoNetworkCheck.CheckAsync();
                AccountPingValue.Text = result.Reachable ? $"{result.LatencyMs} ms" : "Unavailable";
                AccountPingValue.Foreground = Brush.Parse(result.LatencyColor);
                (LocaleKeys natLabel, _) = result.Nat switch
                {
                    NextendoNetworkCheck.NatType.Open => (LocaleKeys.Dialog_Nextendo_NatOpen, LocaleKeys.Dialog_Nextendo_NatOpenTooltip),
                    NextendoNetworkCheck.NatType.Strict => (LocaleKeys.Dialog_Nextendo_NatStrict, LocaleKeys.Dialog_Nextendo_NatStrictTooltip),
                    _ => (LocaleKeys.Dialog_Nextendo_NatUnknown, LocaleKeys.Dialog_Nextendo_NatUnknownTooltip),
                };
                AccountNatValue.Text = LocaleManager.Instance[natLabel];
                AccountNatValue.Foreground = Brush.Parse(result.NatColor);
            }
            finally
            {
                _accountNetworkCheckRunning = false;
                AccountNetworkCheckButton.IsEnabled = true;
                AccountNetworkCheckButton.Content = "Check";
            }
        }

        private void SetSelectedFriendGameCover(string appId)
        {
            try
            {
                ApplicationLibrary library = RyujinxApp.MainWindow?.ApplicationLibrary;
                if (library is null || !ulong.TryParse(appId, System.Globalization.NumberStyles.HexNumber, null, out ulong titleId))
                {
                    return;
                }

                ApplicationData game = library.Applications.Items.FirstOrDefault(app => app.Id == titleId || app.IdBase == (titleId & ~0x1FFFUL));
                if (game?.Icon is not { Length: > 0 } icon)
                {
                    return;
                }

                SelectedFriendGameImage.Source = new Bitmap(new MemoryStream(icon));
                SelectedFriendGameImage.IsVisible = true;
            }
            catch
            {
                // A missing or unsupported local icon should not prevent viewing a friend's profile.
            }
        }

        private void BackToFriends_Click(object sender, RoutedEventArgs e)
        {
            FriendProfileScroll.IsVisible = false;
            FriendsListScroll.IsVisible = true;
        }

        private IGamepad GetNavigationGamepad()
        {
            Ryujinx.Input.HLE.InputManager inputManager = RyujinxApp.MainWindow?.InputManager;
            if (inputManager?.GamepadDriver == null)
            {
                return null;
            }

            Ryujinx.Common.Configuration.Hid.InputConfig config = RyujinxApp.MainWindow.ViewModel.AppHost?.NpadManager?.GetPlayerInputConfigByIndex(0);
            string targetId = config is Ryujinx.Common.Configuration.Hid.Controller.StandardControllerInputConfig ? config.Id : null;

            if (string.IsNullOrEmpty(targetId))
            {
                targetId = inputManager.GamepadDriver.GetGamepads().FirstOrDefault(gamepad => gamepad.IsConnected)?.Id;
            }

            if (string.IsNullOrEmpty(targetId))
            {
                _navigationGamepad?.Dispose();
                _navigationGamepad = null;
                _navigationGamepadId = null;
                return null;
            }

            if (_navigationGamepad is { IsConnected: true } && _navigationGamepadId == targetId)
            {
                return _navigationGamepad;
            }

            _navigationGamepad?.Dispose();
            _navigationGamepad = null;
            _navigationGamepadId = targetId;

            try
            {
                _navigationGamepad = inputManager.GamepadDriver.GetGamepad(targetId);
                if (_navigationGamepad != null && config != null)
                {
                    _navigationGamepad.SetConfiguration(config);
                }
            }
            catch
            {
                _navigationGamepad = null;
            }

            return _navigationGamepad;
        }

        private void PollDashboardGamepad()
        {
            IGamepad gamepad = GetNavigationGamepad();
            if (gamepad == null)
            {
                bool closeDashboard = _closeDashboardOnBackRelease;
                _closeDashboardOnBackRelease = false;
                ResetNavigationButtons();
                if (closeDashboard)
                {
                    CloseRequested?.Invoke(this, EventArgs.Empty);
                }

                return;
            }

            GamepadStateSnapshot snapshot = gamepad.GetMappedStateSnapshot();
            (float stickX, float stickY) = snapshot.GetStick(StickInputId.Left);
            (_, float rightStickY) = snapshot.GetStick(StickInputId.Right);
            bool up = snapshot.IsPressed(GamepadButtonInputId.DpadUp) || stickY > 0.5f;
            bool down = snapshot.IsPressed(GamepadButtonInputId.DpadDown) || stickY < -0.5f;
            bool right = snapshot.IsPressed(GamepadButtonInputId.DpadRight) || stickX > 0.5f;
            bool left = snapshot.IsPressed(GamepadButtonInputId.DpadLeft) || stickX < -0.5f;
            // On the launcher the console layout uses B to activate and A to return. While a
            // title is running, honor the swapped in-game A/B mapping for dashboard navigation.
            bool confirm = snapshot.IsPressed(_isGameRunningContext ? GamepadButtonInputId.A : GamepadButtonInputId.B);
            bool back = snapshot.IsPressed(_isGameRunningContext ? GamepadButtonInputId.B : GamepadButtonInputId.A);
            bool showingFriend = FriendsTab.IsVisible && FriendProfileScroll.IsVisible;

            if (_closeDashboardOnBackRelease)
            {
                _navigationBackDown = back;
                if (!back)
                {
                    _closeDashboardOnBackRelease = false;
                    CloseRequested?.Invoke(this, EventArgs.Empty);
                }

                return;
            }

            ScrollSelectedPanel(rightStickY);

            if (ReportOverlay.IsVisible && back && !_navigationBackDown)
            {
                ReportCancel_Click(this, null);
            }
            else if (ReportProblemTab.IsVisible && back && !_navigationBackDown)
            {
                CancelProblemReport_Click(this, null);
            }
            else if (showingFriend && back && !_navigationBackDown)
            {
                BackToFriends_Click(this, null);
                Dispatcher.UIThread.Post(FocusFirstContentControl);
            }
            else if (showingFriend)
            {
                if (up && !_navigationUpDown)
                    MoveContentFocus(0, -1);
                else if (down && !_navigationDownDown)
                    MoveContentFocus(0, 1);
                else if (left && !_navigationLeftDown)
                    MoveContentFocus(-1, 0);
                else if (right && !_navigationRightDown)
                    MoveContentFocus(1, 0);
                else if (confirm && !_navigationConfirmDown)
                    ActivateFocusedContentControl();
            }
            else if (_navigatingSidebar)
            {
                if (up && !_navigationUpDown)
                {
                    MovePanelSelection(-1);
                    FocusSelectedNavigationButton();
                }
                else if (down && !_navigationDownDown)
                {
                    MovePanelSelection(1);
                    FocusSelectedNavigationButton();
                }
                if ((right && !_navigationRightDown) || (confirm && !_navigationConfirmDown))
                {
                    ActivateSelectedCategory();
                }
                else if (back && !_navigationBackDown)
                {
                    _closeDashboardOnBackRelease = true;
                }
            }
            else
            {
                if (back && !_navigationBackDown)
                {
                    _navigatingSidebar = true;
                    FocusSelectedNavigationButton();
                }
                else if (up && !_navigationUpDown)
                {
                    MoveContentFocus(0, -1);
                }
                else if (down && !_navigationDownDown)
                {
                    MoveContentFocus(0, 1);
                }
                else if (left && !_navigationLeftDown)
                {
                    MoveContentFocus(-1, 0);
                }
                else if (right && !_navigationRightDown)
                {
                    MoveContentFocus(1, 0);
                }
                else if (confirm && !_navigationConfirmDown)
                {
                    ActivateFocusedContentControl();
                }
            }

            _navigationUpDown = up;
            _navigationDownDown = down;
            _navigationLeftDown = left;
            _navigationRightDown = right;
            _navigationConfirmDown = confirm;
            _navigationBackDown = back;
        }

        private void ScrollSelectedPanel(float rightStickY)
        {
            if (Math.Abs(rightStickY) < 0.2f)
            {
                return;
            }

            ScrollViewer scrollViewer = _selectedPanel.GetLogicalDescendants()
                .OfType<ScrollViewer>()
                .FirstOrDefault(viewer => IsVisibleInLogicalTree(viewer) && viewer.Extent.Height > viewer.Viewport.Height);

            if (scrollViewer == null)
            {
                return;
            }

            double maxOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
            double offsetY = Math.Clamp(scrollViewer.Offset.Y - rightStickY * 45, 0, maxOffset);
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, offsetY);
        }

        private List<Control> GetVisibleFocusableControls()
        {
            return _selectedPanel.GetLogicalDescendants()
                .OfType<Control>()
                .Where(control => control.Focusable && control.IsEnabled && IsVisibleInLogicalTree(control))
                .ToList();
        }

        private static bool IsVisibleInLogicalTree(ILogical logical)
        {
            for (ILogical current = logical; current != null; current = current.LogicalParent)
            {
                if (current is Control control && !control.IsVisible)
                {
                    return false;
                }
            }

            return true;
        }

        private void FocusFirstContentControl()
        {
            List<Control> controls = GetVisibleFocusableControls();
            _contentFocusIndex = 0;
            controls.FirstOrDefault()?.Focus(NavigationMethod.Directional);
        }

        private void MoveContentFocus(int horizontalDirection, int verticalDirection)
        {
            List<Control> controls = GetVisibleFocusableControls();
            if (controls.Count == 0)
            {
                return;
            }

            int focusedIndex = controls.FindIndex(control => control.IsFocused);
            if (focusedIndex < 0)
            {
                focusedIndex = Math.Clamp(_contentFocusIndex, 0, controls.Count - 1);
            }
            Control current = controls[focusedIndex];
            Point? currentCenter = current.TranslatePoint(new Point(current.Bounds.Width / 2, current.Bounds.Height / 2), _selectedPanel);
            if (currentCenter is null)
            {
                return;
            }

            Control candidate = null;
            double bestScore = double.PositiveInfinity;
            for (int index = 0; index < controls.Count; index++)
            {
                Control control = controls[index];
                if (control == current)
                {
                    continue;
                }

                Point? point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), _selectedPanel);
                if (point is null)
                {
                    continue;
                }

                double dx = point.Value.X - currentCenter.Value.X;
                double dy = point.Value.Y - currentCenter.Value.Y;
                double primary;
                double cross;
                if (horizontalDirection != 0)
                {
                    if (dx * horizontalDirection <= 1)
                    {
                        continue;
                    }
                    primary = Math.Abs(dx);
                    cross = Math.Abs(dy);
                }
                else
                {
                    if (dy * verticalDirection <= 1)
                    {
                        continue;
                    }
                    primary = Math.Abs(dy);
                    cross = Math.Abs(dx);
                }

                double score = primary + cross * 1.5;
                if (score < bestScore)
                {
                    bestScore = score;
                    candidate = control;
                    _contentFocusIndex = index;
                }
            }

            candidate?.Focus(NavigationMethod.Directional);
        }

        private void ActivateFocusedContentControl()
        {
            List<Control> controls = GetVisibleFocusableControls();
            if (controls.Count == 0)
            {
                return;
            }

            int focused = controls.FindIndex(control => control.IsFocused);
            if (focused >= 0)
            {
                _contentFocusIndex = focused;
            }

            Control selected = controls[_contentFocusIndex % controls.Count];
            if (selected is Button button)
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            else
            {
                selected.Focus(NavigationMethod.Directional);
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (FriendsTab.IsVisible && FriendProfileScroll.IsVisible)
                {
                    FocusFirstContentControl();
                }
            });
        }

        private void FocusSelectedNavigationButton()
        {
            GetNavigationButtons()[_selectedNavigationIndex].Focus(NavigationMethod.Directional);
        }

        private void MovePanelSelection(int direction)
        {
            Button[] buttons = GetNavigationButtons();
            _selectedNavigationIndex = (_selectedNavigationIndex + direction + buttons.Length) % buttons.Length;
        }

        private void ActivateSelectedCategory()
        {
            _navigatingSidebar = false;
            switch (_selectedNavigationIndex)
            {
                case 0: SelectAccountTab(this, null); break;
                case 1: SelectFriendsTab(this, null); break;
                case 2: SelectRequestsTab(this, null); break;
                case 3: SelectLobbyTab(this, null); break;
                case 4: SelectActivityTab(this, null); break;
                case 5: SelectHistoryTab(this, null); break;
                case 6: ReportProblem_Click(this, null); return;
                case 7: SelectEmulationTab(this, null); break;
            }

            _navigatingSidebar = false;
            FocusFirstContentControl();
        }

        private Button[] GetNavigationButtons()
        {
            return _isGameRunningContext
                ? [AccountTabButton, FriendsTabButton, RequestsTabButton, LobbyTabButton, ActivityTabButton, HistoryTabButton, ReportProblemButton, EmulationTabButton]
                : [AccountTabButton, FriendsTabButton, RequestsTabButton, LobbyTabButton, ActivityTabButton, HistoryTabButton, ReportProblemButton];
        }

        private void ResetNavigationButtons()
        {
            _navigationUpDown = _navigationDownDown = _navigationLeftDown = _navigationRightDown = false;
            _navigationConfirmDown = _navigationBackDown = false;
        }

        private static void SetNavigationSelection(Button button, bool selected)
        {
            bool hasSelectedClass = button.Classes.Contains("selected");

            if (selected && !hasSelectedClass)
            {
                button.Classes.Add("selected");
            }
            else if (!selected && hasSelectedClass)
            {
                button.Classes.Remove("selected");
            }
        }

        private void CloseDashboard_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
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
            _lobby.Clear();
            _invites.Clear();
            _syncedHistory.Clear();

            UpdateInviteCountBadge();
            RefreshOwnStatus();
        }

        private async Task LoadFriends()
        {
            (List<NextendoApi.Friend> friends, List<NextendoApi.Friend> requests) = await NextendoApi.GetSocialAsync();

            Fill(_friends, OrderFriendsByPriority(friends));
            Fill(_requests, requests);
            UpdateInviteCountBadge();
            RefreshGameInviteFriends(friends);
            NoFriendsText.IsVisible = _friends.Count == 0;
            FriendRequestsSection.IsVisible = _requests.Count > 0;
            NoRequestsText.IsVisible = !GameInviteSection.IsVisible && _requests.Count == 0 && _invites.Count == 0;

            if (FriendProfileScroll.IsVisible)
            {
                NextendoFriendModel selected = _friends.FirstOrDefault(friend => friend.Pid == (ulong)(SelectedFriendRemoveButton.Tag ?? 0UL));
                if (selected is null)
                {
                    FriendProfileScroll.IsVisible = false;
                    FriendsListScroll.IsVisible = true;
                }
                else
                {
                    ShowFriendProfile(selected.Pid);
                    SelectedFriendFavoriteButton.Content = selected.Favorite ? "★ Favorite" : "☆ Favorite";
                    SelectedFriendFavoriteButton.Foreground = selected.Favorite ? Brush.Parse("#F5C518") : Brush.Parse("#FFCCCCCC");
                }
            }

            int online = _friends.Count(friend => friend.IsOnline);
            SidebarOnlineCountText.Text = $"Online: {online}";
        }

        private void RefreshGameInviteFriends(List<NextendoApi.Friend> friends)
        {
            if (_gameInvitationRequest == null)
            {
                return;
            }

            IEnumerable<NextendoApi.Friend> eligible = friends.Where(friend => friend.IsOnline);
            if (_gameInvitationRequest.Mode == 9)
            {
                eligible = eligible.Where(friend => _gameInvitationRequest.AccountIds.Contains(friend.Pid));
            }

            List<NextendoApi.Friend> ordered = OrderFriendsByPriority(eligible.ToList());
            HashSet<ulong> eligiblePids = ordered.Select(friend => friend.Pid).ToHashSet();
            _selectedGameInviteRecipients.IntersectWith(eligiblePids);
            Fill(_gameInviteFriends, ordered);
            foreach (NextendoFriendModel friend in _gameInviteFriends)
            {
                bool requestedByApplet = _gameInvitationRequest.Mode == 9 &&
                    _gameInvitationRequest.AccountIds.Contains(friend.Pid) &&
                    _selectedGameInviteRecipients.Count < Math.Min(_gameInvitationRequest.RecipientLimit, 15);
                if (requestedByApplet)
                {
                    _selectedGameInviteRecipients.Add(friend.Pid);
                }

                friend.IsSelected = _selectedGameInviteRecipients.Contains(friend.Pid);
            }

            GameInviteStatusText.Text = ordered.Count == 0
                ? "No friends are online and available to invite."
                : $"Choose up to {Math.Min(_gameInvitationRequest.RecipientLimit, 15)} online friends to invite.";
            SendGameInviteButton.IsEnabled = _selectedGameInviteRecipients.Count > 0;
            NoRequestsText.IsVisible = false;
        }

        private static List<NextendoApi.Friend> OrderFriendsByPriority(IEnumerable<NextendoApi.Friend> friends)
        {
            static int Priority(NextendoApi.Friend friend)
            {
                if (!friend.IsOnline) return 4;
                bool playing = !string.IsNullOrWhiteSpace(friend.AppId);
                if (friend.Favorite) return playing ? 0 : 1;
                return playing ? 2 : 3;
            }

            return friends.OrderBy(Priority)
                .ThenBy(friend => friend.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private async Task LoadActivity()
        {
            (List<NextendoApi.Friend> friends, _) = await NextendoApi.GetSocialAsync();
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
                    IsMe = p.IsMe,
                    GameName = ResolveGame(p.TitleId),
                    SeenAt = p.SeenAt,
                });
            }

            RecentText.IsVisible = _recent.Count == 0;
        }

        private async Task LoadHistory()
        {
            List<NextendoApi.HistoryItem> merged = await NextendoApi.SyncHistoryAsync(NextendoHistorySync.CollectLocalHistory());

            if (merged.Count == 0 && _syncedHistory.Count > 0)
            {
                merged = _syncedHistory;
            }
            else
            {
                _syncedHistory = merged;
            }
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

            NoAccountHistoryText.IsVisible = _history.Count == 0;
            NoHistoryText.IsVisible = _history.Count == 0;
        }

        private async Task LoadLobby()
        {
            NextendoApi.NextendoLobby lobby = await NextendoApi.GetMyLobbyAsync();
            if (!lobby.InLobby)
            {
                _lobby.Clear();
                NoLobbyText.IsVisible = true;
                LobbyScroll.IsVisible = false;
                LobbyGameText.Text = "-";
                LobbyStateText.Text = "";
                return;
            }

            NoLobbyText.IsVisible = false;
            LobbyScroll.IsVisible = true;
            LobbyGameText.Text = ResolveGame(lobby.TitleId);
            LocaleKeys? state = lobby.StateCode switch
            {
                "searching" => LocaleKeys.Dialog_Nextendo_LobbyStateSearching,
                "matched" => LocaleKeys.Dialog_Nextendo_LobbyStateMatched,
                _ => null,
            };
            LobbyStateText.Text = state is null
                ? LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.Dialog_Nextendo_LobbyCountFormat, lobby.Count, lobby.Max)
                : LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.Dialog_Nextendo_LobbyStateFormat, lobby.Count, lobby.Max, LocaleManager.Instance[state.Value]);

            (List<NextendoApi.Friend> friends, _) = await NextendoApi.GetSocialAsync();
            _lobby.Clear();
            foreach (NextendoApi.NextendoPlayer player in lobby.Players)
            {
                _lobby.Add(new NextendoLobbyPlayerModel
                {
                    Pid = player.Pid,
                    Name = string.IsNullOrEmpty(player.Name) ? $"#{player.Pid}" : player.Name,
                    Image = await NextendoApi.GetAvatarAsync(player.Pid, player.AvatarUrl),
                    Known = player.Known,
                    Host = player.Host,
                    IsMe = player.IsMe,
                    IsFriend = friends.Any(friend => friend.Pid == player.Pid),
                });
            }
        }

        private void RefreshInvites()
        {
            Dispatcher.UIThread.Post(() =>
            {
                _invites.Clear();
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                foreach (NextendoApi.GameInvitation invite in NextendoGameInvites.Pending())
                {
                    _invites.Add(new NextendoGameInviteModel
                    {
                        Id = invite.Id,
                        Name = invite.SenderName,
                        FriendCode = _friends.FirstOrDefault(friend => friend.Pid == invite.SenderPid)?.FriendCode ?? "",
                        Detail = LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.Dialog_Nextendo_GameInviteLeftFormat,
                            NextendoGameInvites.GameName(invite.TitleId), Math.Max(1, (invite.ExpiresAt - now + 59) / 60)),
                        Image = _friends.FirstOrDefault(friend => friend.Pid == invite.SenderPid)?.Image,
                    });
                }
                UpdateInviteCountBadge();
                InvitesPanel.IsVisible = _invites.Count > 0;
                NoRequestsText.IsVisible = !GameInviteSection.IsVisible && _invites.Count == 0 && _requests.Count == 0;
            });
        }

        private void UpdateInviteCountBadge()
        {
            int pendingCount = _requests.Count + _invites.Count;
            InviteCountText.Text = pendingCount.ToString();
            InviteCountBadge.IsVisible = pendingCount > 0;
        }

        private void AcceptInvite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string id })
            {
                string error = NextendoGameInvites.Accept(id);
                if (error != null) ShowStatus(FriendsStatusText, error, false);
            }
        }

        private void DeclineInvite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string id }) NextendoGameInvites.Decline(id);
        }

        private async void AddLobbyFriend_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: ulong pid }) return;
            NextendoApi.NextendoPlayer player = (await NextendoApi.GetMyLobbyAsync()).Players.FirstOrDefault(item => item.Pid == pid);
            if (player is null || string.IsNullOrEmpty(player.FriendCode)) return;
            (bool ok, string message) = await NextendoApi.AddFriendAsync(player.FriendCode);
            ShowStatus(RecentStatusText, message, ok);
            if (ok) await LoadFriends();
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

        private async void CopyFriendCode_Click(object sender, RoutedEventArgs e)
        {
            string friendCode = SelectedFriendCode.Text;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null && !string.IsNullOrWhiteSpace(friendCode))
            {
                await clipboard.SetTextAsync(friendCode);
                ShowStatus(FriendsStatusText, "Friend code copied.", true);
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

            NextendoLobbyPlayerModel jugador = _recent.FirstOrDefault(p => p.Pid == pid)
                ?? _lobby.FirstOrDefault(p => p.Pid == pid);

            _reportReturnPanel = ActivityTab.IsVisible ? ActivityTab : LobbyTab;

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
            _selectedPanel = ReportOverlay;
            _navigatingSidebar = false;
            Dispatcher.UIThread.Post(FocusFirstContentControl);
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

        private void CerrarModale()
        {
            _reportTarget = 0;
            _reportReason = "";
            ReportOverlay.IsVisible = false;
            SetSelectedPanel(_reportReturnPanel ?? ActivityTab);
            _navigatingSidebar = false;
            Dispatcher.UIThread.Post(FocusFirstContentControl);
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
