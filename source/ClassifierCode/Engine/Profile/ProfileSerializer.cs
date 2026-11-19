using System.Collections;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace ClassifierCode.Engine.Profile;

/// <summary>Профиль не прочитался: битый JSON или профиль из более новой версии плагина.</summary>
public sealed class ProfileReadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>JSON профиля: camelCase, пустые и null-поля не пишутся, чтобы файл читался глазами.</summary>
public static class ProfileSerializer
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new CompactResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.Indented
    };

    private const string ReadError = "Не удалось прочитать профиль: ";

    public static ClassifierProfile Read(string json)
    {
        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new ProfileReadException(ReadError + exception.Message, exception);
        }

        var schemaToken = root["schemaVersion"];
        if (schemaToken == null || schemaToken.Type != JTokenType.Integer)
            throw new ProfileReadException(ReadError + "это не профиль классификатора (нет числового поля schemaVersion).");
        var schema = schemaToken.Value<int>();
        if (schema > ClassifierProfile.CurrentSchemaVersion)
            throw new ProfileReadException(
                $"Профиль сделан в версии плагина новее текущей (формат {schema}, поддерживается {ClassifierProfile.CurrentSchemaVersion}). Обновите ToadTools.");

        ClassifierProfile profile;
        try
        {
            profile = root.ToObject<ClassifierProfile>(JsonSerializer.Create(Settings)) ?? new ClassifierProfile();
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or InvalidCastException)
        {
            throw new ProfileReadException(ReadError + exception.Message, exception);
        }

        RejectNulls(profile, "профиль", ReadError);
        return profile;
    }

    public static string Write(ClassifierProfile profile) => JsonConvert.SerializeObject(profile, Settings);

    /// <summary>Часть профиля (правило, режим, набор правил) в том же формате, что и файл.</summary>
    public static string WriteFragment(object value) => JsonConvert.SerializeObject(value, Settings);

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
            value = JsonConvert.DeserializeObject<T>(json, StrictSettings) ?? throw new ProfileReadException(error + "пустой текст");
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or InvalidCastException)
        {
            throw new ProfileReadException(error + exception.Message, exception);
        }

        RejectNulls(value, "текст", error);
        return value;
    }

    private static readonly JsonSerializerSettings StrictSettings = new()
    {
        ContractResolver = new CompactResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Error
    };

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

    private sealed class CompactResolver : CamelCasePropertyNamesContractResolver
    {
        public CompactResolver()
        {
            // Ключи словарей — это ключи профиля (имена источников, разделов), их регистр не трогаем.
            NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false };
        }

        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);
            if (typeof(ICollection).IsAssignableFrom(property.PropertyType))
            {
                var getter = property.ValueProvider;
                property.ShouldSerialize = owner => getter?.GetValue(owner) is ICollection { Count: > 0 };
            }
            else if (property.PropertyType == typeof(bool))
            {
                var getter = property.ValueProvider;
                property.ShouldSerialize = owner => getter?.GetValue(owner) is true;
            }

            return property;
        }
    }
}
