using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

    private static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
    {
        Converters = [new Newtonsoft.Json.Converters.StringEnumConverter()]
    });

    private static readonly string[] WindowKeys =
    [
        nameof(ExportSettings.RvtExpanded),
        nameof(ExportSettings.NwcMainExpanded),
        nameof(ExportSettings.NwcContentExpanded),
        nameof(ExportSettings.NwcExtraExpanded),
        nameof(ExportSettings.DarkTheme)
    ];

    public static void Save(string path, ExportSettings settings, IReadOnlyList<CheckedModel> models)
    {
        var root = new JObject
        {
            [SettingsKey] = WithoutWindowState(JObject.FromObject(settings, Serializer)),
            [ModelsKey] = JArray.FromObject(models, Serializer)
        };
        File.WriteAllText(path, root.ToString(Formatting.Indented));
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
            if (JToken.Parse(File.ReadAllText(path)) is not JObject root
                || root[SettingsKey] is not JObject settings
                || root[ModelsKey] is not JArray modelArray)
                throw new InvalidDataException("Файл не является конфигурацией ToadExporter.");

            loaded = settings.ToObject<ExportSettings>() ?? new ExportSettings();
            models = modelArray.ToObject<List<CheckedModel>>() ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Файл не является конфигурацией ToadExporter: {exception.Message}", exception);
        }

        JsonConvert.PopulateObject(WithoutWindowState(JObject.FromObject(loaded)).ToString(), target);
        return models;
    }

    private static JObject WithoutWindowState(JObject settings)
    {
        foreach (var key in WindowKeys) settings.Remove(key);
        return settings;
    }
}
