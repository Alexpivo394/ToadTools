using System.Globalization;
using Visibility = System.Windows.Visibility;
using System.Windows.Data;
using System.Windows.Markup;
using Collisions.ViewModels;

namespace Collisions.Views;

/// <summary>Показывает часть панели на перечисленных через запятую экранах: <c>ConverterParameter=NotFound,Error</c>.</summary>
public sealed class ScreenVisibilityConverter : MarkupExtension, IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is PaneScreen screen && parameter is string screens && screens.Split(',').Any(s => s.Trim() == screen.ToString())
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

/// <summary>Пустая строка или null — элемент скрыт.</summary>
public sealed class TextVisibilityConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}
