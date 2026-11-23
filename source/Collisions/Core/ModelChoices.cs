using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Collisions.Core;

/// <summary>
///     Какую модель ToadFlow человек выбрал для файла, когда по имени нашлось несколько: путь документа → ключ модели.
///     Файл <c>%AppData%\ToadTools\Clash\models.json</c>.
/// </summary>
public sealed class ModelChoices(string path)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToadTools", "Clash", "models.json");

    public string? Get(string documentPath) => Read().TryGetValue(documentPath, out var key) ? key : null;

    public void Set(string documentPath, string modelKey)
    {
        var choices = Read();
        choices[documentPath] = modelKey;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(choices, Json), new UTF8Encoding(false));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // выбор — удобство: не запомнился — спросим ещё раз
        }
    }

    private Dictionary<string, string> Read()
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), Json) is { } stored)
                return new Dictionary<string, string>(stored, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
