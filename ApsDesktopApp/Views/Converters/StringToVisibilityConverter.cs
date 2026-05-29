using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ApsDesktopApp.Views.Converters;

// Collapses an element when its bound string is null/empty, otherwise shows it.
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.IsNullOrWhiteSpace(value as string)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
