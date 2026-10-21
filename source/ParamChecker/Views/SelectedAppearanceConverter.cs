using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using Wpf.Ui.Controls;

namespace ParamChecker.Views;

/// <summary>Выбранная кнопка сегментного переключателя — Primary, остальные — Secondary.</summary>
public sealed class SelectedAppearanceConverter : MarkupExtension, IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? ControlAppearance.Primary : ControlAppearance.Secondary;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}
