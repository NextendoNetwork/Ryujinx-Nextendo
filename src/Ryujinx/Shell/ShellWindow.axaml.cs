using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Linq;

namespace Ryujinx.Ava.Shell
{
    // Root window of the new Nextendo UI (design ported from Harbor).
    // Booted only when NEXTENDO_UI=harbor; the legacy MainWindow is otherwise unchanged.
    // Custom chrome (no OS title bar) — window controls + drag handled here.
    public partial class ShellWindow : Window
    {
        private bool _collapsed;

        public ShellWindow()
        {
            InitializeComponent();
            DataContext = new ShellViewModel();
        }

        private void OnToggleCollapse(object sender, RoutedEventArgs e)
        {
            _collapsed = !_collapsed;
            RootGrid.ColumnDefinitions[0].Width = new GridLength(_collapsed ? 76 : 240);
            Sidebar.Classes.Set("collapsed", _collapsed);
            CollapseIcon.Value = _collapsed ? "fa-solid fa-angles-right" : "fa-solid fa-angles-left";
        }

        private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void OnMaximizeRestore(object sender, RoutedEventArgs e) => ToggleMaximize();

        private void OnClose(object sender, RoutedEventArgs e) => Close();

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            MaxGlyph.Text = WindowState == WindowState.Maximized ? "❐" : "□";
        }

        private void OnTitleBarPressed(object sender, PointerPressedEventArgs e)
        {
            // Ignore drags that start on an interactive control (e.g. window buttons).
            if (e.Source is Visual v && v.GetSelfAndVisualAncestors().OfType<Button>().Any())
            {
                return;
            }

            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }

            BeginMoveDrag(e);
        }
    }
}
