using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using DynamicData;
using FluentAvalonia.UI.Controls;
using Gommon;
using Ryujinx.Ava.Common;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Input;
using Ryujinx.Ava.Systems;
using Ryujinx.Ava.Systems.AppLibrary;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.Systems.Configuration.UI;
using Ryujinx.Ava.UI.Applet;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.Models;
using Ryujinx.Ava.UI.ViewModels;
using Ryujinx.Ava.UI.Views.Misc;
using Ryujinx.Ava.Utilities;
using Ryujinx.Common;
using Ryujinx.Common.Helper;
using Ryujinx.Common.Logging;
using Ryujinx.Common.UI;
using Ryujinx.Graphics.Gpu;
using Ryujinx.HLE.FileSystem;
using Ryujinx.HLE.HOS;
using Ryujinx.HLE.HOS.Applets.MyPage;
using Ryujinx.HLE.HOS.Services.Account.Acc;
using Ryujinx.Input.HLE;
using Ryujinx.Input.SDL3;
using Ryujinx.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.Windows
{
    public partial class MainWindow : StyleableAppWindow
    {
        public MainWindowViewModel ViewModel { get; }

        internal readonly AvaHostUIHandler UiHandler;

        private bool _isLoading;
        private bool _applicationsLoadedOnce;
        private double _windowStartupWidthDelta;
        private double _windowStartupHeightDelta;

        private UserChannelPersistence _userChannelPersistence;
        private static bool _deferLoad;
        private static string _launchPath;
        private static string _launchApplicationId;
        private static bool _startFullscreen;
        private IDisposable _appLibraryAppsSubscription;

        public VirtualFileSystem VirtualFileSystem { get; private set; }
        public ContentManager ContentManager { get; private set; }
        public AccountManager AccountManager { get; private set; }

        public LibHacHorizonManager LibHacHorizonManager { get; private set; }

        public InputManager InputManager { get; private set; }

        public SettingsWindow SettingsWindow { get; set; }

        public bool IsNextendoDashboardOpen => NextendoDashboardOverlay.IsVisible || _nextendoGameDashboardWindow?.IsVisible == true;

        private Point _nextendoDashboardResizeStart;
        private double _nextendoDashboardResizeWidth;
        private double _nextendoDashboardResizeHeight;
        private bool _isResizingNextendoDashboard;
        private bool _dashboardBlockedGameInput;
        private bool _nextendoDashboardTakesFocus;
        private NextendoProfileView _activeNextendoDashboard;
        private Avalonia.Controls.Window _nextendoGameDashboardWindow;
        private Border _nextendoGameDashboardFrame;
        private Point _gameDashboardResizeStart;
        private double _gameDashboardResizeWidth;
        private double _gameDashboardResizeHeight;
        private bool _isResizingGameDashboard;

        /// <summary>Shows or hides the Nextendo dashboard in the main window over the renderer.</summary>
        public void ToggleNextendoDashboard()
        {
            if (IsNextendoDashboardOpen)
            {
                CloseNextendoDashboard();

                return;
            }

            NextendoProfileView dashboard = new(ViewModel.IsGameRunning);
            _activeNextendoDashboard = dashboard;
            dashboard.CloseRequested += (_, _) => CloseNextendoDashboard();
            SuspendGameControllerInputForDashboard();

            int savedWidth = ConfigurationState.Instance.UI.WindowStartup.NextendoDashboardWidth.Value;
            int savedHeight = ConfigurationState.Instance.UI.WindowStartup.NextendoDashboardHeight.Value;

            if (ViewModel.IsGameRunning)
            {
                OpenGameDashboardWindow(dashboard, savedWidth, savedHeight);
                return;
            }

            NextendoDashboardContent.Content = dashboard;
            if (savedWidth >= 700 && savedHeight >= 400)
            {
                NextendoDashboardFrame.Width = savedWidth;
                NextendoDashboardFrame.Height = savedHeight;
                NextendoDashboardFrame.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
                NextendoDashboardFrame.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            }
            else
            {
                // Fit the default dashboard to the requested 3-column by 2-row
                // friend layout, including the navigation rail and outer margins.
                NextendoDashboardFrame.Width = 1100;
                NextendoDashboardFrame.Height = 780;
                NextendoDashboardFrame.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
                NextendoDashboardFrame.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            }

            NextendoDashboardOverlay.IsVisible = true;

            // Persist the initial friend-grid size on first use.
            if (savedWidth < 700 || savedHeight < 400)
            {
                Dispatcher.UIThread.Post(SaveInitialNextendoDashboardSize, DispatcherPriority.Loaded);
            }
        }

        public void OpenNextendoInvitesDashboard()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(OpenNextendoInvitesDashboard);
                return;
            }

            if (!IsNextendoDashboardOpen)
            {
                ToggleNextendoDashboard();
            }

            _activeNextendoDashboard?.ShowInvitesTab();
            if (_nextendoGameDashboardWindow?.IsVisible == true)
            {
                _nextendoGameDashboardWindow.Activate();
            }
            else
            {
                Activate();
            }
        }

        /// <summary>Routes the firmware's MyPage invite picker into the in-game dashboard.</summary>
        public void OpenGameInvitationDashboard(FriendInvitationRequest request, Action<bool> completed)
        {
            if (request == null || !ViewModel.IsGameRunning)
            {
                completed?.Invoke(false);
                return;
            }

            NextendoProfileView dashboard = _activeNextendoDashboard;
            if (dashboard == null)
            {
                dashboard = new NextendoProfileView(isGameRunning: true);
                _activeNextendoDashboard = dashboard;
                dashboard.CloseRequested += (_, _) => CloseNextendoDashboard();
                SuspendGameControllerInputForDashboard();
                OpenGameDashboardWindow(dashboard,
                    ConfigurationState.Instance.UI.WindowStartup.NextendoDashboardWidth.Value,
                    ConfigurationState.Instance.UI.WindowStartup.NextendoDashboardHeight.Value);
            }

            dashboard.BeginGameInvitation(request, sent =>
            {
                completed?.Invoke(sent);
                // Let the synchronous applet caller resume and return focus to the game.
                Dispatcher.UIThread.Post(CloseNextendoDashboard);
            });
        }

        private void OpenGameDashboardWindow(NextendoProfileView dashboard, int savedWidth, int savedHeight)
        {
            double overlayWidth = Math.Max(700, ClientSize.Width);
            double overlayHeight = Math.Max(400, ClientSize.Height);
            double maxWidth = Math.Max(700, overlayWidth - 48);
            double maxHeight = Math.Max(400, overlayHeight - 48);
            double width = savedWidth >= 700 ? Math.Min(savedWidth, maxWidth) : Math.Min(1100, maxWidth);
            double height = savedHeight >= 400 ? Math.Min(savedHeight, maxHeight) : Math.Min(780, maxHeight);

            Grid overlay = new()
            {
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#B8000000")),
            };
            Border frame = new()
            {
                Width = width,
                Height = height,
                MinWidth = 700,
                MinHeight = 400,
                MaxWidth = Math.Max(700, overlayWidth - 24),
                MaxHeight = Math.Max(400, overlayHeight - 24),
                Margin = new Thickness(24),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Background = Avalonia.Media.Brush.Parse("#FF1E2025"),
                BorderBrush = Avalonia.Media.Brush.Parse("#447F8C9B"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                BoxShadow = Avalonia.Media.BoxShadows.Parse("0 12 40 0 #80000000"),
            };

            Grid frameContent = new() { ClipToBounds = true };
            frameContent.Children.Add(dashboard);
            Border grip = new()
            {
                Width = 24,
                Height = 24,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
                Background = Avalonia.Media.Brush.Parse("#FF202329"),
                BorderBrush = Avalonia.Media.Brush.Parse("#887F8C9B"),
                BorderThickness = new Thickness(1, 1, 0, 0),
                Child = new TextBlock
                {
                    Text = "◢",
                    FontSize = 15,
                    Foreground = Avalonia.Media.Brush.Parse("#FF3EE8C8"),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                },
            };
            grip.PointerPressed += GameDashboardResize_PointerPressed;
            grip.PointerMoved += GameDashboardResize_PointerMoved;
            grip.PointerReleased += GameDashboardResize_PointerReleased;
            frameContent.Children.Add(grip);
            frame.Child = frameContent;
            overlay.Children.Add(frame);

            Avalonia.Controls.Window window = new()
            {
                Width = overlayWidth,
                Height = overlayHeight,
                MinWidth = 700,
                MinHeight = 400,
                CanResize = false,
                SystemDecorations = SystemDecorations.None,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Avalonia.Media.Brushes.Transparent,
                TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
                Content = overlay,
            };

            _nextendoGameDashboardWindow = window;
            _nextendoGameDashboardFrame = frame;
            _nextendoDashboardTakesFocus = true;
            window.Deactivated += (_, _) => _nextendoDashboardTakesFocus = false;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_nextendoGameDashboardWindow, window))
                {
                    _nextendoGameDashboardWindow = null;
                    _nextendoGameDashboardFrame = null;
                    _nextendoDashboardTakesFocus = false;
                    RestoreGameControllerInputAfterDashboard();
                }
            };

            try
            {
                window.Show(this);
                window.Activate();
                if (savedWidth < 700 || savedHeight < 400)
                {
                    Dispatcher.UIThread.Post(SaveInitialGameDashboardSize, DispatcherPriority.Loaded);
                }
            }
            catch
            {
                _nextendoGameDashboardWindow = null;
                _nextendoGameDashboardFrame = null;
                _nextendoDashboardTakesFocus = false;
                RestoreGameControllerInputAfterDashboard();
                throw;
            }
        }

        private void SaveInitialGameDashboardSize()
        {
            if (_nextendoGameDashboardWindow?.IsVisible != true || _nextendoGameDashboardFrame is null)
            {
                return;
            }

            SaveGameDashboardSize();
        }

        private void SaveGameDashboardSize()
        {
            if (_nextendoGameDashboardFrame is null || _nextendoGameDashboardFrame.Bounds.Width < 700 || _nextendoGameDashboardFrame.Bounds.Height < 400)
            {
                return;
            }

            ConfigurationState.Instance.UI.WindowStartup.NextendoDashboardWidth.Value = (int)Math.Round(_nextendoGameDashboardFrame.Bounds.Width);
            ConfigurationState.Instance.UI.WindowStartup.NextendoDashboardHeight.Value = (int)Math.Round(_nextendoGameDashboardFrame.Bounds.Height);
            MainWindowViewModel.SaveConfig();
        }

        private void GameDashboardResize_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (_nextendoGameDashboardFrame is null || sender is not Control grip || !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
            {
                return;
            }

            _gameDashboardResizeStart = e.GetPosition(_nextendoGameDashboardWindow);
            _gameDashboardResizeWidth = _nextendoGameDashboardFrame.Bounds.Width;
            _gameDashboardResizeHeight = _nextendoGameDashboardFrame.Bounds.Height;
            _isResizingGameDashboard = true;
            e.Pointer.Capture(grip);
            e.Handled = true;
        }

        private void GameDashboardResize_PointerMoved(object sender, PointerEventArgs e)
        {
            if (!_isResizingGameDashboard || _nextendoGameDashboardFrame is null || _nextendoGameDashboardWindow is null)
            {
                return;
            }

            Point current = e.GetPosition(_nextendoGameDashboardWindow);
            double maxWidth = Math.Clamp(_nextendoGameDashboardWindow.ClientSize.Width - 48, 700, 2000);
            double maxHeight = Math.Clamp(_nextendoGameDashboardWindow.ClientSize.Height - 48, 400, 1400);
            _nextendoGameDashboardFrame.Width = Math.Clamp(_gameDashboardResizeWidth + current.X - _gameDashboardResizeStart.X, 700, maxWidth);
            _nextendoGameDashboardFrame.Height = Math.Clamp(_gameDashboardResizeHeight + current.Y - _gameDashboardResizeStart.Y, 400, maxHeight);
            e.Handled = true;
        }

        private void GameDashboardResize_PointerReleased(object sender, PointerReleasedEventArgs e)
        {
            if (_isResizingGameDashboard)
            {
                _isResizingGameDashboard = false;
                SaveGameDashboardSize();
                e.Pointer.Capture(null);
                e.Handled = true;
            }
        }

        private void SaveInitialNextendoDashboardSize()
        {
            if (!NextendoDashboardOverlay.IsVisible ||
                NextendoDashboardFrame.Bounds.Width < 700 ||
                NextendoDashboardFrame.Bounds.Height < 400)
            {
                return;
            }

            ConfigurationState.Instance.UI.WindowStartup.NextendoDashboardWidth.Value = (int)Math.Round(NextendoDashboardFrame.Bounds.Width);
            ConfigurationState.Instance.UI.WindowStartup.NextendoDashboardHeight.Value = (int)Math.Round(NextendoDashboardFrame.Bounds.Height);
            MainWindowViewModel.SaveConfig();
        }

        private void NextendoDashboardResize_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (sender is not Control grip || !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
            {
                return;
            }

            _nextendoDashboardResizeStart = e.GetPosition(this);
            _nextendoDashboardResizeWidth = NextendoDashboardFrame.Bounds.Width;
            _nextendoDashboardResizeHeight = NextendoDashboardFrame.Bounds.Height;
            _isResizingNextendoDashboard = true;
            NextendoDashboardFrame.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            NextendoDashboardFrame.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            NextendoDashboardFrame.Width = _nextendoDashboardResizeWidth;
            NextendoDashboardFrame.Height = _nextendoDashboardResizeHeight;
            e.Pointer.Capture(grip);
            e.Handled = true;
        }

        private void NextendoDashboardResize_PointerMoved(object sender, PointerEventArgs e)
        {
            if (!_isResizingNextendoDashboard)
            {
                return;
            }

            Point current = e.GetPosition(this);
            double maxWidth = Math.Clamp(NextendoDashboardOverlay.Bounds.Width - 48, 700, 2000);
            double maxHeight = Math.Clamp(NextendoDashboardOverlay.Bounds.Height - 48, 400, 1400);
            NextendoDashboardFrame.Width = Math.Clamp(_nextendoDashboardResizeWidth + current.X - _nextendoDashboardResizeStart.X, 700, maxWidth);
            NextendoDashboardFrame.Height = Math.Clamp(_nextendoDashboardResizeHeight + current.Y - _nextendoDashboardResizeStart.Y, 400, maxHeight);
            e.Handled = true;
        }

        private void NextendoDashboardOverlay_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (double.IsNaN(NextendoDashboardFrame.Width) || double.IsNaN(NextendoDashboardFrame.Height))
            {
                return;
            }

            double maxWidth = Math.Clamp(e.NewSize.Width - 48, 700, 2000);
            double maxHeight = Math.Clamp(e.NewSize.Height - 48, 400, 1400);
            NextendoDashboardFrame.Width = Math.Min(NextendoDashboardFrame.Width, maxWidth);
            NextendoDashboardFrame.Height = Math.Min(NextendoDashboardFrame.Height, maxHeight);
        }

        private void NextendoDashboardResize_PointerReleased(object sender, PointerReleasedEventArgs e)
        {
            if (_isResizingNextendoDashboard)
            {
                _isResizingNextendoDashboard = false;
                ConfigurationState.Instance.UI.WindowStartup.NextendoDashboardWidth.Value = (int)Math.Round(NextendoDashboardFrame.Bounds.Width);
                ConfigurationState.Instance.UI.WindowStartup.NextendoDashboardHeight.Value = (int)Math.Round(NextendoDashboardFrame.Bounds.Height);
                MainWindowViewModel.SaveConfig();
                e.Pointer.Capture(null);
                e.Handled = true;
            }
        }

        private void CloseNextendoDashboard()
        {
            _activeNextendoDashboard?.CancelPendingGameInvitation();
            _activeNextendoDashboard = null;
            Avalonia.Controls.Window gameDashboard = _nextendoGameDashboardWindow;
            _nextendoGameDashboardWindow = null;
            _nextendoGameDashboardFrame = null;
            _nextendoDashboardTakesFocus = false;
            gameDashboard?.Close();
            NextendoDashboardOverlay.IsVisible = false;
            NextendoDashboardContent.Content = null;
            RestoreGameControllerInputAfterDashboard();
        }

        private void SuspendGameControllerInputForDashboard()
        {
            if (!ViewModel.IsGameRunning || ViewModel.AppHost is not { } host || host.NpadManager.InputUpdatesBlocked)
            {
                return;
            }

            host.NpadManager.BlockInputUpdates();
            _dashboardBlockedGameInput = true;
        }

        private void RestoreGameControllerInputAfterDashboard()
        {
            if (!_dashboardBlockedGameInput)
            {
                return;
            }

            try
            {
                if (ViewModel.AppHost is { } host && host.NpadManager.InputUpdatesBlocked)
                {
                    host.NpadManager.UnblockInputUpdates();
                }
            }
            catch (ObjectDisposedException)
            {
                // The game may have ended while the dashboard was open.
            }
            finally
            {
                _dashboardBlockedGameInput = false;
            }
        }

        public static bool ShowKeyErrorOnLoad { get; set; }
        public ApplicationLibrary ApplicationLibrary { get; set; }

        // Correctly size window when 'TitleBar' is enabled (Nov. 14, 2024)
        public readonly double TitleBarHeight;

        public readonly double StatusBarHeight;
        public readonly double MenuBarHeight;

        public MainWindow() : base(useCustomTitleBar: true)
        {
            DataContext = ViewModel = new MainWindowViewModel
            {
                Window = this
            };

            InitializeComponent();
            Load();

            UiHandler = new AvaHostUIHandler(this);

            ViewModel.Title = RyujinxApp.FormatTitle();

            // NOTE: Height of MenuBar and StatusBar is not usable here, since it would still be 0 at this point.
            StatusBarHeight = StatusBarView.StatusBar.MinHeight;
            MenuBarHeight = MenuBar.MinHeight;

            TitleBar.Height = MenuBarHeight;

            // Correctly size window when 'TitleBar' is enabled (Nov. 14, 2024)
            TitleBarHeight = (ConfigurationState.Instance.ShowOldUI ? TitleBar.Height : 0);

            ApplicationList.DataContext = DataContext;
            ApplicationGrid.DataContext = DataContext;
            ApplicationCarousel.DataContext = DataContext;

            SetWindowSizePosition();

            if (Program.PreviewerDetached)
            {
                AvaloniaKeyboardDriver keyboardDriver = new(this, KeyboardInputMode.Semantic);
                keyboardDriver.KeyPressed += PhysicalKeyLabelHelper.ObserveKeyPress;
                InputManager = new InputManager(keyboardDriver, new SDL3GamepadDriver());

                _ = this.GetObservable(IsActiveProperty).Subscribe(it => ViewModel.IsActive = it);
                this.ScalingChanged += OnScalingChanged;
            }
        }

        /// <summary>
        /// Event handler for detecting OS theme change when using "Follow OS theme" option
        /// </summary>
        private static void OnPlatformColorValuesChanged(object sender, PlatformColorValues e)
        {
            if (Application.Current is RyujinxApp app)
                app.ApplyConfiguredTheme(ConfigurationState.Instance.UI.BaseStyle);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (PlatformSettings != null)
            {
                PlatformSettings.ColorValuesChanged -= OnPlatformColorValuesChanged;
            }
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);

            NotificationHelper.SetNotificationManager(this);

            // [Nextendo] Wire the Switch-style in-game toast overlay (a separate transparent, topmost,
            // click-through window — the game render is a native surface that paints over Avalonia).
            NextendoNotificationOverlayWindow.Attach(this);

            // [Nextendo] L'historique ne partait qu'a la fin d'un jeu ou a la fermeture de la
            // fenetre : un emulateur tue ou plante n'envoyait donc rien, et une longue session
            // n'arrivait sur le compte qu'apres coup. Mesure du 2026-08-23 : un compte ayant joue
            // plusieurs sessions avait encore, cote serveur, un historique vieux de trois jours.
            // La poussee periodique n'envoie que ce qui a bouge, sans les icones deja connues.
            Ryujinx.Ava.Common.NextendoHistorySync.DemarrerPousseePeriodique();

            Executor.ExecuteBackgroundAsync(async () =>
            {
                // [Nextendo] Warm the remote gate at startup, then ENFORCE the mandatory update:
                // if this build is older than the required version, the emulator cannot be used
                // until it's updated — a blocking popup points to the download (GitHub release)
                // and quits. Server unreachable => Evaluate() is Unreachable (not UpdateRequired),
                // so offline use still works; only a CONFIRMED outdated version hard-blocks.
                await Ryujinx.Ava.Common.NextendoBeta.RefreshAsync();
                if (Ryujinx.Ava.Common.NextendoBeta.Evaluate() == Ryujinx.Ava.Common.NextendoBeta.BlockReason.UpdateRequired)
                {
                    await Dispatcher.UIThread.InvokeAsync(Ryujinx.Ava.Common.NextendoUpdate.ShowMandatoryUpdateAsync);
                    return;
                }

                // [Nextendo] Heartbeat de session : tant que l'app tourne avec un compte lié, on
                // rafraîchit la session "connecté" du compte pour qu'elle apparaisse sur le site.
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    while (true)
                    {
                        await Ryujinx.Ava.Common.NextendoApi.TouchSessionAsync();
                        await System.Threading.Tasks.Task.Delay(System.TimeSpan.FromMinutes(2));
                    }
                });

                // [Nextendo] Heal a link that predates the profile binding (or a login path
                // that skipped it): bind the linked account to the active local profile so the
                // pseudo / avatar / Mii + "N" badge follow the account and the in-game name
                // matches the identity. Runs on the UI thread; no-op when nothing needs healing.
                await Dispatcher.UIThread.InvokeAsync(
                    Ryujinx.Ava.UI.Views.Main.MainMenuBarView.HealNextendoProfileAsync);

                await ShowIntelMacWarningAsync();
                if (CommandLineState.FirmwareToInstallPathArg.TryGet(out FilePath fwPath))
                {
                    if (fwPath is { ExistsAsFile: true, Extension: "xci" or "zip" } || fwPath.ExistsAsDirectory)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                            ViewModel.HandleFirmwareInstallation(fwPath));
                        CommandLineState.FirmwareToInstallPathArg = default;
                    }
                    else
                        Logger.Notice.Print(LogClass.UI, "Invalid firmware type provided. Path must be a directory, or a .zip or .xci file.");
                }
            });
        }

        private void OnScalingChanged(object sender, EventArgs e)
        {
            Program.DesktopScaleFactor = this.RenderScaling;
        }

        private void ApplicationLibrary_ApplicationCountUpdated(object sender, ApplicationCountUpdatedEventArgs e)
        {
            LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.StatusBarGamesLoaded, e.NumAppsLoaded, e.NumAppsFound);

            Dispatcher.UIThread.Post(() =>
            {
                ViewModel.StatusBarProgressValue = e.NumAppsLoaded;
                ViewModel.StatusBarProgressMaximum = e.NumAppsFound;

                if (e.NumAppsFound == 0)
                {
                    StatusBarView.LoadProgressBar.IsVisible = false;
                }

                if (e.NumAppsLoaded == e.NumAppsFound)
                {
                    StatusBarView.LoadProgressBar.IsVisible = false;
                }
            });
        }

        private void ApplicationLibrary_LdnGameDataReceived(LdnGameDataReceivedEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                ViewModel.LdnModels = e.LdnData;
                ViewModel.UsableLdnData.Clear();
                foreach (ApplicationData application in ViewModel.Applications.Where(it => it.HasControlHolder))
                {
                    ViewModel.UsableLdnData[application.IdString] = LdnGameModel.GetArrayForApp(e.LdnData, ref application.ControlHolder.Value);

                    UpdateApplicationWithLdnData(application);
                }

                ViewModel.RefreshView();
            });
        }

        private void UpdateApplicationWithLdnData(ApplicationData application)
        {
            if (application.HasControlHolder && ViewModel.UsableLdnData.TryGetValue(application.IdString, out LdnGameModel.Array ldnGameDatas))
            {
                application.PlayerCount = ldnGameDatas.PlayerCount;
                application.GameCount = ldnGameDatas.GameCount;
            }
            else
            {
                application.PlayerCount = 0;
                application.GameCount = 0;
            }
        }

        public async void Application_Opened(object sender, ApplicationOpenedEventArgs args)
        {
            if (args.Application != null)
            {
                ViewModel.SelectedIcon = args.Application.Icon;

                await ViewModel.LoadApplication(args.Application);
            }

            args.Handled = true;
        }

        internal static void DeferLoadApplication(string launchPathArg, string launchApplicationId, bool startFullscreenArg)
        {
            _deferLoad = true;
            _launchPath = launchPathArg;
            _launchApplicationId = launchApplicationId;
            _startFullscreen = startFullscreenArg;
        }

        public void SwitchToGameControl(bool startFullscreen = false)
        {
            ViewModel.ShowLoadProgress = false;
            ViewModel.ShowContent = true;
            ViewModel.IsLoadingIndeterminate = false;

            if (startFullscreen && ViewModel.WindowState is not WindowState.FullScreen)
            {
                ViewModel.ToggleFullscreen();
            }
        }

        public void ShowLoading(bool startFullscreen = false)
        {
            ViewModel.ShowContent = false;
            ViewModel.ShowLoadProgress = true;
            ViewModel.IsLoadingIndeterminate = true;

            if (startFullscreen && ViewModel.WindowState is not WindowState.FullScreen)
            {
                ViewModel.ToggleFullscreen();
            }
        }

        private void Initialize()
        {
            _userChannelPersistence = new UserChannelPersistence();
            VirtualFileSystem = VirtualFileSystem.CreateInstance();
            LibHacHorizonManager = new LibHacHorizonManager();
            ContentManager = new ContentManager(VirtualFileSystem);

            LibHacHorizonManager.InitializeFsServer(VirtualFileSystem);
            LibHacHorizonManager.InitializeArpServer();
            LibHacHorizonManager.InitializeBcatServer();
            LibHacHorizonManager.InitializeSystemClients();

            ApplicationLibrary = new ApplicationLibrary(VirtualFileSystem, ConfigurationState.Instance.System.IntegrityCheckLevel)
            {
                DesiredLanguage = ConfigurationState.Instance.System.Language,
            };

            // Save data created before we supported extra data in directory save data will not work properly if
            // given empty extra data. Luckily some of that extra data can be created using the data from the
            // save data indexer, which should be enough to check access permissions for user saves.
            // Every single save data's extra data will be checked and fixed if needed each time the emulator is opened.
            // Consider removing this at some point in the future when we don't need to worry about old saves.
            VirtualFileSystem.FixExtraData(LibHacHorizonManager.RyujinxClient);

            AccountManager = new AccountManager(LibHacHorizonManager.RyujinxClient, CommandLineState.Profile);

            VirtualFileSystem.ReloadKeySet();

            ApplicationHelper.Initialize(VirtualFileSystem, AccountManager, LibHacHorizonManager.RyujinxClient);
        }

        [SupportedOSPlatform("linux")]
        private static async Task ShowVmMaxMapCountWarning()
        {
            LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.LinuxVmMaxMapCountWarningTextSecondary,
                LinuxHelper.VmMaxMapCount, LinuxHelper.RecommendedVmMaxMapCount);

            await ContentDialogHelper.CreateWarningDialog(
                LocaleManager.Instance[LocaleKeys.LinuxVmMaxMapCountWarningTextPrimary],
                LocaleManager.Instance[LocaleKeys.LinuxVmMaxMapCountWarningTextSecondary]
            );
        }

        [SupportedOSPlatform("linux")]
        private static async Task ShowVmMaxMapCountDialog()
        {
            LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.LinuxVmMaxMapCountDialogTextPrimary,
                LinuxHelper.RecommendedVmMaxMapCount);

            UserResult response = await ContentDialogHelper.ShowTextDialog(
                RyujinxApp.FormatTitle(LocaleKeys.LinuxVmMaxMapCountDialogTitle, false),
                LocaleManager.Instance[LocaleKeys.LinuxVmMaxMapCountDialogTextPrimary],
                LocaleManager.Instance[LocaleKeys.LinuxVmMaxMapCountDialogTextSecondary],
                LocaleManager.Instance[LocaleKeys.LinuxVmMaxMapCountDialogButtonUntilRestart],
                LocaleManager.Instance[LocaleKeys.LinuxVmMaxMapCountDialogButtonPersistent],
                LocaleManager.Instance[LocaleKeys.InputDialogNo],
                (int)Symbol.Help
            );

            int rc;

            switch (response)
            {
                case UserResult.Ok:
                    rc = LinuxHelper.RunPkExec($"echo {LinuxHelper.RecommendedVmMaxMapCount} > {LinuxHelper.VmMaxMapCountPath}");
                    if (rc == 0)
                    {
                        Logger.Info?.Print(LogClass.Application, $"vm.max_map_count set to {LinuxHelper.VmMaxMapCount} until the next restart.");
                    }
                    else
                    {
                        Logger.Error?.Print(LogClass.Application, $"Unable to change vm.max_map_count. Process exited with code: {rc}");
                    }

                    break;
                case UserResult.No:
                    rc = LinuxHelper.RunPkExec($"echo \"vm.max_map_count = {LinuxHelper.RecommendedVmMaxMapCount}\" > {LinuxHelper.SysCtlConfigPath} && sysctl -p {LinuxHelper.SysCtlConfigPath}");
                    if (rc == 0)
                    {
                        Logger.Info?.Print(LogClass.Application, $"vm.max_map_count set to {LinuxHelper.VmMaxMapCount}. Written to config: {LinuxHelper.SysCtlConfigPath}");
                    }
                    else
                    {
                        Logger.Error?.Print(LogClass.Application, $"Unable to write new value for vm.max_map_count to config. Process exited with code: {rc}");
                    }

                    break;
            }
        }

        private async Task CheckLaunchState()
        {
            // [Nextendo] First-launch quick-start (fresh install) and the "what's new" popup (first
            // launch after an update) both need the main window loaded before a modal can open, so
            // handle them together. A fresh install shows the wizard only; an existing install that
            // just updated shows the condensed patch notes once.
            bool firstRun = NextendoFirstRunWindow.IsFirstRun();
            bool showPatchNotes = !firstRun && Ryujinx.Ava.Common.NextendoPatchNotes.ShouldShow();

            if (firstRun || showPatchNotes)
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        // ShowDialog needs this owner window fully loaded; CheckLaunchState can run
                        // before that, so wait for Loaded first (otherwise the modal throws and the
                        // dialog silently never appears).
                        if (!IsLoaded)
                        {
                            System.Threading.Tasks.TaskCompletionSource ready = new();
                            void OnLoaded(object s, Avalonia.Interactivity.RoutedEventArgs e)
                            {
                                Loaded -= OnLoaded;
                                ready.TrySetResult();
                            }

                            Loaded += OnLoaded;
                            if (IsLoaded)
                            {
                                Loaded -= OnLoaded;
                                ready.TrySetResult();
                            }

                            await ready.Task;
                        }

                        if (firstRun)
                        {
                            await new NextendoFirstRunWindow().ShowDialog(this);

                            // Fresh install: seed the patch-note flag so the wizard isn't immediately
                            // followed by a "what's new" for a version this install never had before.
                            Ryujinx.Ava.Common.NextendoPatchNotes.MarkShown();
                        }
                        else
                        {
                            await Ryujinx.Ava.Common.NextendoPatchNotes.ShowAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning?.Print(LogClass.UI, $"[Nextendo] first-run / patch-note popup failed: {ex.Message}");
                    }
                });
            }

            if (OperatingSystem.IsLinux() && LinuxHelper.VmMaxMapCount < LinuxHelper.RecommendedVmMaxMapCount)
            {
                Logger.Warning?.Print(LogClass.Application, $"The value of vm.max_map_count is lower than {LinuxHelper.RecommendedVmMaxMapCount}. ({LinuxHelper.VmMaxMapCount})");

                if (LinuxHelper.PkExecPath is not null)
                {
                    await Dispatcher.UIThread.InvokeAsync(ShowVmMaxMapCountDialog);
                }
                else
                {
                    await Dispatcher.UIThread.InvokeAsync(ShowVmMaxMapCountWarning);
                }
            }

            if (!ShowKeyErrorOnLoad)
            {
                if (_deferLoad)
                {
                    _deferLoad = false;

                    if (ApplicationLibrary.TryGetApplicationsFromFile(_launchPath, out List<ApplicationData> applications))
                    {
                        ApplicationData applicationData;

                        if (_launchApplicationId != null)
                        {
                            applicationData = applications.FirstOrDefault(application => application.IdString == _launchApplicationId);

                            if (applicationData != null)
                            {
                                ViewModel.SelectedApplication = applicationData;
                                await ViewModel.LoadApplication(applicationData, _startFullscreen);
                            }
                            else
                            {
                                Logger.Error?.Print(LogClass.Application, $"Couldn't find requested application id '{_launchApplicationId}' in '{_launchPath}'.");
                                await Dispatcher.UIThread.InvokeAsync(async () => await UserErrorDialog.ShowUserErrorDialog(UserError.ApplicationNotFound));
                            }
                        }
                        else
                        {
                            applicationData = applications[0];
                            ViewModel.SelectedApplication = applicationData;
                            await ViewModel.LoadApplication(applicationData, _startFullscreen);
                        }
                    }
                    else
                    {
                        Logger.Error?.Print(LogClass.Application, $"Couldn't find any application in '{_launchPath}'.");
                        await Dispatcher.UIThread.InvokeAsync(async () => await UserErrorDialog.ShowUserErrorDialog(UserError.ApplicationNotFound));
                    }
                }
            }
            else
            {
                ShowKeyErrorOnLoad = false;

                await Dispatcher.UIThread.InvokeAsync(async () => await UserErrorDialog.ShowUserErrorDialog(UserError.NoKeys));
            }

            if (!Updater.CanUpdate() || CommandLineState.HideAvailableUpdates)
                return;

            switch (ConfigurationState.Instance.UpdateCheckerType.Value)
            {
                // [Nextendo] Both paths go to the Nextendo release page instead of the Ryujinx
                // update server, which has no idea this fork exists.
                case UpdaterType.PromptAtStartup:
                    await Updater.BeginNextendoUpdateAsync()
                        .Catch(task => Logger.Error?.Print(LogClass.Application, $"Updater Error: {task.Exception}"));
                    break;
                case UpdaterType.CheckInBackground:
                    bool available = await Updater.NextendoUpdateAvailableAsync();

                    Dispatcher.UIThread.Post(() => RyujinxApp.MainWindow.ViewModel.UpdateAvailable = available);

                    break;
            }
        }

        private void Load()
        {
            StatusBarView.VolumeStatus.Click += VolumeStatus_CheckedChanged;

            ApplicationGrid.DataContext = ApplicationList.DataContext = ApplicationCarousel.DataContext = ViewModel;

            ApplicationGrid.ApplicationOpened += Application_Opened;
            ApplicationList.ApplicationOpened += Application_Opened;
            ApplicationCarousel.ApplicationOpened += Application_Opened;
        }

        private void SetWindowSizePosition()
        {
            if (!ConfigurationState.Instance.RememberWindowState)
            {
                // Correctly size window when 'TitleBar' is enabled (Nov. 14, 2024)
                ViewModel.WindowHeight = (720 + StatusBarHeight + MenuBarHeight + TitleBarHeight) * Program.WindowScaleFactor;
                ViewModel.WindowWidth = 1280 * Program.WindowScaleFactor;

                WindowState = WindowState.Normal;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;

                return;
            }

            PixelPoint savedPoint = new(ConfigurationState.Instance.UI.WindowStartup.WindowPositionX,
                                        ConfigurationState.Instance.UI.WindowStartup.WindowPositionY);

            ViewModel.WindowHeight = ConfigurationState.Instance.UI.WindowStartup.WindowSizeHeight * Program.WindowScaleFactor;
            ViewModel.WindowWidth = ConfigurationState.Instance.UI.WindowStartup.WindowSizeWidth * Program.WindowScaleFactor;

            ViewModel.WindowState = ConfigurationState.Instance.UI.WindowStartup.WindowMaximized.Value ? WindowState.Maximized : WindowState.Normal;

            if (Screens.All.Any(screen => screen.Bounds.Contains(savedPoint)))
            {
                Position = savedPoint;
            }
            else
            {
                Logger.Warning?.Print(LogClass.Application, "Failed to find valid start-up coordinates. Defaulting to primary monitor center.");
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        private void SaveWindowSizePosition()
        {
            ConfigurationState.Instance.UI.WindowStartup.WindowMaximized.Value = WindowState == WindowState.Maximized;

            // Only save rectangle properties if the window is not in a maximized state.
            if (WindowState != WindowState.Maximized)
            {
                // Since scaling is being applied to the loaded settings from disk (see SetWindowSizePosition() above), scaling should be removed from width/height before saving out to disk
                // as well - otherwise anyone not using a 1.0 scale factor their window will increase in size with every subsequent launch of the program when scaling is applied (Nov. 14, 2024)
                ConfigurationState.Instance.UI.WindowStartup.WindowSizeHeight.Value = (int)((Height - _windowStartupHeightDelta) / Program.WindowScaleFactor);
                ConfigurationState.Instance.UI.WindowStartup.WindowSizeWidth.Value = (int)((Width - _windowStartupWidthDelta) / Program.WindowScaleFactor);

                ConfigurationState.Instance.UI.WindowStartup.WindowPositionX.Value = Position.X;
                ConfigurationState.Instance.UI.WindowStartup.WindowPositionY.Value = Position.Y;
            }

            MainWindowViewModel.SaveConfig();
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            Initialize();

            _windowStartupWidthDelta = Math.Max(0, Width - ViewModel.WindowWidth);
            _windowStartupHeightDelta = Math.Max(0, Height - ViewModel.WindowHeight);

            PlatformSettings!.ColorValuesChanged += OnPlatformColorValuesChanged;

            ViewModel.Initialize(
                ContentManager,
                StorageProvider,
                ApplicationLibrary,
                VirtualFileSystem,
                AccountManager,
                InputManager,
                _userChannelPersistence,
                LibHacHorizonManager,
                UiHandler,
                ShowLoading,
                SwitchToGameControl,
                SetMainContent,
                this);

            ApplicationLibrary.ApplicationCountUpdated += ApplicationLibrary_ApplicationCountUpdated;
            _appLibraryAppsSubscription?.Dispose();
            _appLibraryAppsSubscription = ApplicationLibrary.Applications
                    .Connect()
                    .ObserveOn(SynchronizationContext.Current!)
                    .Bind(ViewModel.Applications)
                    .OnItemAdded(UpdateApplicationWithLdnData)
                    .Subscribe();
            ApplicationLibrary.LdnGameDataReceived += ApplicationLibrary_LdnGameDataReceived;

            ConfigurationState.Instance.Multiplayer.Mode.Event += (sender, evt) =>
            {
                _ = Task.Run(ViewModel.ApplicationLibrary.RefreshLdn);
            };

            ConfigurationState.Instance.Multiplayer.LdnServer.Event += (sender, evt) =>
            {
                _ = Task.Run(ViewModel.ApplicationLibrary.RefreshLdn);
            };
            _ = Task.Run(ViewModel.ApplicationLibrary.RefreshLdn);

            ViewModel.RefreshFirmwareStatus();

            // Load applications if no application was requested by the command line
            if (!_deferLoad)
            {
                LoadApplications();
            }

            _ = CheckLaunchState();
        }

        private void SetMainContent(Control content = null)
        {
            content ??= GameLibrary;

            if (MainContent.Content != content)
            {
                // Load applications while switching to the GameLibrary if we haven't done that yet
                if (!_applicationsLoadedOnce && content == GameLibrary)
                {
                    LoadApplications();
                }

                MainContent.Content = content;
            }
        }

        public static void UpdateGraphicsConfig()
        {
#pragma warning disable IDE0055 // Disable formatting
            GraphicsConfig.ResScale                   = ConfigurationState.Instance.Graphics.ResScale == -1 
                ? ConfigurationState.Instance.Graphics.ResScaleCustom 
                : ConfigurationState.Instance.Graphics.ResScale;
            GraphicsConfig.MaxAnisotropy              = ConfigurationState.Instance.Graphics.MaxAnisotropy;
            GraphicsConfig.ShadersDumpPath            = ConfigurationState.Instance.Graphics.ShadersDumpPath;
            GraphicsConfig.EnableShaderCache          = ConfigurationState.Instance.Graphics.EnableShaderCache;
            GraphicsConfig.EnableTextureRecompression = ConfigurationState.Instance.Graphics.EnableTextureRecompression;
            GraphicsConfig.EnableMacroHLE             = ConfigurationState.Instance.Graphics.EnableMacroHLE;
#pragma warning restore IDE0055
        }

        private void VolumeStatus_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsGameRunning && sender is ToggleSplitButton volumeSplitButton)
            {
                if (!volumeSplitButton.IsChecked)
                {
                    ViewModel.AppHost.Device.SetVolume(ViewModel.VolumeBeforeMute);
                }
                else
                {
                    ViewModel.VolumeBeforeMute = ViewModel.AppHost.Device.GetVolume();
                    ViewModel.AppHost.Device.SetVolume(0);
                }

                ViewModel.Volume = ViewModel.AppHost.Device.GetVolume();
            }
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (!ViewModel.IsClosing && ViewModel.AppHost != null && ConfigurationState.Instance.ShowConfirmExit)
            {
                e.Cancel = true;

                ConfirmExit();

                return;
            }

            ViewModel.IsClosing = true;

            if (ViewModel.AppHost != null)
            {
                ViewModel.AppHost.AppExit -= ViewModel.AppHost_AppExit;
                ViewModel.AppHost.AppExit += (_, _) =>
                {
                    ViewModel.AppHost = null;

                    Dispatcher.UIThread.Post(async () =>
                    {
                        MainContent = null;

                        // [Nextendo] Closing the window swaps out the normal AppHost_AppExit
                        // (which uploads the save AND pushes play history); do both here so they
                        // sync on window-close (Alt+F4) too, not only via "Actions > Stop emulation".
                        // Awaited before Close() so they finish before the process exits.
                        await ViewModel.FlushNextendoSaveAsync();
                        await Ryujinx.Ava.Common.NextendoHistorySync.PushAsync("window-close");
                        Ryujinx.Ava.Common.NextendoHistorySync.ArreterPousseePeriodique();

                        Close();
                    });
                };
                ViewModel.AppHost?.Stop();

                e.Cancel = true;

                return;
            }

            if (ConfigurationState.Instance.RememberWindowState)
            {
                SaveWindowSizePosition();
            }

            ApplicationLibrary.CancelLoading();
            InputManager.Dispose();
            _appLibraryAppsSubscription?.Dispose();
            Program.Exit();

            base.OnClosing(e);
        }

        private void ConfirmExit()
        {
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                ViewModel.IsClosing = await ContentDialogHelper.CreateExitDialog();

                if (ViewModel.IsClosing)
                {
                    Close();
                }
            });
        }

        public void LoadApplications()
        {
            _applicationsLoadedOnce = true;

            StatusBarView.LoadProgressBar.IsVisible = true;
            ViewModel.StatusBarProgressMaximum = 0;
            ViewModel.StatusBarProgressValue = 0;

            LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.StatusBarGamesLoaded, 0, 0);

            ReloadGameList();
        }

        public void ToggleFileType(string fileType)
        {
            switch (fileType)
            {
                case "NSP":
                    ConfigurationState.Instance.UI.ShownFileTypes.NSP.Toggle();
                    break;
                case "PFS0":
                    ConfigurationState.Instance.UI.ShownFileTypes.PFS0.Toggle();
                    break;
                case "XCI":
                    ConfigurationState.Instance.UI.ShownFileTypes.XCI.Toggle();
                    break;
                case "NCA":
                    ConfigurationState.Instance.UI.ShownFileTypes.NCA.Toggle();
                    break;
                case "NRO":
                    ConfigurationState.Instance.UI.ShownFileTypes.NRO.Toggle();
                    break;
                case "NSO":
                    ConfigurationState.Instance.UI.ShownFileTypes.NSO.Toggle();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(fileType);
            }

            ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.ConfigurationPath);
            LoadApplications();
        }

        private void ReloadGameList()
        {
            if (_isLoading)
            {
                return;
            }

            _isLoading = true;

            Thread applicationLibraryThread = new(() =>
            {
                ApplicationLibrary.DesiredLanguage = ConfigurationState.Instance.System.Language;

                ApplicationLibrary.LoadApplications(ConfigurationState.Instance.UI.GameDirs);

                List<string> autoloadDirs = ConfigurationState.Instance.UI.AutoloadDirs.Value;
                autoloadDirs.ForEach(dir => Logger.Info?.Print(LogClass.Application, $"Auto loading DLC & updates from: {dir}"));
                if (autoloadDirs.Count > 0)
                {
                    int updatesLoaded = ApplicationLibrary.AutoLoadTitleUpdates(autoloadDirs, out int updatesRemoved);
                    int dlcLoaded = ApplicationLibrary.AutoLoadDownloadableContents(autoloadDirs, out int dlcRemoved);

                    ShowNewContentAddedDialog(dlcLoaded, dlcRemoved, updatesLoaded, updatesRemoved);
                }

                Executor.ExecuteBackgroundAsync(ApplicationLibrary.RefreshTotalTimePlayedAsync);

                _isLoading = false;
                _ = NextendoOnlineCounts.RefreshAsync();
            })
            {
                Name = "GUI.ApplicationLibraryThread",
                IsBackground = true,
            };
            applicationLibraryThread.Start();
        }

        private static void ShowNewContentAddedDialog(int numDlcAdded, int numDlcRemoved, int numUpdatesAdded, int numUpdatesRemoved)
        {
            string[] messages =
            [
                numDlcRemoved > 0 ? string.Format(LocaleManager.Instance[LocaleKeys.Dialog_ContentLoading_DLCRemovedMessage], numDlcRemoved): null,
                numDlcAdded > 0 ? string.Format(LocaleManager.Instance[LocaleKeys.Dialog_ContentLoading_DLCAddedMessage], numDlcAdded): null,
                numUpdatesRemoved > 0 ? string.Format(LocaleManager.Instance[LocaleKeys.Dialog_ContentLoading_UpdatesRemovedMessage], numUpdatesRemoved): null,
                numUpdatesAdded > 0 ? string.Format(LocaleManager.Instance[LocaleKeys.Dialog_ContentLoading_UpdatesAddedMessage], numUpdatesAdded) : null
            ];

            string msg = String.Join("\r\n", messages);

            if (String.IsNullOrWhiteSpace(msg))
                return;

            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await ContentDialogHelper.ShowTextDialog(
                    LocaleManager.Instance[LocaleKeys.DialogConfirmationTitle],
                    msg,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    LocaleManager.Instance[LocaleKeys.InputDialogOk],
                    (int)Symbol.Checkmark);
            });
        }

        private static bool _intelMacWarningShown = !RunningPlatform.IsIntelMac;

        public static async Task ShowIntelMacWarningAsync()
        {
            if (_intelMacWarningShown)
                return;

            await Dispatcher.UIThread.InvokeAsync(async () => await ContentDialogHelper.CreateWarningDialog(
                "Intel Mac Warning",
                "Intel Macs are not supported and will not work properly.\nIf you continue, do not come to our Discord asking for support;\nand do not report bugs on the GitHub. They will be closed."));

            _intelMacWarningShown = true;
        }

        private void AppWindow_OnGotFocus(object sender, GotFocusEventArgs e)
        {
            if (ViewModel.AppHost is null)
                return;

            if (!_focusLoss.Active)
                return;

            switch (_focusLoss.Type)
            {
                case FocusLostType.BlockInput:
                    {
                        if (!ViewModel.AppHost.NpadManager.InputUpdatesBlocked)
                        {
                            _focusLoss = default;
                            return;
                        }

                        ViewModel.AppHost.NpadManager.UnblockInputUpdates();
                        _focusLoss = default;
                        break;
                    }
                case FocusLostType.MuteAudio:
                    {
                        if (!ViewModel.AppHost.Device.IsAudioMuted())
                        {
                            _focusLoss = default;
                            return;
                        }

                        ViewModel.AppHost.Device.SetVolume(ViewModel.VolumeBeforeMute);

                        _focusLoss = default;
                        break;
                    }
                case FocusLostType.BlockInputAndMuteAudio:
                    {
                        if (!ViewModel.AppHost.Device.IsAudioMuted())
                            goto case FocusLostType.BlockInput;

                        ViewModel.AppHost.Device.SetVolume(ViewModel.VolumeBeforeMute);
                        ViewModel.AppHost.NpadManager.UnblockInputUpdates();

                        _focusLoss = default;
                        break;
                    }
                case FocusLostType.PauseEmulation:
                    {
                        if (!ViewModel.AppHost.Device.System.IsPaused)
                        {
                            _focusLoss = default;
                            return;
                        }

                        ViewModel.AppHost.Resume();

                        _focusLoss = default;
                        break;
                    }
            }
        }

        private (FocusLostType Type, bool Active) _focusLoss;

        private void AppWindow_OnLostFocus(object sender, RoutedEventArgs e)
        {
            // Showing the owned dashboard window moves keyboard focus away from this host,
            // but it must not trigger Ryujinx's configured focus-loss pause/mute action.
            if (_nextendoDashboardTakesFocus)
            {
                return;
            }

            if (ConfigurationState.Instance.FocusLostActionType.Value is FocusLostType.DoNothing)
                return;

            if (ViewModel.AppHost is null)
                return;

            switch (ConfigurationState.Instance.FocusLostActionType.Value)
            {
                case FocusLostType.BlockInput:
                    {
                        if (ViewModel.AppHost.NpadManager.InputUpdatesBlocked)
                            return;

                        ViewModel.AppHost.NpadManager.BlockInputUpdates();
                        _focusLoss = (FocusLostType.BlockInput, ViewModel.AppHost.NpadManager.InputUpdatesBlocked);
                        break;
                    }
                case FocusLostType.MuteAudio:
                    {
                        if (ViewModel.AppHost.Device.GetVolume() is 0)
                            return;

                        ViewModel.VolumeBeforeMute = ViewModel.AppHost.Device.GetVolume();
                        ViewModel.AppHost.Device.SetVolume(0);
                        _focusLoss = (FocusLostType.MuteAudio, ViewModel.AppHost.Device.GetVolume() is 0f);
                        break;
                    }
                case FocusLostType.BlockInputAndMuteAudio:
                    {
                        if (ViewModel.AppHost.Device.GetVolume() is 0)
                            goto case FocusLostType.BlockInput;

                        ViewModel.VolumeBeforeMute = ViewModel.AppHost.Device.GetVolume();
                        ViewModel.AppHost.Device.SetVolume(0);
                        ViewModel.AppHost.NpadManager.BlockInputUpdates();
                        _focusLoss = (FocusLostType.BlockInputAndMuteAudio, ViewModel.AppHost.Device.GetVolume() is 0f && ViewModel.AppHost.NpadManager.InputUpdatesBlocked);
                        break;
                    }
                case FocusLostType.PauseEmulation:
                    {
                        if (ViewModel.AppHost.Device.System.IsPaused)
                            return;

                        ViewModel.AppHost.Pause();
                        _focusLoss = (FocusLostType.PauseEmulation, ViewModel.AppHost.Device.System.IsPaused);
                        break;
                    }
            }
        }
    }
}
