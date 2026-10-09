using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;

namespace ToadTools.UI.Converters;

public class EnumDescriptionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return "";

        var name = value.ToString();
        if (name == null) return "";

        var field = value.GetType().GetField(name);
        var attr = field?.GetCustomAttributes(typeof(DescriptionAttribute), false)
            .FirstOrDefault() as DescriptionAttribute;

        return attr?.Description ?? name;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}