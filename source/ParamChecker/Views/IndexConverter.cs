using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace ParamChecker.Views;

/// <summary>Индекс строки списка с нуля → номер с единицы.</summary>
public sealed class IndexConverter : MarkupExtension, IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int index ? (index + 1).ToString(culture) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}
