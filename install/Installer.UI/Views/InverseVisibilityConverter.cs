using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Installer.UI.Views;

/// <summary>True → Collapsed, false → Visible.</summary>
public sealed class InverseVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
