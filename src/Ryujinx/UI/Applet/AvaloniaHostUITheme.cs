using Avalonia.Media;
using Ryujinx.Ava.UI.Windows;
using Ryujinx.HLE.UI;
using System;
using System.Globalization;

namespace Ryujinx.Ava.UI.Applet
{
    class AvaloniaHostUITheme : IHostUITheme
    {
        private readonly MainWindow _parent;

        public string FontFamily { get; }
        public ThemeColor DefaultBackgroundColor { get; }
        public ThemeColor DefaultForegroundColor { get; }
        public ThemeColor DefaultBorderColor { get; }
        public ThemeColor SelectionBackgroundColor { get; }
        public ThemeColor SelectionForegroundColor { get; }

        public AvaloniaHostUITheme(MainWindow parent)
        {
            _parent = parent;

            // Initialize font property
            FontFamily = GetSystemFontFamily();

            // Initialize all properties that depend on parent
            DefaultBackgroundColor = BrushToThemeColor(parent.Background);
            DefaultForegroundColor = BrushToThemeColor(parent.Foreground);
            DefaultBorderColor = BrushToThemeColor(parent.BorderBrush);
            SelectionBackgroundColor = BrushToThemeColor(parent.ViewControls.SearchBox.SelectionBrush);
            SelectionForegroundColor = BrushToThemeColor(parent.ViewControls.SearchBox.SelectionForegroundBrush);
        }

        private string GetSystemFontFamily()
        {
            if (OperatingSystem.IsWindows())
            {
                return GetWindowsFontByLanguage();
            }
            else if (OperatingSystem.IsMacOS())
            {
                return GetMacOSFontByLanguage();
            }
            else // Linux and other platforms
            {
                return GetLinuxFontByLanguage();
            }
        }

        private string GetWindowsFontByLanguage()
        {
            CultureInfo culture = CultureInfo.CurrentUICulture;
            string langCode = culture.Name;

            return culture.TwoLetterISOLanguageName switch
            {
                "zh" => langCode == "zh-CN" || langCode == "zh-Hans" || langCode == "zh-SG"
                    ? "Microsoft YaHei UI"  // Simplified Chinese
                    : "Microsoft JhengHei UI", // Traditional Chinese

                "ja" => "Yu Gothic UI",     // Japanese
                "ko" => "Malgun Gothic",     // Korean
                _ => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
                    ? "Segoe UI Variable"   // Other languages - Windows 11+
                    : _parent.FontFamily.Name // Fallback to parent window font
            };
        }

        private string GetMacOSFontByLanguage()
        {
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
            {
                "zh" => "PingFang SC",      // Chinese (both simplified and traditional)
                "ja" => "Hiragino Sans",    // Japanese
                "ko" => "Apple SD Gothic Neo", // Korean
                _ => _parent.FontFamily.Name // Fallback to parent window font
            };
        }

        private string GetLinuxFontByLanguage()
        {
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
            {
                "zh" => "Noto Sans CJK SC", // Chinese
                "ja" => "Noto Sans CJK JP", // Japanese
                "ko" => "Noto Sans CJK KR", // Korean
                _ => _parent.FontFamily.Name // Fallback to parent window font
            };
        }

        private static ThemeColor BrushToThemeColor(IBrush brush)
        {
            if (brush is SolidColorBrush solidColor)
            {
                return new ThemeColor(
                    (float)solidColor.Color.A / 255,
                    (float)solidColor.Color.R / 255,
                    (float)solidColor.Color.G / 255,
                    (float)solidColor.Color.B / 255
                );
            }
            return new ThemeColor();
        }
    }
}
