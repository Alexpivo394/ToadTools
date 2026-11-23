using System.Collections;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ClassifierCode.Engine.Profile;

/// <summary>Профиль не прочитался: битый JSON или профиль из более новой версии плагина.</summary>
public sealed class ProfileReadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>JSON профиля: camelCase, пустые и null-поля не пишутся, чтобы файл читался глазами.</summary>
public static class ProfileSerializer
{
    private static readonly JsonSerializerOptions Options = Create(JsonUnmappedMemberHandling.Skip);

    /// <summary>Профили пишут и руками, и ИИ-агент: комментарии и лишняя запятая — не ошибка.</summary>
    private static readonly JsonDocumentOptions DocumentOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private const string ReadError = "Не удалось прочитать профиль: ";

    public static ClassifierProfile Read(string json)
    {
        int schema;
        try
        {
            using var document = JsonDocument.Parse(json, DocumentOptions);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new ProfileReadException(ReadError + "это не профиль классификатора (ожидался объект JSON).");
            if (!root.TryGetProperty("schemaVersion", out var schemaToken) || schemaToken.ValueKind != JsonValueKind.Number || !schemaToken.TryGetInt32(out schema))
                throw new ProfileReadException(ReadError + "это не профиль классификатора (нет числового поля schemaVersion).");
        }
        catch (JsonException exception)
        {
            throw new ProfileReadException(ReadError + exception.Message, exception);
        }

        if (schema > ClassifierProfile.CurrentSchemaVersion)
            throw new ProfileReadException(
                $"Профиль сделан в версии плагина новее текущей (формат {schema}, поддерживается {ClassifierProfile.CurrentSchemaVersion}). Обновите ToadTools.");

        ClassifierProfile profile;
        try
        {
            profile = JsonSerializer.Deserialize<ClassifierProfile>(json, Options) ?? new ClassifierProfile();
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            throw new ProfileReadException(ReadError + exception.Message, exception);
        }

        RejectNulls(profile, "профиль", ReadError);
        return profile;
    }

    public static string Write(ClassifierProfile profile) => JsonSerializer.Serialize(profile, Options);

    /// <summary>Часть профиля (правило, режим, набор правил) в том же формате, что и файл.</summary>
    public static string WriteFragment(object value) => JsonSerializer.Serialize(value, value.GetType(), Options);

    /// <summary>
    ///     Часть профиля, набранная руками (вкладка «Логика»). Неизвестное поле — ошибка: опечатка «fallthrought»
    ///     иначе молча превратилась бы в значение по умолчанию.
    /// </summary>
    public static T ReadFragment<T>(string json) where T : class
    {
        const string error = "Не удалось прочитать: ";
        T value;
        try
        {
            value = (string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json, StrictOptions)) ?? throw new ProfileReadException(error + "пустой текст");
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            throw new ProfileReadException(error + exception.Message, exception);
        }

        RejectNulls(value, "текст", error);
        return value;
    }

    private static readonly JsonSerializerOptions StrictOptions = Create(JsonUnmappedMemberHandling.Disallow);

    private static bool IsReadFailure(Exception exception) =>
        exception is JsonException or ArgumentException or FormatException or InvalidCastException or InvalidOperationException or NotSupportedException;

    private static JsonSerializerOptions Create(JsonUnmappedMemberHandling unmapped) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = unmapped,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // Ключи словарей — это ключи профиля (имена источников, разделов): политики имён для них нет, регистр не трогаем.
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { EngineJson.KeepDefaultsOnNull, Compact } }
    };

    /// <summary>Пустые списки и словари и <c>false</c> в файл не пишутся.</summary>
    private static void Compact(JsonTypeInfo type)
    {
        if (type.Kind != JsonTypeInfoKind.Object) return;
        foreach (var property in type.Properties)
        {
            if (typeof(ICollection).IsAssignableFrom(property.PropertyType))
                property.ShouldSerialize = (_, value) => value is ICollection { Count: > 0 };
            else if (property.PropertyType == typeof(bool))
                property.ShouldSerialize = (_, value) => value is true;
        }
    }

    /// <summary>Пустые элементы списков и словарей («modes»: [null]) — ошибка чтения, а не падение дальше.</summary>
    private static void RejectNulls(object value, string path, string error)
    {
        foreach (var property in value.GetType().GetProperties())
        {
            if (property.GetIndexParameters().Length > 0) continue;
            var item = property.GetValue(value);
            var at = path + " › " + char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1);
            switch (item)
            {
                case null or string or Guid:
                    continue;
                case IDictionary dictionary:
                    foreach (DictionaryEntry entry in dictionary)
                        Check(entry.Value, at + " › " + entry.Key, error);
                    break;
                case IList list:
                    for (var i = 0; i < list.Count; i++) Check(list[i], at + " › " + (i + 1), error);
                    break;
                default:
                    if (IsProfileType(item.GetType())) RejectNulls(item, at, error);
                    break;
            }
        }
    }

    private static void Check(object? item, string path, string error)
    {
        if (item == null) throw new ProfileReadException(error + "пустой элемент в «" + path + "».");
        if (item is IList list)
            for (var i = 0; i < list.Count; i++) Check(list[i], path + " › " + (i + 1), error);
        else if (IsProfileType(item.GetType())) RejectNulls(item, path, error);
    }

    private static bool IsProfileType(Type type) => type.Namespace == typeof(ClassifierProfile).Namespace || type.Namespace?.StartsWith("ClassifierCode.Editor") == true;

    /// <summary>Глубокая копия через JSON — для рабочей копии в редакторе.</summary>
    public static ClassifierProfile Clone(ClassifierProfile profile) => Read(Write(profile));
}
