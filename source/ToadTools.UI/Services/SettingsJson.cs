#nullable enable
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToadTools.UI.Services;

/// <summary>
///     JSON файлов настроек инструментов: с отступами, кириллица как есть. Читает и то, что записали прежние версии
///     плагина (Newtonsoft.Json) или поправили руками: регистр имён не важен, комментарии и лишняя запятая допустимы.
/// </summary>
public static class SettingsJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Объект из текста; пустой текст — <c>null</c>, как и JSON <c>null</c>.</summary>
    /// <exception cref="JsonException">Текст не JSON или не подходит к типу.</exception>
    public static T? Read<T>(string json) =>
        string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, Options);

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
}
