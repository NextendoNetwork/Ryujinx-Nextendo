using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Windows;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.Applet
{
    internal partial class ErrorAppletWindow : StyleableAppWindow
    {
        private readonly Window _owner;
        private object _buttonResponse;

        public ErrorAppletWindow(Window owner, string[] buttons, string message)
        {
            _owner = owner;
            Message = message;
            DataContext = this;
            InitializeComponent();
            SetNextendoWindowIcon();

            int responseId = 0;

            if (buttons != null)
            {
                foreach (string buttonText in buttons)
                {
                    AddButton(buttonText, responseId);
                    responseId++;
                }
            }
            else
            {
                AddButton(LocaleManager.Instance[LocaleKeys.InputDialogOk], 0);
            }
        }

        public ErrorAppletWindow()
        {
            DataContext = this;
            InitializeComponent();
            SetNextendoWindowIcon();
        }

        private void SetNextendoWindowIcon()
        {
            using Stream iconStream = AssetLoader.Open(new Uri("resm:Ryujinx.Assets.UIImages.Logo_Nextendo_Window.png?assembly=Ryujinx"));
            Icon = new Bitmap(iconStream);
        }

        public string Message { get; set; }

        private void AddButton(string label, object tag)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                Button button = new() { Content = label, Tag = tag };

                button.Click += Button_Click;
                ButtonStack.Children.Add(button);
            });
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                _buttonResponse = button.Tag;
            }

            Close();
        }

        public async Task<object> Run()
        {
            await ShowDialog(_owner);

            return _buttonResponse;
        }
    }
}
