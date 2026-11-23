using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToadTools.Connection;

/// <summary>
///     Перечисления строкой; значение, которого плагин ещё не знает (сервер новее), читается как <c>Unknown</c>, если оно
///     есть в перечислении, — список не падает целиком из-за одного нового статуса.
/// </summary>
public sealed class TolerantEnumConverter : JsonConverterFactory
{
    public const string UnknownName = "Unknown";

    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert))!;

    private sealed class Converter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String when Enum.TryParse<T>(reader.GetString(), true, out var named):
                    return named;
                case JsonTokenType.Number when reader.TryGetInt64(out var number):
                    return (T)Enum.ToObject(typeof(T), number);
            }

            if (Enum.TryParse<T>(UnknownName, out var unknown)) return unknown;
            throw new JsonException($"Значение не подходит к перечислению {typeof(T).Name}");
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }
}
