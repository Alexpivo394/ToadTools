using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToadTools.Connection;

/// <summary>
///     Строка, на месте которой сервер прислал число или логическое значение, читается их текстом — так читал
///     Newtonsoft.Json; ответ с таким полем не отвергается целиком.
/// </summary>
public sealed class LenientStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString(),
        JsonTokenType.Number => Encoding.UTF8.GetString(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan.ToArray()),
        JsonTokenType.True => "True",
        JsonTokenType.False => "False",
        _ => throw new JsonException("Ожидалась строка")
    };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}
