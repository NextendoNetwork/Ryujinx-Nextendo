using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ryujinx.Ava.Common;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.Models;
using System;
using System.Linq;
using System.Runtime.Versioning;

namespace Ryujinx.Ava.UI.Windows
{
    /// <summary>
    /// [Nextendo] The floating window that hosts the Switch-style in-game toasts. Separate, transparent
    /// and non-activating: the running game is a native child window that paints over Avalonia, so
    /// notifications drawn inside the main window would be hidden behind it.
    ///
    /// It is shown ONLY while at least one toast is on screen (a few seconds at a time) and hidden the
    /// moment they clear, so it never sits over the game (or captures clicks, or covers other apps)
    /// otherwise. The whole window is a single opaque panel — transparency over the game paints black,
    /// so we leave no transparent area — and its corners are rounded by DWM so they show the game, not
    /// black triangles.
    /// </summary>
    public partial class NextendoNotificationOverlayWindow : Window
    {
        // Push the overlay below the menu bar (~35px) so it never overlaps it.
        private const int MenuBarOffset = 40;

        private static Window _parent;
        private static NextendoNotificationOverlayWindow _overlay;

        public NextendoNotificationOverlayWindow()
        {
            InitializeComponent();

            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            SystemDecorations = SystemDecorations.None;
            Background = Brushes.Transparent;
            // NOT Topmost: owned by the main window, so it sits above the game but NOT above other
            // windows (e.g. a second Ryujinx in front) — the toast only shows over its own instance.
            CanResize = false;
            ShowInTaskbar = false;
            Focusable = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ExtendClientAreaTitleBarHeightHint = 0;

            // Size to the toast panel exactly, so there is no transparent area around it (which renders
            // solid black over the game). Shown only once a toast exists, so it always has content to
            // measure.
            SizeToContent = SizeToContent.WidthAndHeight;

            // Fade the panel in on show (the WINDOW opacity is left at 1 — animating it uses a layered
            // window, which blacks out the GPU-composited content; the panel is a normal control and
            // fades cleanly).
            ToastPanel.Opacity = 0;
            ToastPanel.Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(200) },
            };

            ToastHost.ItemsSource = NextendoInGameNotifications.Toasts;
            ToastHost.AddHandler(Button.ClickEvent, OnToastButtonClick);

            // Round the window's corners via DWM (the compositor cuts the window so the corners show the
            // game, not black), and make it non-activating so showing it doesn't steal focus from the
            // main window (which would freeze its menu bar). We deliberately do NOT set WS_EX_TRANSPARENT:
            // over the game's native surface it paints the whole thing solid black.
            Opened += (_, _) =>
            {
                MakeNoActivate(this);
                RoundCorners(this);
                _parent?.Activate();
            };
        }

        /// <summary>Wire the in-game toast overlay to the main window. Call once after it loads.</summary>
        public static void Attach(Window parent)
        {
            _parent = parent;

            // The service captures its baseline and polls only while a game runs.
            NextendoInGameNotifications.Initialize();
            NextendoGameInvites.Initialize();

            // Show only while there are toasts; hide as soon as they clear (and on game exit, when the
            // service clears the collection). Showing AFTER a toast exists also means the window always
            // has content to render.
            NextendoInGameNotifications.Toasts.CollectionChanged += (_, _) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (NextendoInGameNotifications.Toasts.Count > 0)
                    {
                        ShowOverlay();
                    }
                    else
                    {
                        _overlay?.Hide();
                    }
                });
            };

            // Follow the main window's client top-left as it moves / resizes / goes fullscreen. (No
            // hide-on-Deactivated: that hide/show loop froze the menu.)
            parent.PositionChanged += (_, _) => Sync();
            parent.PropertyChanged += (_, ev) =>
            {
                if (ev.Property == Visual.BoundsProperty)
                {
                    Sync();
                }
            };
        }

        // Accept/Decline on a game invitation or friend request toast.
        private static async void OnToastButtonClick(object sender, RoutedEventArgs e)
        {
            if (e.Source is not Button { Tag: NextendoToastModel { HasActions: true } toast } button)
            {
                return;
            }

            bool accept = button.Classes.Contains("accept");
            if (toast.IsFriendRequest)
            {
                bool succeeded = toast.FriendRequestPid is ulong pid &&
                    (accept
                        ? await NextendoApi.AcceptFriendAsync(pid)
                        : await NextendoApi.DeclineFriendAsync(pid));
                if (succeeded)
                {
                    NextendoInGameNotifications.Dismiss(toast.Id);
                }
                else
                {
                    NextendoInGameNotifications.PushNotice(
                        toast.Title,
                        accept
                            ? "Could not accept this friend request. Try again from Invites."
                            : "Could not decline this friend request. Try again from Invites.");
                }

                return;
            }

            NextendoInGameNotifications.Dismiss(toast.Id);
            if (!accept)
            {
                NextendoGameInvites.Decline(toast.InviteId);
                return;
            }

            string error = NextendoGameInvites.Accept(toast.InviteId);
            if (error != null)
            {
                NextendoInGameNotifications.PushNotice(toast.Title, error);
            }
        }

        private static void OnToastPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (sender is not Border { DataContext: NextendoToastModel { OpensInvites: true } } ||
                e.Source is Visual source && source.GetSelfAndVisualAncestors().OfType<Button>().Any())
            {
                return;
            }

            RyujinxApp.MainWindow?.OpenNextendoInvitesDashboard();
        }

        private static void ShowOverlay()
        {
            if (_parent == null)
            {
                return;
            }

            _overlay ??= new NextendoNotificationOverlayWindow();
            Sync();

            if (!_overlay.IsVisible)
            {
                _overlay.ToastPanel.Opacity = 0;
                _overlay.Show(_parent);
            }

            // Fade in on the next frame, so a frame renders at 0 first and the transition animates
            // (setting it synchronously right after Show jumps straight to 1 with no animation).
            NextendoNotificationOverlayWindow overlay = _overlay;
            Dispatcher.UIThread.Post(() => overlay.ToastPanel.Opacity = 1, DispatcherPriority.Background);
        }

        private static void Sync()
        {
            if (_overlay == null || _parent == null)
            {
                return;
            }

            try
            {
                _overlay.Position = _parent.PointToScreen(new Point(16, MenuBarOffset));
            }
            catch
            {
                // Parent not realised yet; the next event re-syncs.
            }
        }

        // Keep the overlay from ever taking focus, so the main window's menu bar stays usable.
        private static void MakeNoActivate(Window w)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            nint hwnd = w.TryGetPlatformHandle()?.Handle ?? nint.Zero;
            if (hwnd != nint.Zero)
            {
                ApplyNoActivate(hwnd);
            }
        }

        [SupportedOSPlatform("windows")]
        private static void ApplyNoActivate(nint hwnd)
        {
            nint ex = Win32NativeInterop.GetWindowLongPtrW(hwnd, Win32NativeInterop.GWL_EXSTYLE);
            Win32NativeInterop.SetWindowLongPtrW(hwnd, Win32NativeInterop.GWL_EXSTYLE,
                unchecked((nint)((ulong)ex | Win32NativeInterop.WS_EX_NOACTIVATE)));
        }

        // Round the window corners via DWM. A GDI window region (SetWindowRgn) does NOT clip Avalonia's
        // DirectComposition content, so it left the corners black; DWM's corner preference cuts the
        // window at the compositor and the corners show the game instead.
        private static void RoundCorners(Window w)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            nint hwnd = w.TryGetPlatformHandle()?.Handle ?? nint.Zero;
            if (hwnd != nint.Zero)
            {
                ApplyRoundedCorners(hwnd);
            }
        }

        [SupportedOSPlatform("windows")]
        private static void ApplyRoundedCorners(nint hwnd)
        {
            int pref = Win32NativeInterop.DWMWCP_ROUND;
            Win32NativeInterop.DwmSetWindowAttribute(
                hwnd, Win32NativeInterop.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
    }
}
