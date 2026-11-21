using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media.Imaging;

namespace Families.Views;

/// <summary>PNG картинки семейства → изображение, сразу в нужную ширину; битый файл — пусто, строка покажет заглушку.</summary>
public sealed class PreviewConverter : MarkupExtension, IValueConverter
{
    public int DecodeWidth { get; set; } = 128;

    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } bytes) return null;
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = DecodeWidth;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

/// <summary>Видимость: true / непустой текст / не null — видно; <c>Invert</c> — наоборот.</summary>
public sealed class ShownConverter : MarkupExtension, IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var shown = value switch
        {
            bool flag => flag,
            string text => text.Length > 0,
            int number => number > 0,
            null => false,
            _ => true
        };
        if (parameter is string expected && value is not null) shown = expected.Split(',').Contains(value.ToString());
        return shown != Invert ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

/// <summary>Логическое «не» — для пары переключателей над одним флагом.</summary>
public sealed class NotConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}
