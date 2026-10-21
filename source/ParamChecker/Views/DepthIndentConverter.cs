using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace ParamChecker.Views;

/// <summary>Уровень вложенности узла → отступ слева, чтобы галочки оставались ровными на всех уровнях.</summary>
public sealed class DepthIndentConverter : MarkupExtension, IValueConverter
{
    private const double Step = 18;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new Thickness(value is int depth ? depth * Step : 0, 0, 0, 0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}
