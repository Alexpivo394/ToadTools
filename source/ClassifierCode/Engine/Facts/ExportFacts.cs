using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ClassifierCode.Engine.Facts;

/// <summary>Факты элемента из выгрузки — то же, что FactsBuilder даёт в Revit.</summary>
public sealed class ExportFacts : IElementFacts
{
    private readonly ExportElement _element;
    private readonly ExportType? _type;
    private readonly ExportModel _model;
    private readonly IReadOnlyDictionary<long, ExportFacts> _all;

    private ExportFacts(ExportModel model, ExportElement element, IReadOnlyDictionary<long, ExportFacts> all)
    {
        _model = model;
        _element = element;
        _all = all;
        _type = element.TypeId != null && model.Types.TryGetValue(element.TypeId, out var type) ? type : null;
    }

    /// <summary>Факты всех элементов модели; родители и хосты — по Id внутри модели.</summary>
    public static IReadOnlyList<IElementFacts> Load(ExportModel model)
    {
        var all = new Dictionary<long, ExportFacts>();
        var list = new List<IElementFacts>(model.Elements.Count);
        foreach (var element in model.Elements)
        {
            var facts = new ExportFacts(model, element, all);
            all[element.Id] = facts;
            list.Add(facts);
        }

        return list;
    }

    public ExportElement Element => _element;

    public long Id => _element.Id;
    public string Category => _element.Category;
    public NetworkDomain Domain => _element.Domain switch { "pipe" => NetworkDomain.Pipe, "air" => NetworkDomain.Air, _ => NetworkDomain.Other };
    public bool HasType => _type != null;
    public string InstanceName => _element.Name;
    public string FamilyName => _type?.Family ?? string.Empty;
    public string TypeName => _type?.Name ?? string.Empty;
    public string InstanceParameter(string name) => First(_element.Parameters, name);
    public string TypeParameter(string name) => _type == null ? string.Empty : First(_type.Parameters, name);
    public IEnumerable<string> InstanceStringValues() => StringValues(_element.Parameters);
    public IEnumerable<string> TypeStringValues() => _type == null ? [] : StringValues(_type.Parameters);
    public string InstanceComments => _element.Comments;
    public string TypeComments => _type?.Comments ?? string.Empty;
    public string SystemText => _element.SystemText;
    public string? Workset => _element.Workset;
    public string DocumentTitle => _model.Model;
    public IElementFacts? Parent => _element.ParentId is { } id && _all.TryGetValue(id, out var parent) ? parent : null;
    public IElementFacts? Host => _element.HostId is { } id && _all.TryGetValue(id, out var host) ? host : null;

    /// <summary>Как LookupParameter: среди тёзок — помеченный выгрузкой, иначе первый.</summary>
    private static string First(List<ExportParameter> parameters, string name)
    {
        string? first = null;
        foreach (var parameter in parameters)
        {
            if (parameter.Name != name) continue;
            if (parameter.Lookup) return parameter.Value;
            first ??= parameter.Value;
        }

        return first ?? string.Empty;
    }

    private static IEnumerable<string> StringValues(List<ExportParameter> parameters) =>
        parameters.Where(p => p.IsString && p.Value.Length > 0).Select(p => p.Value);

    public static string DomainKey(NetworkDomain domain) => domain switch
    {
        NetworkDomain.Pipe => "pipe",
        NetworkDomain.Air => "air",
        _ => "other"
    };
}

public static class ExportJson
{
    internal static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver { NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false } },
        NullValueHandling = NullValueHandling.Ignore,
        Converters = [new ExportParameterConverter()]
    };
}

/// <summary>Чтение выгрузки потоком (файлы бывают в сотни мегабайт).</summary>
public static class ExportReader
{
    /// <summary>Шапка выгрузки без типов и элементов — им проверяются категории, не разбирая сотни мегабайт.</summary>
    public static ExportModel ReadHeader(string path)
    {
        using var stream = File.OpenText(path);
        using var reader = new JsonTextReader(stream);
        if (!reader.Read() || reader.TokenType != JsonToken.StartObject) throw NotExport(path);
        var header = new Newtonsoft.Json.Linq.JObject();
        while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
        {
            var name = (string)reader.Value!;
            reader.Read();
            if (name is "types" or "elements") reader.Skip();
            else header[name] = Newtonsoft.Json.Linq.JToken.ReadFrom(reader);
        }

        return Check(header.ToObject<ExportModel>(JsonSerializer.Create(ExportJson.Settings)), path);
    }

    public static ExportModel Read(string path)
    {
        using var stream = File.OpenText(path);
        using var reader = new JsonTextReader(stream);
        return Check(JsonSerializer.Create(ExportJson.Settings).Deserialize<ExportModel>(reader), path);
    }

    private static ExportModel Check(ExportModel? model, string path)
    {
        if (model == null || string.IsNullOrEmpty(model.Model)) throw NotExport(path);
        if (model.SchemaVersion > ExportModel.CurrentSchemaVersion)
            throw new InvalidDataException($"Выгрузка сделана более новой версией плагина (формат {model.SchemaVersion}): {path}");
        return model;
    }

    private static InvalidDataException NotExport(string path) =>
        new($"Файл не выгрузка модели (нет имени модели): {path}. Выгрузки — exports\\*.export.json");
}

public static class ExportWriter
{
    /// <summary>Пишет во временный файл и переименовывает — при сбое битого файла не остаётся.</summary>
    public static void Write(ExportModel model, string path)
    {
        var temp = path + ".tmp";
        using (var stream = new StreamWriter(temp, false, new UTF8Encoding(false)))
        using (var writer = new JsonTextWriter(stream) { Formatting = Formatting.None })
            JsonSerializer.Create(ExportJson.Settings).Serialize(writer, model);
        if (File.Exists(path)) File.Delete(path);
        File.Move(temp, path);
    }
}

public static class ExportNames
{
    public const string Extension = ".export.json";

    /// <summary>
    ///     Имя файла выгрузки. Одноимённые модели из разных папок сервера получают суффикс по пути-источнику — он одинаков
    ///     при каждой выгрузке, поэтому довыгрузка одной модели пишет в её же файл, а не поверх другой.
    /// </summary>
    /// <param name="folder">Папка выгрузок: файл с этим именем от другой модели занимает имя.</param>
    /// <param name="taken">Имена, уже выданные в этом запуске.</param>
    public static string FileName(string model, string source, string folder, ISet<string> taken)
    {
        var safe = Safe(model);
        var plain = safe + Extension;
        if (!taken.Contains(plain) && BelongsTo(Path.Combine(folder, plain), source))
        {
            taken.Add(plain);
            return plain;
        }

        var name = $"{safe} ({Hash(source)}){Extension}";
        for (var n = 2; !taken.Add(name); n++) name = $"{safe} ({Hash(source)}-{n}){Extension}";
        return name;
    }

    /// <summary>Только для одного запуска без папки (тесты, лаборатория).</summary>
    public static string FileName(string model, ISet<string> taken)
    {
        var safe = Safe(model);
        var name = safe + Extension;
        for (var n = 2; !taken.Add(name); n++) name = $"{safe} ({n}){Extension}";
        return name;
    }

    /// <summary>Имя модели по имени файла выгрузки (без суффикса).</summary>
    public static string ModelOf(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - Extension.Length);
        var match = System.Text.RegularExpressions.Regex.Match(name, @"^(.*) \((\d+|[0-9a-f]{8}(-\d+)?)\)$");
        return match.Success ? match.Groups[1].Value : name;
    }

    private static string Safe(string model)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(model.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    /// <summary>Файла нет, он битый или выгружен из того же источника — имя свободно.</summary>
    private static bool BelongsTo(string path, string source)
    {
        if (!File.Exists(path)) return true;
        try
        {
            return string.Equals(ExportReader.ReadHeader(path).Source, source, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static string Hash(string source)
    {
        using var sha = System.Security.Cryptography.SHA1.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(source.ToLowerInvariant()));
        return string.Concat(bytes.Take(4).Select(b => b.ToString("x2")));
    }
}
