using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ToadExporter.Core;

/// <summary>Модель, отмеченная в дереве: путь её папки в источнике, путь модели и форматы.</summary>
public sealed record CheckedModel(string FolderPath, string ModelPath, bool Rvt, bool Nwc);

/// <summary>
///     Файл конфигурации выгрузки: настройки и отмеченные модели. Состояние окна (тема, свёрнутые блоки)
///     в конфигурацию не входит.
/// </summary>
public static class ExportConfig
{
    private const string SettingsKey = "Settings";
    private const string ModelsKey = "Models";

    /// <summary>Перечисления пишутся именем, читаются и именем, и числом (так их писали настройки окна).</summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonDocumentOptions DocumentOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static readonly string[] WindowKeys =
    [
        nameof(ExportSettings.RvtExpanded),
        nameof(ExportSettings.NwcMainExpanded),
        nameof(ExportSettings.NwcContentExpanded),
        nameof(ExportSettings.NwcExtraExpanded)
    ];

    public static void Save(string path, ExportSettings settings, IReadOnlyList<CheckedModel> models)
    {
        var root = new JsonObject
        {
            [SettingsKey] = WithoutWindowState(JsonSerializer.SerializeToNode(settings, Json)!.AsObject()),
            [ModelsKey] = JsonSerializer.SerializeToNode(models, Json)
        };
        File.WriteAllText(path, root.ToJsonString(Json));
    }

    /// <summary>
    ///     Переносит настройки из файла в <paramref name="target" /> и возвращает отмеченные модели. Чего в файле
    ///     нет, получает значение по умолчанию. Файл не конфигурации — <see cref="InvalidDataException" />,
    ///     <paramref name="target" /> при этом не меняется.
    /// </summary>
    public static IReadOnlyList<CheckedModel> Load(string path, ExportSettings target)
    {
        ExportSettings loaded;
        List<CheckedModel> models;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path), null, DocumentOptions) is not JsonObject root
                || root[SettingsKey] is not JsonObject settings
                || root[ModelsKey] is not JsonArray modelArray)
                throw new InvalidDataException("Файл не является конфигурацией ToadExporter.");

            loaded = settings.Deserialize<ExportSettings>(Json) ?? new ExportSettings();
            models = modelArray.Deserialize<List<CheckedModel>>(Json) ?? [];
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException($"Файл не является конфигурацией ToadExporter: {exception.Message}", exception);
        }

        foreach (var property in typeof(ExportSettings).GetProperties())
        {
            if (!property.CanRead || !property.CanWrite || Array.IndexOf(WindowKeys, property.Name) >= 0) continue;
            property.SetValue(target, property.GetValue(loaded));
        }

        return models;
    }

    private static JsonObject WithoutWindowState(JsonObject settings)
    {
        foreach (var key in WindowKeys) settings.Remove(key);
        return settings;
    }
}
