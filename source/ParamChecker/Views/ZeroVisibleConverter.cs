using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using Visibility = System.Windows.Visibility;

namespace ParamChecker.Views;

/// <summary>
///     Число элементов → подсказка «пусто» видна только при нуле. Привязка к <c>Count</c>, а не к самой коллекции:
///     к коллекции привязка не узнаёт о добавлении элементов.
/// </summary>
public sealed class ZeroVisibleConverter : MarkupExtension, IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}
