using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ToadTools.Connection;

/// <summary>
///     Перечисления строкой; значение, которого плагин ещё не знает (сервер новее), читается как <c>Unknown</c>, если оно
///     есть в перечислении, — список не падает целиком из-за одного нового статуса.
/// </summary>
public sealed class TolerantEnumConverter : StringEnumConverter
{
    public const string UnknownName = "Unknown";

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        try
        {
            return base.ReadJson(reader, objectType, existingValue, serializer);
        }
        catch (JsonSerializationException)
        {
            var type = Nullable.GetUnderlyingType(objectType) ?? objectType;
            if (Enum.IsDefined(type, UnknownName)) return Enum.Parse(type, UnknownName);
            throw;
        }
    }
}
