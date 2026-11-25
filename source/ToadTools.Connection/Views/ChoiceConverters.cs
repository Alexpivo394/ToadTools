using System.Globalization;
using Visibility = System.Windows.Visibility;
using System.Windows.Data;
using Binding = System.Windows.Data.Binding;
using System.Windows.Markup;

namespace ToadTools.Connection.Views;

/// <summary>Радиокнопка выбора из перечисления: отмечена, когда значение равно имени в <c>ConverterParameter</c>.</summary>
public sealed class ChoiceConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && value.ToString() == parameter as string;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is string name ? Enum.Parse(targetType, name) : Binding.DoNothing;

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

/// <summary>Содержимое раздела видно, когда выбран раздел с именем из <c>ConverterParameter</c>.</summary>
public sealed class SectionVisibilityConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && value.ToString() == parameter as string ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}
