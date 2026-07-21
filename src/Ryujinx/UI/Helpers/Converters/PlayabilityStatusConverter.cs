using Avalonia.Data.Converters;
using Avalonia.Media;
using Gommon;
using Ryujinx.Ava.Common.Locale;
using System;
using System.Globalization;

namespace Ryujinx.Ava.UI.Helpers
{
    public class PlayabilityStatusConverter : IValueConverter
    {
        private static readonly Lazy<PlayabilityStatusConverter> _shared = new(() => new());
        public static PlayabilityStatusConverter Shared => _shared.Value;

        public object Convert(object value, Type _, object __, CultureInfo ___)
            => value.Cast<LocaleKeys>() switch
            {
                LocaleKeys.CompatibilityListNothing => Brushes.DarkGray,
                LocaleKeys.CompatibilityListBoots => Brushes.Red,
                LocaleKeys.CompatibilityListMenus => Brushes.Tomato,
                LocaleKeys.CompatibilityListIngame => Brushes.Orange,
                _ => Brushes.LimeGreen
            };

        public object ConvertBack(object value, Type _, object __, CultureInfo ___)
            => throw new NotSupportedException();
    }
}
