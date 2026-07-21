using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using FluentAvalonia.UI.Controls;
using Ryujinx.Ava.Common.Models;
using System;
using System.Globalization;
using static Ryujinx.Common.Utilities.XCIFileTrimmer;

namespace Ryujinx.Ava.UI.Helpers
{
    internal class XCITrimmerFileStatusConverter : IValueConverter
    {
        public static XCITrimmerFileStatusConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is UnsetValueType)
                return BindingOperations.DoNothing;

            if (value is not XCITrimmerFileModel app)
                return default(Symbol);
            
            bool isProcessing = app.PercentageProgress != null;
            
            if (isProcessing)
                return Symbol.Sync;

            if (app.ProcessingOutcome is not OperationOutcome.Successful
                and not OperationOutcome.Undetermined)
                return Symbol.ImportantFilled;

            if (app.Trimmable && app.Untrimmable)
                return Symbol.Repair;

            if (app.Trimmable)
                return Symbol.Clear;

            if (app.Untrimmable)
                return Symbol.Checkmark;

            return Symbol.Help;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
