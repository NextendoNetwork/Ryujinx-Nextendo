using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Svg.Skia;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Systems.AppLibrary;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.UI.Controls;
using Avalonia.Layout;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.ViewModels;
using Ryujinx.Ava.UI.Windows;
using Ryujinx.Ava.Utilities;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Utilities;
using Ryujinx.Input;
using LibHac.Common;
using LibHac.Ns;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.Views.Misc
{
    public partial class ApplicationCarouselView : RyujinxControl<MainWindowViewModel>
    {
        public static readonly RoutedEvent<ApplicationOpenedEventArgs> ApplicationOpenedEvent =
            RoutedEvent.Register<ApplicationCarouselView, ApplicationOpenedEventArgs>(nameof(ApplicationOpened), RoutingStrategies.Bubble);

        public event EventHandler<ApplicationOpenedEventArgs> ApplicationOpened
        {
            add => AddHandler(ApplicationOpenedEvent, value);
            remove => RemoveHandler(ApplicationOpenedEvent, value);
        }

        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _connectionTimer;
        private readonly DispatcherTimer _gamepadTimer;

        private bool _gamepadLeftPressed;
        private bool _gamepadRightPressed;
        private bool _gamepadUpPressed;
        private bool _gamepadDownPressed;
        private bool _gamepadConfirmDown;
        private bool _gamepadMenuDown;
        private bool _gamepadBackDown;

        // [Nextendo] Joystick-driven context menu: the selected option index plus the UI
        // elements it moves across. The Y/X buttons open it and the joystick / A / B drive it.
        private int _contextMenuIndex;
        private readonly List<Border> _contextMenuItemBorders = [];
        private readonly List<Action> _contextMenuActions = [];

        // The profile dialog (left, circular button) so B can close it from the gamepad.
        private ContentDialog _profileDialog;

        // [Nextendo] Home+Plus combo: edge-tracked Plus press while Home is held (see
        // PollProfileShortcut). Runs unpolled by the game gate so it works with a game open.
        private bool _profileComboPlusPressed;

        private const int NavProfile = -1;
        private const int NavCarousel = 0;
        private const int NavBottom = 1;

        private int _navLevel;
        private int _bottomIndex;

        private MainWindow _window;

        private const int BottomButtonCount = 6;

        public ApplicationCarouselView()
        {
            InitializeComponent();

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => UpdateClock();
            _clockTimer.Start();

            _connectionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _connectionTimer.Tick += async (_, _) => await RefreshConnectionStatusAsync(false);
            _connectionTimer.Start();

            _gamepadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            // [Nextendo] The Home+Plus shortcut must keep working while a game/applet runs, so it
            // is polled before the gated PollGamepad: for the launcher a running game briefly
            // owns the controller for the menu ring, but the physical combo stays a dashboard one.
            _gamepadTimer.Tick += (_, _) =>
            {
                PollProfileShortcut();
                PollGamepad();
            };
            _gamepadTimer.Start();

            CarouselList.SelectionChanged += CarouselList_SelectionChanged;
            Loaded += (_, _) => { _ = LoadAvatarAsync(); LoadDiscordSvg(); };
            Loaded += (_, _) => LoadWallpaper();
            ConfigurationState.Instance.UI.WallpaperPath.Event += (_, _) => LoadWallpaper();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _window = VisualRoot as MainWindow;
        }

        // [Nextendo] Loads the Discord logo (PNG) into the round Discord button.
        private void LoadDiscordSvg()
        {
            if (DiscordButton == null)
                return;

            try
            {
                using var stream = AssetLoader.Open(new Uri("resm:Ryujinx.Assets.UIImages.Logo_Discord_Nextendo.png?assembly=Ryujinx"));
                DiscordButton.Content = new Image
                {
                    Source = new Bitmap(stream),
                    Width = 40,
                    Height = 40,
                    Stretch = Stretch.Uniform,
                };
            }
            catch
            {
                // The button simply keeps its default empty look if the asset is missing.
            }
        }

        // [Nextendo] Applies the user's chosen wallpaper image as the launcher background.
        private void LoadWallpaper()
        {
            try
            {
                string path = ConfigurationState.Instance.UI.WallpaperPath.Value;
                if (WallpaperBackground != null)
                {
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        if (Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase))
                        {
                            using var stream = File.OpenRead(path);
                            WallpaperBackground.Source = new SvgImage { Source = SvgSource.LoadFromStream(stream) };
                        }
                        else
                        {
                            WallpaperBackground.Source = new Bitmap(path);
                        }
                    }
                    else
                    {
                        WallpaperBackground.Source = null;
                    }
                }
            }
            catch
            {
                // Ignore bad/invalid wallpaper files; the carousel just keeps its default look.
            }
        }

        public void GameLaunched() { }

        public void CarouselList_DoubleTapped(object sender, TappedEventArgs args)
        {
            if (sender is ListBox { SelectedItem: ApplicationData selected })
                RaiseEvent(new ApplicationOpenedEventArgs(selected, ApplicationOpenedEvent));
        }

        private void ProfileButton_OnClick(object? sender, RoutedEventArgs e) => OpenNextendoProfile();

        /// <summary>
        /// [Nextendo] Opens the launcher profile dialog. Public so it can be called from the
        /// top-level window (Ctrl+F) and from the gamepad Home+Plus shortcut, both of which
        /// must keep working while a game is running. The dialog is tracked so the gamepad's
        /// B button can close it (see PollGamepad).
        /// </summary>
        public async void OpenNextendoProfile()
        {
            // Already open (e.g. fast double Ctrl+F, held combo): don't stack dialogs.
            if (_profileDialog != null)
            {
                return;
            }

            try
            {
                ClearBottomFocus();
                // [Nextendo] Full app-style profile: identity header + Friends/Activity/History
                // tabs in a modal dialog, shown from the Switch launcher's circular button.
                var profile = new NextendoProfileView();
                var dialog = new ContentDialog
                {
                    Title = "Perfil",
                    Content = profile,
                    CloseButtonText = "Cerrar",
                };

                // Tracked so the gamepad's B button can close it (see PollGamepad).
                _profileDialog = dialog;
                try
                {
                    await ContentDialogHelper.ShowAsync(dialog);
                }
                finally
                {
                    _profileDialog = null;
                }
            }
            catch (Exception)
            {
                _profileDialog = null;
                // Never let a UI issue in the profile block the launcher.
            }
        }

        private void ClearBottomFocus()
        {
            _navLevel = NavCarousel;
            UpdateSectionHighlights();
        }

        private void WebsiteButton_OnClick(object? sender, RoutedEventArgs e)
        {
            ClearBottomFocus();
            try { Ryujinx.Common.Helper.OpenHelper.OpenUrl(Ryujinx.Ava.Common.NextendoApi.SiteUrl()); }
            catch (Exception) { /* ignore */ }
        }

        private void StatusButton_OnClick(object? sender, RoutedEventArgs e)
        {
            ClearBottomFocus();
            try { Ryujinx.Common.Helper.OpenHelper.OpenUrl("https://nextendo.network/status"); }
            catch (Exception) { /* ignore */ }
        }

        private void DiscordButton_OnClick(object? sender, RoutedEventArgs e)
        {
            ClearBottomFocus();
            try { Ryujinx.Common.Helper.OpenHelper.OpenUrl("https://discord.com/invite/nextendonetwork"); }
            catch (Exception) { /* ignore */ }
        }

        private async void NewsButton_OnClick(object? sender, RoutedEventArgs e)
        {
            // [Nextendo] "What's new" news panel.
            ClearBottomFocus();
            try { await Ryujinx.Ava.Common.NextendoPatchNotes.ShowAsync(); }
            catch (Exception) { /* ignore */ }
        }

        // [Nextendo] Fast-open the Mii editor applet: the same action as Actions > Tools > Mii editor.
        private async void MiiEditorButton_OnClick(object? sender, RoutedEventArgs e)
        {
            ClearBottomFocus();
            try
            {
                var mii = new AppletMetadata(
                    ViewModel.ContentManager,
                    LocaleManager.Instance[LocaleKeys.MenuBar_Actions_MiiEditorButton],
                    0x0100000000001009);

                if (!mii.CanStart(out ApplicationData appData, out BlitStruct<ApplicationControlProperty> nacpData))
                    return;

                await ViewModel.LoadApplication(appData, ViewModel.IsFullScreen || ViewModel.StartGamesInFullscreen, nacpData);
            }
            catch (Exception)
            {
                // Never let a launcher shortcut crash the UI.
            }
        }

        // [Nextendo] Fast-open the controller configuration (Settings > the Input page).
        private async void ControlsButton_OnClick(object? sender, RoutedEventArgs e)
        {
            ClearBottomFocus();
            MainWindow window = _window;
            if (window == null || window.SettingsWindow != null)
                return;

            try
            {
                window.SettingsWindow = new SettingsWindow(window.VirtualFileSystem, window.ContentManager);
                window.SettingsWindow.NavPanel.Content = window.SettingsWindow.InputPage;
                window.SettingsWindow.NavPanel.SelectedItem = window.SettingsWindow.NavPanel.MenuItems.ElementAt(1);

                await ContentDialogHelper.ShowWindowAsync(window.SettingsWindow, window);
            }
            catch (Exception)
            {
                // Ignore: opening controls must never take the launcher down.
            }
            finally
            {
                window.SettingsWindow = null;
            }
        }

        // [Nextendo] Fast-open the emulator settings; same behaviour as Options > Settings (it
        // respects a running game's own configuration).
        private async void SettingsButton_OnClick(object? sender, RoutedEventArgs e)
        {
            ClearBottomFocus();
            MainWindow window = _window;
            if (window == null)
                return;

            window.SettingsWindow = new(window.VirtualFileSystem, window.ContentManager);

            Rainbow.Enable();

            if (ViewModel.SelectedApplication is null)
            {
                await StyleableAppWindow.ShowAsync(window.SettingsWindow);
            }
            else
            {
                bool customConfigExists = File.Exists(Program.GetDirGameUserConfig(ViewModel.SelectedApplication.IdString));

                if (!ViewModel.IsGameRunning || !customConfigExists)
                {
                    await window.SettingsWindow.ShowDialog(window);
                }
                else
                {
                    await StyleableAppWindow.ShowAsync(new GameSpecificSettingsWindow(ViewModel, customConfigExists));
                }
            }

            Rainbow.Disable();
            Rainbow.Reset();

            window.SettingsWindow = null;

            ViewModel.LoadConfigurableHotKeys();
        }

        private void CarouselList_KeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Avalonia.Input.Key.Down:
                    if (_navLevel == NavProfile)
                        ExitProfile();
                    else if (_navLevel == NavCarousel)
                        EnterBottom();
                    e.Handled = true;
                    break;
                case Avalonia.Input.Key.Up:
                    if (_navLevel == NavBottom)
                        ExitBottom();
                    else if (_navLevel == NavCarousel)
                        EnterProfile();
                    e.Handled = true;
                    break;
                case Avalonia.Input.Key.Right:
                    if (_navLevel == NavBottom)
                        MoveBottom(1);
                    else if (_navLevel == NavCarousel)
                        MoveBy(1);
                    e.Handled = true;
                    break;
                case Avalonia.Input.Key.Left:
                    if (_navLevel == NavBottom)
                        MoveBottom(-1);
                    else if (_navLevel == NavCarousel)
                        MoveBy(-1);
                    e.Handled = true;
                    break;
                case Avalonia.Input.Key.Enter:
                case Avalonia.Input.Key.Space:
                    if (_navLevel == NavProfile)
                        ProfileButton_OnClick(this, null);
                    else if (_navLevel == NavBottom)
                        ActivateBottom();
                    else
                        Confirm();
                    e.Handled = true;
                    break;
            }
        }

        internal void MoveLeft() => MoveBy(-1);
        internal void MoveRight() => MoveBy(1);
        internal void Confirm()
        {
            if (CarouselList.SelectedItem is ApplicationData selected)
                RaiseEvent(new ApplicationOpenedEventArgs(selected, ApplicationOpenedEvent));
        }

        private Avalonia.Controls.Button GetBottomButton(int index) => index switch
        {
            0 => WebsiteButton,
            1 => DiscordButton,
            2 => NewsButton,
            3 => MiiEditorButton,
            4 => ControlsButton,
            _ => SettingsButton,
        };

        private void EnterBottom()
        {
            _navLevel = NavBottom;
            _bottomIndex = 0;
            UpdateSectionHighlights();
        }

        private void ExitBottom()
        {
            _navLevel = NavCarousel;
            UpdateSectionHighlights();
        }

        private void EnterProfile()
        {
            _navLevel = NavProfile;
            UpdateSectionHighlights();
        }

        private void ExitProfile()
        {
            _navLevel = NavCarousel;
            UpdateSectionHighlights();
        }

        private void MoveBottom(int delta)
        {
            _bottomIndex = Math.Clamp(_bottomIndex + delta, 0, BottomButtonCount - 1);
            UpdateSectionHighlights();
        }

        private void UpdateSectionHighlights()
        {
            bool bottomFocused = _navLevel == NavBottom;
            for (int i = 0; i < BottomButtonCount; i++)
                GetBottomButton(i).Classes.Set("carouselBottomSelected", bottomFocused && i == _bottomIndex);

            if (ProfileSelectionRing != null)
                ProfileSelectionRing.IsVisible = _navLevel == NavProfile;
        }

        private void ActivateBottom()
        {
            switch (_bottomIndex)
            {
                case 0: WebsiteButton_OnClick(this, null); break;
                case 1: DiscordButton_OnClick(this, null); break;
                case 2: NewsButton_OnClick(this, null); break;
                case 3: MiiEditorButton_OnClick(this, null); break;
                case 4: ControlsButton_OnClick(this, null); break;
                default: SettingsButton_OnClick(this, null); break;
            }
        }

        private void CarouselList_ContextRequested(object? sender, ContextRequestedEventArgs e)
        {
            OpenContextMenu();
        }

        // [Nextendo] Opens the per-game options flyout for the currently selected tile. Shared
        // between the mouse right-click (ContextRequested) and the Y/X buttons on the gamepad,
        // and fully navigable with the joystick/d-pad: A accepts the highlighted option, B closes.
        private void OpenContextMenu()
        {
            if (CarouselList.SelectedItem is not ApplicationData selected)
            {
                return;
            }

            // The menu commands act on MainWindowViewModel.SelectedApplication, which in
            // carousel mode resolves to CarouselSelectedApplication.
            ViewModel.CarouselSelectedApplication = selected;

            _contextMenuActions.Clear();
            _contextMenuItemBorders.Clear();

            _contextMenuActions.Add(() => MainWindowViewModel.RunApplication.Execute(ViewModel));
            _contextMenuActions.Add(() => MainWindowViewModel.ToggleFavorite.Execute(ViewModel));
            _contextMenuActions.Add(() => MainWindowViewModel.OpenTitleUpdateManager.Execute(ViewModel));
            _contextMenuActions.Add(() => MainWindowViewModel.OpenDownloadableContentManager.Execute(ViewModel));
            _contextMenuActions.Add(() => MainWindowViewModel.OpenModManager.Execute(ViewModel));

            string[] labels =
            {
                LocaleManager.Instance[LocaleKeys.GameListContextMenuRunApplication],
                LocaleManager.Instance[LocaleKeys.GameListContextMenuToggleFavorite],
                LocaleManager.Instance[LocaleKeys.GameListContextMenuManageTitleUpdates],
                LocaleManager.Instance[LocaleKeys.GameListContextMenuManageDlc],
                LocaleManager.Instance[LocaleKeys.GameListContextMenuManageMod],
            };

            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 6,
            };

            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var border = new Border
                {
                    MinWidth = 230,
                    Padding = new Thickness(14, 7),
                    CornerRadius = new CornerRadius(5),
                    Child = new TextBlock
                    {
                        Text = labels[i],
                        FontWeight = FontWeight.SemiBold,
                    },
                };
                border.Tapped += (_, _) => ExecuteContextMenuItem(index);
                panel.Children.Add(border);
                _contextMenuItemBorders.Add(border);
            }

            _contextMenuIndex = 0;
            UpdateContextMenuHighlight();

            _contextMenuFlyout = new Flyout
            {
                Placement = PlacementMode.Bottom,
                Content = panel,
            };

            _contextMenuFlyout.ShowAt(CarouselList, true);
        }

        private void MoveContextMenuSelection(int delta)
        {
            if (_contextMenuItemBorders.Count == 0)
            {
                return;
            }

            _contextMenuIndex = Math.Clamp(_contextMenuIndex + delta, 0, _contextMenuItemBorders.Count - 1);
            UpdateContextMenuHighlight();
        }

        private void UpdateContextMenuHighlight()
        {
            for (int i = 0; i < _contextMenuItemBorders.Count; i++)
            {
                _contextMenuItemBorders[i].Background = new SolidColorBrush(Color.FromArgb(
                    (byte)(i == _contextMenuIndex ? 255 : 0), 62, 232, 200));
            }
        }

        private void ExecuteContextMenuItem(int index)
        {
            CloseContextMenu();

            if (index < 0 || index >= _contextMenuActions.Count)
            {
                return;
            }

            try
            {
                _contextMenuActions[index]();
            }
            catch (Exception)
            {
                // Never let a menu action crash the launcher.
            }
        }

        private void CloseContextMenu()
        {
            _contextMenuFlyout?.Hide();
            _contextMenuFlyout = null;
            _contextMenuIndex = 0;
        }

        private Flyout _contextMenuFlyout;

        private void MoveBy(int delta)
        {
            ReadOnlyObservableCollection<ApplicationData> list = ViewModel.CarouselAppsObservableList;
            if (list == null || list.Count == 0)
            {
                return;
            }

            ApplicationData current = CarouselList.SelectedItem as ApplicationData;
            int index = current != null ? list.IndexOf(current) : -1;
            if (index < 0)
            {
                index = 0;
            }

            int target = Math.Clamp(index + delta, 0, list.Count - 1);
            CarouselList.SelectedIndex = target;
            CarouselList.ScrollIntoView(list[target]);
        }

        private void CarouselList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // [Nextendo] Feed the library commands (Run, Favorite, managers...) which read
            // MainWindowViewModel.SelectedApplication.
            ViewModel.CarouselSelectedApplication = CarouselList.SelectedItem as ApplicationData;
        }

        // [Nextendo] Mouse wheel selects games like the stick does: wheel up = previous tile,
        // wheel down = next tile. Works in the carousel nav level only.
        private void CarouselList_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (_navLevel != NavCarousel)
            {
                return;
            }

            e.Handled = true;

            double delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
            if (delta > 0)
            {
                MoveBy(-1);
            }
            else if (delta < 0)
            {
                MoveBy(1);
            }
        }

        private IGamepad _menuGamepad;
        private string _menuGamepadId;

        private void ResetGamepadInputFlags()
        {
            _gamepadLeftPressed = false;
            _gamepadRightPressed = false;
            _gamepadUpPressed = false;
            _gamepadDownPressed = false;
            _gamepadConfirmDown = false;
            _gamepadMenuDown = false;
            _gamepadBackDown = false;
        }

        /// <summary>
        /// [Nextendo] Launcher shortcut: hold Home and press + once → open the profile dialog.
        /// The raw (unmapped) snapshot is used on purpose, because Home and + are physical buttons
        /// that players reach for as a "dashboard" gesture regardless of the in-game remapping —
        /// the mapped snapshot only exposes logical Switch buttons. It is polled outside the
        /// PollGamepad gate so the same combo works while a game/applet runs.
        /// </summary>
        private void PollProfileShortcut()
        {
            IGamepad gamepad = GetConfiguredGamepad();
            if (gamepad == null)
            {
                _profileComboPlusPressed = false;
                return;
            }

            GamepadStateSnapshot snapshot = gamepad.GetStateSnapshot();

            bool home = snapshot.IsPressed(GamepadButtonInputId.Guide);
            bool plus = snapshot.IsPressed(GamepadButtonInputId.Plus);

            if (home && plus && !_profileComboPlusPressed)
            {
                _profileComboPlusPressed = true;
                OpenNextendoProfile();
            }
            else if (!plus)
            {
                _profileComboPlusPressed = false;
            }
        }

        private void PollGamepad()
        {
            // [Nextendo] While a game/applet (Mii editor, controller applet, ...) runs in the
            // embedded renderer, or a modal window (settings, profile, ...) is open on top, the
            // controller belongs to that thing — the launcher must not keep consuming it (it
            // would move the menu, open panels, or even launch titles behind the Mii editor).
            // Also skip when the carousel is not on screen (another library view, or the renderer
            // replaced it). Edge flags are cleared so resumed input never triggers a phantom press.
            if (ViewModel.IsGameRunning ||
                !IsEffectivelyVisible ||
                (_window != null && _window.SettingsWindow != null))
            {
                ResetGamepadInputFlags();
                return;
            }

            IGamepad gamepad = GetConfiguredGamepad();
            if (gamepad == null)
            {
                ResetGamepadInputFlags();
                return;
            }

            // The configured gamepad maps physical inputs to logical Switch buttons via the
            // user's settings, so navigation honours whatever the player assigned in options
            // (remapped A/D-pad, the left stick, swapped buttons, etc.).
            GamepadStateSnapshot snapshot = gamepad.GetMappedStateSnapshot();
            (float stickX, float stickY) = snapshot.GetStick(StickInputId.Left);

            bool left = snapshot.IsPressed(GamepadButtonInputId.DpadLeft) || stickX < -0.5f;
            bool right = snapshot.IsPressed(GamepadButtonInputId.DpadRight) || stickX > 0.5f;
            bool up = snapshot.IsPressed(GamepadButtonInputId.DpadUp) || stickY > 0.5f;
            bool down = snapshot.IsPressed(GamepadButtonInputId.DpadDown) || stickY < -0.5f;
            // [Nextendo] Swapped A/B: the physical bottom button (logical B, i.e. Xbox "A") confirms /
            // accepts, and the physical right/side button (logical A) is "back" (close menus,
            // exit the profile ring). This is the usual PC controller convention; the mapped
            // snapshot means whatever the user assigned still drives these actions.
            bool confirm = snapshot.IsPressed(GamepadButtonInputId.B);
            bool back = snapshot.IsPressed(GamepadButtonInputId.A);

            // Y or X both open the same per-game options flyout as the right-click.
            // GetMappedStateSnapshot honours the user's input remapping for logical Y/X.
            bool menu = snapshot.IsPressed(GamepadButtonInputId.Y) ||
                        snapshot.IsPressed(GamepadButtonInputId.X);

            bool menuOpen = _contextMenuFlyout is { IsOpen: true };
            if (menuOpen)
            {
                // Joystick / d-pad drive the open context menu: up/down move the highlighted
                // option, the confirm button (logical B, bottom) executes it, the back button
                // (logical A, side) closes it. The carousel itself stays put.
                if (up && !_gamepadUpPressed)
                    MoveContextMenuSelection(-1);
                if (down && !_gamepadDownPressed)
                    MoveContextMenuSelection(1);
                if (confirm && !_gamepadConfirmDown)
                    ExecuteContextMenuItem(_contextMenuIndex);
                if (back && !_gamepadBackDown)
                    CloseContextMenu();
            }
            else
            {
                if (down && !_gamepadDownPressed)
                {
                    if (_navLevel == NavProfile)
                        ExitProfile();
                    else if (_navLevel == NavCarousel)
                        EnterBottom();
                }
                if (up && !_gamepadUpPressed)
                {
                    if (_navLevel == NavBottom)
                        ExitBottom();
                    else if (_navLevel == NavCarousel)
                        EnterProfile();
                }
                if (left && !_gamepadLeftPressed)
                {
                    if (_navLevel == NavBottom)
                        MoveBottom(-1);
                    else if (_navLevel == NavCarousel)
                        MoveBy(-1);
                }
                if (right && !_gamepadRightPressed)
                {
                    if (_navLevel == NavBottom)
                        MoveBottom(1);
                    else if (_navLevel == NavCarousel)
                        MoveBy(1);
                }
                if (confirm && !_gamepadConfirmDown)
                {
                    if (_navLevel == NavProfile)
                        ProfileButton_OnClick(this, null);
                    else if (_navLevel == NavBottom)
                        ActivateBottom();
                    else
                        Confirm();
                }
                // Back (logical A, the side/physical-right button, Xbox-style cancel): it
                // closes the profile dialog if one is open, otherwise it un-highlights the
                // profile ring.
                if (back && !_gamepadBackDown)
                {
                    if (_profileDialog != null)
                        _profileDialog.Hide();
                    else if (_navLevel == NavProfile)
                        ExitProfile();
                }
                if (menu && !_gamepadMenuDown && _navLevel == NavCarousel)
                {
                    OpenContextMenu();
                }
            }

            _gamepadLeftPressed = left;
            _gamepadRightPressed = right;
            _gamepadUpPressed = up;
            _gamepadDownPressed = down;
            _gamepadConfirmDown = confirm;
            _gamepadMenuDown = menu;
            _gamepadBackDown = back;
        }

        private IGamepad GetConfiguredGamepad()
        {
            Ryujinx.Input.HLE.InputManager inputManager = ViewModel.InputManager;
            if (inputManager?.GamepadDriver == null)
                return null;

            // Use the controller the user selected in the emulator's input settings (player 1).
            Ryujinx.Common.Configuration.Hid.InputConfig config = null;
            if (ViewModel.AppHost?.NpadManager != null)
                config = ViewModel.AppHost.NpadManager.GetPlayerInputConfigByIndex(0);

            string targetId = config is Ryujinx.Common.Configuration.Hid.Controller.StandardControllerInputConfig ? config.Id : null;

            // Fall back to the first connected gamepad when no controller mapping is active.
            if (string.IsNullOrEmpty(targetId))
            {
                foreach (var g in inputManager.GamepadDriver.GetGamepads())
                {
                    if (g.IsConnected)
                    {
                        targetId = g.Id;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(targetId))
                return null;

            if (_menuGamepad != null && _menuGamepad.Id == targetId)
            {
                if (_menuGamepad.IsConnected)
                    return _menuGamepad;

                _menuGamepad?.Dispose();
                _menuGamepad = null;
            }

            if (_menuGamepadId != targetId)
            {
                _menuGamepad?.Dispose();
                _menuGamepad = null;
                _menuGamepadId = targetId;
            }

            if (_menuGamepad == null)
            {
                try
                {
                    _menuGamepad = inputManager.GamepadDriver.GetGamepad(targetId);
                    if (_menuGamepad != null && config != null && !string.IsNullOrEmpty(config.Id))
                        _menuGamepad.SetConfiguration(config);
                }
                catch (Exception)
                {
                    _menuGamepad = null;
                }
            }

            return _menuGamepad;
        }

        private async Task LoadAvatarAsync()
        {
            // [Nextendo] Profile photo for the circular profile button. Loaded async so the
            // UI never blocks on the network. Falls back to a blank avatar on failure.
            if (!NextendoAccount.IsLinked)
                return;

            try
            {
                var profile = await Ryujinx.Ava.Common.NextendoApi.GetProfileSyncAsync();
                if (profile.image != null && profile.image.Length > 0 && ProfileAvatarImage != null)
                {
                    using var mem = new MemoryStream(profile.image);
                    var bitmap = new Avalonia.Media.Imaging.Bitmap(mem);
                    ProfileAvatarImage.Source = bitmap;
                }
            }
            catch (Exception)
            {
                // ignore network/avatar errors
            }
        }

        private void UpdateClock()
        {
            if (ClockTextBlock != null)
                ClockTextBlock.Text = DateTime.Now.ToString("HH:mm");
        }

        private async Task RefreshConnectionStatusAsync(bool force)
        {
            // Throttle: rely on the periodic timer.
            if (!force && _connectionLastCheck != null &&
                (DateTime.UtcNow - _connectionLastCheck.Value).TotalSeconds < 8)
                return;

            _connectionLastCheck = DateTime.UtcNow;

            bool hasInternet = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
            bool linked = NextendoAccount.IsLinked && !NextendoServerOverride.HorsNextendo;

            await Dispatcher.UIThread.InvokeAsync(() => UpdateConnectionDots(hasInternet, linked));
        }

        private DateTime? _connectionLastCheck;
        private Avalonia.Media.IBrush? internetBrush;

        private static bool IsWifi(System.Net.NetworkInformation.NetworkInterfaceType type)
        {
            return type == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211;
        }

        private void UpdateConnectionDots(bool hasInternet, bool linked)
        {
            if (NextendoStatusDot != null)
            {
                NextendoStatusDot.Fill = linked ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#33E86B"))
                                                : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#7A7A7A"));
            }

            var brush = internetBrush = hasInternet
                ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#33E86B"))
                : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#7A7A7A"));

            bool wifi = false;
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                    continue;
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
                {
                    wifi = true;
                    break;
                }
            }

            if (EthSignalBars != null)
                EthSignalBars.IsVisible = !wifi;

            if (WifiStrengthSymbol != null)
            {
                WifiStrengthSymbol.IsVisible = wifi;
                WifiStrengthSymbol.Foreground = wifi ? brush : null;
            }

            if (!wifi && EthSignalBars != null)
            {
                EthBar1.Fill = hasInternet ? brush : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#7A7A7A"));
                EthBar2.Fill = hasInternet ? brush : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#7A7A7A"));
                EthBar3.Fill = hasInternet ? brush : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#7A7A7A"));
                EthBar4.Fill = hasInternet ? brush : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#7A7A7A"));
            }
        }
    }
}
