using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Windows;
using Ryujinx.Common.Helper;
using Ryujinx.Common.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.Helpers
{
    public class CheckBoxDialogResult
    {
        public bool IsChecked { get; set; }
    }

    public static class ContentDialogHelper
    {
        private static bool _isChoiceDialogOpen;
        private static ContentDialogOverlayWindow _contentDialogOverlayWindow;

        public static ContentDialog ApplyStyles(
            this ContentDialog contentDialog,
            double closeButtonWidth = 80,
            HorizontalAlignment buttonSpaceAlignment = HorizontalAlignment.Right)
        {
            Style closeButton = new(x => x.Name("CloseButton"));
            closeButton.Setters.Add(new Setter(Layoutable.WidthProperty, closeButtonWidth));

            Style closeButtonParent = new(x => x.Name("CommandSpace"));
            closeButtonParent.Setters.Add(new Setter(Layoutable.HorizontalAlignmentProperty, buttonSpaceAlignment));

            contentDialog.Styles.Add(closeButton);
            contentDialog.Styles.Add(closeButtonParent);

            return contentDialog;
        }

        private async static Task<UserResult> ShowContentDialog(
             string title,
             object content,
             string primaryButton,
             string secondaryButton,
             string closeButton,
             UserResult primaryButtonResult = UserResult.Ok,
             ManualResetEvent deferResetEvent = null,
             TypedEventHandler<ContentDialog, ContentDialogButtonClickEventArgs> deferCloseAction = null)
        {
            UserResult result = UserResult.None;

            ContentDialog contentDialog = new()
            {
                Title = title,
                PrimaryButtonText = primaryButton,
                SecondaryButtonText = secondaryButton,
                CloseButtonText = closeButton,
                Content = content,
                PrimaryButtonCommand = Commands.Create(() =>
                {
                    result = primaryButtonResult;
                })
            };

            contentDialog.SecondaryButtonCommand = Commands.Create(() =>
            {
                result = UserResult.No;
                contentDialog.PrimaryButtonClick -= deferCloseAction;
            });

            contentDialog.CloseButtonCommand = Commands.Create(() =>
            {
                result = UserResult.Cancel;
                contentDialog.PrimaryButtonClick -= deferCloseAction;
            });

            if (deferResetEvent != null)
            {
                contentDialog.PrimaryButtonClick += deferCloseAction;
            }

            await ShowAsync(contentDialog);

            return result;
        }

        public async static Task<UserResult> ShowTextDialog(
            string title,
            string primaryText,
            string secondaryText,
            string primaryButton,
            string secondaryButton,
            string closeButton,
            int iconSymbol,
            UserResult primaryButtonResult = UserResult.Ok,
            ManualResetEvent deferResetEvent = null,
            TypedEventHandler<ContentDialog, ContentDialogButtonClickEventArgs> deferCloseAction = null)
        {
            Grid content = CreateTextDialogContent(primaryText, secondaryText, iconSymbol);

            return await ShowContentDialog(title, content, primaryButton, secondaryButton, closeButton, primaryButtonResult, deferResetEvent, deferCloseAction);
        }
        
        public async static Task<UserResult> ShowTextDialogWithButton(
            string title,
            string primaryText,
            string secondaryText,
            string primaryButton,
            string secondaryButton,
            string closeButton,
            int iconSymbol,
            string buttonText,
            Action onClick,
            UserResult primaryButtonResult = UserResult.Ok,
            ManualResetEvent deferResetEvent = null,
            TypedEventHandler<ContentDialog, ContentDialogButtonClickEventArgs> deferCloseAction = null)
        {
            Grid content = CreateTextDialogContentWithButton(primaryText, secondaryText, iconSymbol, buttonText, onClick);

            return await ShowContentDialog(title, content, primaryButton, secondaryButton, closeButton, primaryButtonResult, deferResetEvent, deferCloseAction);
        }

        public static async Task<UserResult> ShowDeferredContentDialog(
            Window window,
            string title,
            string primaryText,
            string secondaryText,
            string primaryButton,
            string secondaryButton,
            string closeButton,
            int iconSymbol,
            ManualResetEvent deferResetEvent,
            Func<Window, Task> doWhileDeferred = null)
        {
            bool startedDeferring = false;

            return await ShowTextDialog(
                title,
                primaryText,
                secondaryText,
                primaryButton,
                secondaryButton,
                closeButton,
                iconSymbol,
                primaryButton == LocaleManager.Instance[LocaleKeys.InputDialogYes] ? UserResult.Yes : UserResult.Ok,
                deferResetEvent,
                DeferClose);

            async void DeferClose(ContentDialog sender, ContentDialogButtonClickEventArgs args)
            {
                if (startedDeferring)
                {
                    return;
                }

                sender.PrimaryButtonClick -= DeferClose;

                startedDeferring = true;

                Deferral deferral = args.GetDeferral();

                sender.PrimaryButtonClick -= DeferClose;

                _ = Task.Run(() =>
                {
                    deferResetEvent.WaitOne();

                    Dispatcher.UIThread.Post(() =>
                    {
                        deferral.Complete();
                    });
                });

                if (doWhileDeferred != null)
                {
                    await doWhileDeferred(window);

                    deferResetEvent.Set();
                }
            }
        }

        private static Grid CreateTextDialogContent(string primaryText, string secondaryText, int symbol)
        {
            Grid content = new()
            {
                RowDefinitions = [new(), new()],
                ColumnDefinitions = [new(GridLength.Auto), new()],

                MinHeight = 80,
            };

            content.Children.Add(new SymbolIcon
            {
                Symbol = (Symbol)symbol,
                Margin = new Thickness(10),
                FontSize = 40,
                FlowDirection = FlowDirection.LeftToRight,
                VerticalAlignment = VerticalAlignment.Center,
                GridColumn = 0,
                GridRow = 0,
                GridRowSpan = 2
            });

            content.Children.Add(new TextBlock
            {
                Text = primaryText,
                Margin = new Thickness(5),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 450,
                GridColumn = 1,
                GridRow = 0
            });

            content.Children.Add(new TextBlock
            {
                Text = secondaryText,
                Margin = new Thickness(5),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 450,
                GridColumn = 1,
                GridRow = 1
            });

            return content;
        }

        private static Grid CreateTextDialogContentWithButton(string primaryText, string secondaryText, int symbol, string buttonName, Action onClick)
        {
            Grid content = new()
            {
                RowDefinitions = [new(), new(), new(GridLength.Star), new()],
                ColumnDefinitions = [new(GridLength.Auto), new()],

                MinHeight = 80,
            };

            content.Children.Add(new SymbolIcon
            {
                Symbol = (Symbol)symbol,
                Margin = new Thickness(10),
                FontSize = 40,
                FlowDirection = FlowDirection.LeftToRight,
                VerticalAlignment = VerticalAlignment.Center,
                GridColumn = 0,
                GridRow = 0,
                GridRowSpan = 2
            });

            StackPanel buttonContent = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2
            };

            buttonContent.Children.Add(new TextBlock
            {
                Text = buttonName, 
                Margin = new Thickness(2)
            });

            buttonContent.Children.Add(new SymbolIcon
            {
                FlowDirection = FlowDirection.LeftToRight,
                Symbol = Symbol.Open
            });

            content.Children.Add(new TextBlock
            {
                Text = primaryText,
                Margin = new Thickness(5),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 450,
                GridColumn = 1,
                GridRow = 0
            });

            content.Children.Add(new TextBlock
            {
                Text = secondaryText,
                Margin = new Thickness(5),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 450,
                GridColumn = 1,
                GridRow = 1
            });

            content.Children.Add(new Button
            {
                Content = buttonContent,
                HorizontalAlignment = HorizontalAlignment.Center,
                Command = Commands.Create(onClick),
                GridRow = 2,
                GridColumnSpan = 2,
            });

            return content;
        }

        public static Task<UserResult> CreateInfoDialog(
            string primary,
            string secondaryText,
            string acceptButton,
            string closeButton,
            string title)
            => ShowTextDialog(
                title,
                primary,
                secondaryText,
                acceptButton,
                string.Empty,
                closeButton,
                (int)Symbol.Important);

        internal static async Task<UserResult> CreateConfirmationDialog(
            string primaryText,
            string secondaryText,
            string acceptButtonText,
            string cancelButtonText,
            string title,
            UserResult primaryButtonResult = UserResult.Yes)
            => await ShowTextDialog(
                string.IsNullOrWhiteSpace(title) ? LocaleManager.Instance[LocaleKeys.DialogConfirmationTitle] : title,
                primaryText,
                secondaryText,
                acceptButtonText,
                string.Empty,
                cancelButtonText,
                (int)Symbol.Help,
                primaryButtonResult);

        internal static async Task<UserResult> CreateDeniableConfirmationDialog(
            string primaryText,
            string secondaryText,
            string acceptButtonText,
            string noAcceptButtonText,
            string cancelButtonText,
            string title,
            UserResult primaryButtonResult = UserResult.Yes)
            => await ShowTextDialog(
                string.IsNullOrWhiteSpace(title) ? LocaleManager.Instance[LocaleKeys.DialogConfirmationTitle] : title,
                primaryText,
                secondaryText,
                acceptButtonText,
                noAcceptButtonText,
                cancelButtonText,
                (int)Symbol.Help,
                primaryButtonResult);

        internal static async Task<UserResult> CreateLocalizedConfirmationDialog(string primaryText, string secondaryText)
            => await CreateConfirmationDialog(
                primaryText,
                secondaryText,
                LocaleManager.Instance[LocaleKeys.InputDialogYes],
                LocaleManager.Instance[LocaleKeys.InputDialogNo],
                LocaleManager.Instance[LocaleKeys.RyujinxConfirm]);

        internal static async Task CreateUpdaterInfoDialog(string primary, string secondaryText)
            => await ShowTextDialog(
                LocaleManager.Instance[LocaleKeys.DialogUpdaterTitle],
                primary,
                secondaryText,
                string.Empty,
                string.Empty,
                LocaleManager.Instance[LocaleKeys.InputDialogOk],
                (int)Symbol.Important);

        internal static async Task CreateUpdaterUpToDateInfoDialog(string primary, string secondaryText,
            string changelogUrl)
        {
            await ShowTextDialogWithButton(
                LocaleManager.Instance[LocaleKeys.DialogUpdaterTitle],
                primary,
                secondaryText,
                string.Empty,
                string.Empty,
                LocaleManager.Instance[LocaleKeys.InputDialogOk],
                (int)Symbol.Important,
                LocaleManager.Instance[LocaleKeys.DialogUpdaterShowChangelogMessage],
                () => OpenHelper.OpenUrl(changelogUrl));
        }

        internal static async Task CreateWarningDialog(string primary, string secondaryText)
            => await ShowTextDialog(
                LocaleManager.Instance[LocaleKeys.DialogWarningTitle],
                primary,
                secondaryText,
                string.Empty,
                string.Empty,
                LocaleManager.Instance[LocaleKeys.InputDialogOk],
                (int)Symbol.Important);

        internal static async Task CreateErrorDialog(string errorMessage, string secondaryErrorMessage = "")
        {
            Logger.Error?.Print(LogClass.Application, errorMessage);

            await ShowTextDialog(
                LocaleManager.Instance[LocaleKeys.DialogErrorTitle],
                LocaleManager.Instance[LocaleKeys.DialogErrorMessage],
                errorMessage,
                secondaryErrorMessage,
                string.Empty,
                LocaleManager.Instance[LocaleKeys.InputDialogOk],
                (int)Symbol.Dismiss);
        }

        internal static async Task<bool> CreateChoiceDialog(string title, string primary, string secondaryText)
        {
            if (_isChoiceDialogOpen)
            {
                return false;
            }

            _isChoiceDialogOpen = true;

            UserResult response = await ShowTextDialog(
                title,
                primary,
                secondaryText,
                LocaleManager.Instance[LocaleKeys.InputDialogYes],
                string.Empty,
                LocaleManager.Instance[LocaleKeys.InputDialogNo],
                (int)Symbol.Help,
                UserResult.Yes);

            _isChoiceDialogOpen = false;

            return response == UserResult.Yes;
        }

        internal static async Task<CheckBoxDialogResult> CreateCheckBoxDialog(string title, string primaryText, string checkBoxText, bool isCheckedDefault)
        {
            CheckBoxDialogResult result = new CheckBoxDialogResult { IsChecked = isCheckedDefault };

            Grid content = new()
            {
                RowDefinitions = [new(), new(), new()],
                ColumnDefinitions = [new(GridLength.Auto), new()],
                MinHeight = 80,
            };

            content.Children.Add(new SymbolIcon
            {
                Symbol = (Symbol)Symbol.Important,
                Margin = new Thickness(10),
                FontSize = 40,
                FlowDirection = FlowDirection.LeftToRight,
                VerticalAlignment = VerticalAlignment.Center,
                GridColumn = 0,
                GridRow = 0,
                GridRowSpan = 2
            });

            content.Children.Add(new TextBlock
            {
                Text = primaryText,
                Margin = new Thickness(5),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 450,
                GridColumn = 1,
                GridRow = 0
            });

            CheckBox checkBox = new()
            {
                Content = checkBoxText,
                IsChecked = isCheckedDefault,
                Margin = new Thickness(5),
                GridColumn = 1,
                GridRow = 1
            };

            checkBox.IsCheckedChanged += (s, e) =>
            {
                result.IsChecked = checkBox.IsChecked == true;
            };

            content.Children.Add(checkBox);

            ContentDialog contentDialog = new()
            {
                Title = title,
                PrimaryButtonText = LocaleManager.Instance[LocaleKeys.InputDialogOk],
                Content = content,
            };

            await ShowAsync(contentDialog);

            return result;
        }

        internal static async Task<UserResult> CreateUpdaterChoiceDialog(string title, string primary, string secondaryText, string changelogUrl)
        {
            if (_isChoiceDialogOpen)
            {
                return UserResult.Cancel;
            }

            _isChoiceDialogOpen = true;

            UserResult response = await ShowTextDialogWithButton(
                title,
                primary,
                secondaryText,
                LocaleManager.Instance[LocaleKeys.InputDialogYes],
                string.Empty,
                LocaleManager.Instance[LocaleKeys.InputDialogNo],
                (int)Symbol.Help,
                LocaleManager.Instance[LocaleKeys.DialogUpdaterShowChangelogMessage],
                () => OpenHelper.OpenUrl(changelogUrl),
                UserResult.Yes);

            _isChoiceDialogOpen = false;

            return response;
        }

        internal static async Task<bool> CreateExitDialog()
        {
            return await CreateChoiceDialog(
                LocaleManager.Instance[LocaleKeys.DialogExitTitle],
                LocaleManager.Instance[LocaleKeys.DialogExitMessage],
                LocaleManager.Instance[LocaleKeys.DialogExitSubMessage]);
        }

        internal static async Task<bool> CreateStopEmulationDialog()
        {
            return await CreateChoiceDialog(
                LocaleManager.Instance[LocaleKeys.DialogStopEmulationTitle],
                LocaleManager.Instance[LocaleKeys.DialogStopEmulationMessage],
                LocaleManager.Instance[LocaleKeys.DialogExitSubMessage]);
        }

        public static async Task<ContentDialogResult> ShowAsync(ContentDialog contentDialog)
        {
            ContentDialogResult result;
            bool isTopDialog = true;

            Window parent = GetMainWindow();

            if (_contentDialogOverlayWindow != null)
            {
                isTopDialog = false;
            }

            if (parent is MainWindow window)
            {
                parent.Activate();

                _contentDialogOverlayWindow = new ContentDialogOverlayWindow
                {
                    Height = parent.Bounds.Height,
                    Width = parent.Bounds.Width,
                    Position = parent.PointToScreen(new Point()),
                    ShowInTaskbar = false,
                };

#if DEBUG
                _contentDialogOverlayWindow.AttachDevTools(new KeyGesture(Key.F12, KeyModifiers.Control));
#endif

                parent.PositionChanged += OverlayOnPositionChanged;

                void OverlayOnPositionChanged(object sender, PixelPointEventArgs e)
                {
                    if (_contentDialogOverlayWindow is null)
                    {
                        return;
                    }

                    _contentDialogOverlayWindow.Position = parent.PointToScreen(new Point());
                }

                _contentDialogOverlayWindow.ContentDialog = contentDialog;

                bool opened = false;

                _contentDialogOverlayWindow.Opened += OverlayOnActivated;

                async void OverlayOnActivated(object sender, EventArgs e)
                {
                    if (opened)
                    {
                        return;
                    }

                    opened = true;

                    _contentDialogOverlayWindow.Position = parent.PointToScreen(new Point());

                    result = await ShowDialog();
                }

                result = await _contentDialogOverlayWindow.ShowDialog<ContentDialogResult>(parent);
            }
            else
            {
                result = await ShowDialog();
            }

            async Task<ContentDialogResult> ShowDialog()
            {
                if (_contentDialogOverlayWindow is not null)
                {
                    result = await contentDialog.ShowAsync(_contentDialogOverlayWindow);

                    _contentDialogOverlayWindow!.Close();
                }
                else
                {
                    result = ContentDialogResult.None;

                    Logger.Warning?.Print(LogClass.UI, "Content dialog overlay failed to populate. Default value has been returned.");
                }

                return result;
            }

            if (isTopDialog && _contentDialogOverlayWindow is not null)
            {
                _contentDialogOverlayWindow.Content = null;
                _contentDialogOverlayWindow.Close();
                _contentDialogOverlayWindow = null;
            }

            return result;
        }

        public static async Task ShowWindowAsync(Window dialogWindow, Window mainWindow = null)
        {
            await dialogWindow.ShowDialog(_contentDialogOverlayWindow ?? mainWindow ?? GetMainWindow());
        }

        private static MainWindow GetMainWindow()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime al)
            {
                foreach (Window item in al.Windows)
                {
                    if (item is MainWindow window)
                    {
                        return window;
                    }
                }
            }

            return null;
        }
    }
}
