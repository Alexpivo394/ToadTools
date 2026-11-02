using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media.Imaging;

namespace Collisions.Views;

/// <summary>
///     Картинка коллизии для миниатюры строки: PNG декодируется сразу в ширину <see cref="DecodeWidth" /> (а не в полный
///     размер) и замораживается — сотни строк не держат в памяти полноразмерные растры. Не PNG или битый файл — пусто,
///     строка показывает заглушку.
/// </summary>
public sealed class ThumbnailConverter : MarkupExtension, IValueConverter
{
    /// <summary>Пикселей по ширине: миниатюра 96 точек, с запасом на масштаб экрана 200 %.</summary>
    public int DecodeWidth { get; set; } = 192;

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
            return null; // конвертер не должен ронять панель из-за одной картинки
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}
