using ClassifierCode.Engine.Library;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Engine.Facts;

/// <summary>
///     Выгрузка одной модели для ИИ-агента и проверялки: ровно те факты, что видит движок в Revit
///     (<see cref="IElementFacts" />), чтобы проверка вне Revit совпадала с «Заполнить».
/// </summary>
public sealed class ExportModel
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Имя модели, как Document.Title (по нему определяется раздел).</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Откуда открыта: путь на сервере или диске.</summary>
    public string Source { get; set; } = string.Empty;

    public string RevitVersion { get; set; } = string.Empty;
    public DateTime ExportedAt { get; set; }
    public bool Workshared { get; set; }

    /// <summary>Категории, элементы которых выгружены. Профиль с другими категориями проверять на этой выгрузке нельзя.</summary>
    public List<string> Categories { get; set; } = [];

    /// <summary>Параметры записи, по которым сняты текущие коды.</summary>
    public List<OutputField> Outputs { get; set; } = [];

    /// <summary>Каким профилем и разделом модель заполнялась (если заполнялась).</summary>
    public ModelBinding? Binding { get; set; }

    public Dictionary<string, ExportType> Types { get; set; } = new();
    public List<ExportElement> Elements { get; set; } = [];
}

public sealed class ExportType
{
    public string Family { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Comments { get; set; } = string.Empty;
    public List<ExportParameter> Parameters { get; set; } = [];
}

public sealed class ExportElement
{
    public long Id { get; set; }
    public string Category { get; set; } = string.Empty;

    /// <summary>Ключ в <see cref="ExportModel.Types" />; null — у элемента нет типа.</summary>
    public string? TypeId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Comments { get; set; } = string.Empty;
    public string SystemText { get; set; } = string.Empty;

    /// <summary>"pipe", "air" или "other".</summary>
    public string Domain { get; set; } = "other";

    public string? Workset { get; set; }
    public long? ParentId { get; set; }
    public long? HostId { get; set; }

    /// <summary>Есть ли у элемента или его типа параметр кода (без него «Заполнить» элемент пропустит).</summary>
    public bool HasCodeParameter { get; set; }

    public string? CurrentCode { get; set; }
    public string? CurrentDescription { get; set; }

    /// <summary>Параметры в порядке ParameterSet, с дублями имён (пустые — только если имя повторяется).</summary>
    public List<ExportParameter> Parameters { get; set; } = [];
}

public sealed class ExportParameter
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    /// <summary>StorageType.String — такие значения видит «первый строковый параметр по шаблону».</summary>
    public bool IsString { get; set; }

    /// <summary>Этот из одноимённых возвращает LookupParameter (он не обязательно первый в ParameterSet).</summary>
    public bool Lookup { get; set; }
}

/// <summary>Параметр в файле — массив ["имя", "значение"] или ["имя", "значение", флаги] (1 — строковый, 2 — победитель LookupParameter среди тёзок): их у элемента десятки.</summary>
public sealed class ExportParameterConverter : Newtonsoft.Json.JsonConverter<ExportParameter>
{
    public override void WriteJson(Newtonsoft.Json.JsonWriter writer, ExportParameter? value, Newtonsoft.Json.JsonSerializer serializer)
    {
        writer.WriteStartArray();
        writer.WriteValue(value!.Name);
        writer.WriteValue(value.Value);
        var flags = (value.IsString ? 1 : 0) | (value.Lookup ? 2 : 0); // 1 — строковый, 2 — его возвращает LookupParameter
        if (flags != 0) writer.WriteValue(flags);
        writer.WriteEndArray();
    }

    public override ExportParameter ReadJson(Newtonsoft.Json.JsonReader reader, Type objectType, ExportParameter? existingValue, bool hasExistingValue,
        Newtonsoft.Json.JsonSerializer serializer)
    {
        var array = Newtonsoft.Json.Linq.JArray.Load(reader);
        if (array.Count < 2) throw new Newtonsoft.Json.JsonSerializationException("Параметр выгрузки: нужно [имя, значение]");
        return new ExportParameter
        {
            Name = string.Intern((string?)array[0] ?? string.Empty), // имён параметров сотни, а элементов — десятки тысяч
            Value = (string?)array[1] ?? string.Empty,
            IsString = array.Count > 2 && ((int)array[2] & 1) != 0,
            Lookup = array.Count > 2 && ((int)array[2] & 2) != 0
        };
    }
}
