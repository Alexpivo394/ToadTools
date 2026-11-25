using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Collisions.Core;

/// <summary>
///     Какую часть здания человек выбрал в списке коллизий модели: ключ модели → id части. Помнится между сеансами Revit,
///     файл <c>%AppData%\ToadTools\collisions.json</c>.
/// </summary>
public sealed class PartMemory(string path)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToadTools", "collisions.json");

    /// <summary>Запомненная часть модели; null — не выбрана (или файл не читается).</summary>
    public Guid? Get(string modelKey) => Read().TryGetValue(modelKey, out var part) ? part : null;

    /// <summary>Запомнить часть модели; null — «Все части», запись убирается.</summary>
    public void Set(string modelKey, Guid? part)
    {
        var stored = Read();
        if (part is { } value) stored[modelKey] = value;
        else if (!stored.Remove(modelKey)) return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(stored, Json), new UTF8Encoding(false));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // выбор — удобство: не запомнился — в следующий раз будут все части
        }
    }

    private Dictionary<string, Guid> Read()
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Dictionary<string, Guid>>(File.ReadAllText(path), Json) is { } stored)
                return new Dictionary<string, Guid>(stored, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
    }
}
