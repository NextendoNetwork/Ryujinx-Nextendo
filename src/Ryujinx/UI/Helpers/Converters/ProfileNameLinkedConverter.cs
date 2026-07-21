using Avalonia.Data.Converters;
using Ryujinx.Ava.UI.ViewModels.Input;
using System;
using System.Globalization;

namespace Ryujinx.Ava.UI.Helpers
{
    public class ProfileNameLinkedConverter : IValueConverter
    {
        public static readonly ProfileNameLinkedConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string profileName || string.IsNullOrWhiteSpace(profileName))
            {
                return false;
            }

            if (parameter is InputViewModel viewModel)
            {
                return viewModel.IsProfileNameLinked(profileName);
            }

            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
