using Avalonia.Data.Converters;
using CommandLine;
using Ryujinx.Ava.Common.Locale;
using System;
using System.Globalization;

namespace Ryujinx.Ava.UI.Helpers
{
    public class LocaleKeyValueConverter : IValueConverter
    {
        private static readonly Lazy<LocaleKeyValueConverter> _shared = new(() => new());
        public static LocaleKeyValueConverter Shared => _shared.Value;

        public object Convert(object value, Type _, object __, CultureInfo ___) 
            => LocaleManager.Instance[value.Cast<LocaleKeys>()];

        public object ConvertBack(object value, Type _, object __, CultureInfo ___)
            => throw new NotSupportedException();
    }
}
