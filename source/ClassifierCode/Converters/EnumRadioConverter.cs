using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using Binding = System.Windows.Data.Binding;

namespace ClassifierCode.Converters;

/// <summary>
///     Привязка группы RadioButton к enum: ConverterParameter — значение кнопки.
///     Снятие отметки ничего не пишет в источник, иначе снимаемая кнопка перетирает выбор новой.
/// </summary>
public sealed class EnumRadioConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Equals(value, parameter);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? parameter : Binding.DoNothing;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return this;
    }
}
