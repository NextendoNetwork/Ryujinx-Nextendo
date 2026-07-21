using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Gommon;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Common.Models;
using System;
using System.Globalization;

namespace Ryujinx.Ava.UI.Helpers
{
    internal class XCITrimmerFileSpaceSavingsConverter : IValueConverter
    {
        private const long _bytesPerMB = 1024 * 1024;

        public static readonly XCITrimmerFileSpaceSavingsConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || value == AvaloniaProperty.UnsetValue)
                return BindingOperations.DoNothing;

            if (value is not XCITrimmerFileModel app)
                return null;

            long originalSize = app.OriginalSizeB;
            long currentSavings = app.CurrentSavingsB;
            long potentialSavings = app.PotentialSavingsB;

            if (originalSize <= 0)
            {
                return GetFormattedString(app, 0, 0);
            }

            long mbValue = 0;
            double percentage = 0;

            if (currentSavings > 0)
            {
                mbValue = (currentSavings / _bytesPerMB).CoerceAtLeast(0);
                percentage = (currentSavings / (double)originalSize) * 100;
            }
            else if (potentialSavings > 0)
            {
                mbValue = (potentialSavings / _bytesPerMB).CoerceAtLeast(0);
                percentage = (potentialSavings / (double)originalSize) * 100;
            }

            return GetFormattedString(app, mbValue, percentage);
        }

        private string GetFormattedString(XCITrimmerFileModel app, long mb, double percentage)
        {
            // Round percentage to 1 decimal place
            double roundedPercentage = Math.Round(percentage, 1);

            if (app.CurrentSavingsB < app.PotentialSavingsB)
            {
                return LocaleManager.Instance.UpdateAndGetDynamicValue(
                    LocaleKeys.XCITrimmer_CalculatedSavingsLabel, mb, roundedPercentage);
            }
            else
            {
                return LocaleManager.Instance.UpdateAndGetDynamicValue(
                    LocaleKeys.XCITrimmer_CalculatedSavingsLabel, mb, roundedPercentage);
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
