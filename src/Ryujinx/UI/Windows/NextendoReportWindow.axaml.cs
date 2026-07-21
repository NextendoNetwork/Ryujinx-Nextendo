using Avalonia.Media;
using Ryujinx.Ava.Common;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Common.Configuration;
using System;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.Windows
{
    /// <summary>
    /// [Nextendo] "Signaler un problème" — the player's side of the bug-report path. It sends
    /// what they type plus the tail of the emulator log (NextendoApi.SendReportAsync reads it),
    /// so a report lands in the admin inbox with the actual session log rather than a screenshot.
    /// </summary>
    public partial class NextendoReportWindow : StyleableAppWindow
    {
        private bool _sending;

        public static void Open(string errorCode = null)
        {
            NextendoReportWindow window = new();

            if (!string.IsNullOrEmpty(errorCode))
            {
                window.ErrorCodeBox.Text = errorCode;
            }

            window.Show(RyujinxApp.MainWindow);
        }

        public NextendoReportWindow() : base(useCustomTitleBar: true, 37)
        {
            Title = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ReportTitle];

            InitializeComponent();

            CancelButton.Click += (_, _) => Close();
            SendButton.Click += async (_, _) => await Send();
        }

        private async Task Send()
        {
            if (_sending)
            {
                return;
            }

            // A report with neither a code nor a description is nothing to act on — nudge rather
            // than send an empty row to the inbox.
            if (string.IsNullOrWhiteSpace(ErrorCodeBox.Text) && string.IsNullOrWhiteSpace(CommentBox.Text))
            {
                ShowStatus(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ReportEmpty], ok: false);

                return;
            }

            if (!NextendoAccount.IsLinked)
            {
                ShowStatus(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ReportNeedsAccount], ok: false);

                return;
            }

            _sending = true;
            SendButton.IsEnabled = false;
            ShowStatus(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ReportSending], ok: true);

            // The unchecked box means "do not attach the log": pass an empty comment marker rather
            // than the log. SendReportAsync always reads the log, so honour the choice here by
            // telling it not to when the player opted out.
            (bool ok, string message) = AttachLogCheck.IsChecked == true
                ? await NextendoApi.SendReportAsync(ErrorCodeBox.Text?.Trim(), CommentBox.Text?.Trim())
                : await NextendoApi.SendReportAsync(ErrorCodeBox.Text?.Trim(), CommentBox.Text?.Trim(), attachLog: false);

            if (ok)
            {
                ShowStatus(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ReportSent], ok: true);

                // Leave the confirmation up briefly, then close on its own.
                await Task.Delay(1400);
                Close();

                return;
            }

            _sending = false;
            SendButton.IsEnabled = true;
            ShowStatus(message, ok: false);
        }

        private void ShowStatus(string text, bool ok)
        {
            StatusText.Text = text;
            StatusText.Foreground = Brush.Parse(ok ? "#3EE8C8" : "#E8833E");
            StatusText.IsVisible = !string.IsNullOrEmpty(text);
        }
    }
}
