using System.Text.Json.Serialization.Metadata;

namespace ClassifierCode.Engine;

/// <summary>Общие правила JSON движка: профилей и выгрузок.</summary>
internal static class EngineJson
{
    /// <summary>
    ///     <c>null</c> в файле не затирает значение по умолчанию: «"modes": null» оставляет пустой список, а не роняет
    ///     движок дальше.
    /// </summary>
    public static void KeepDefaultsOnNull(JsonTypeInfo type)
    {
        if (type.Kind != JsonTypeInfoKind.Object) return;
        foreach (var property in type.Properties)
        {
            if (property.Set is not { } set || property.PropertyType.IsValueType) continue;
            property.Set = (owner, value) =>
            {
                if (value != null) set(owner, value);
            };
        }
    }
}
