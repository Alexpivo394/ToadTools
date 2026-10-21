namespace WorksetCheck.Models;

/// <summary>Имена рабочих наборов, по которым идёт проверка. Пустое значение — эта проверка пропускается.</summary>
public sealed class WorksetRules
{
    /// <summary>Начало имени РН отдельной связи: у каждой связи Revit свой РН «#Связь имя_файла».</summary>
    public const string LinkWorksetPrefix = "#Связь ";

    public WorksetRules(string? cadWorkset, string? gridsWorkset, string? openingsWorkset)
    {
        CadWorkset = cadWorkset?.Trim() ?? string.Empty;
        GridsWorkset = gridsWorkset?.Trim() ?? string.Empty;
        OpeningsWorkset = openingsWorkset?.Trim() ?? string.Empty;
    }

    /// <summary>Полное имя РН для всех CAD-подложек, например «#Связи_DWG».</summary>
    public string CadWorkset { get; }

    /// <summary>Полное имя РН осей и уровней.</summary>
    public string GridsWorkset { get; }

    /// <summary>Полное имя РН заданий на отверстия.</summary>
    public string OpeningsWorkset { get; }

    public bool ChecksCad => CadWorkset.Length > 0;
    public bool ChecksGrids => GridsWorkset.Length > 0;
    public bool ChecksOpenings => OpeningsWorkset.Length > 0;

    public bool IsCadWorkset(string name) =>
        ChecksCad && string.Equals(name, CadWorkset, StringComparison.OrdinalIgnoreCase);

    public bool IsGridsWorkset(string name) =>
        ChecksGrids && string.Equals(name, GridsWorkset, StringComparison.OrdinalIgnoreCase);

    public bool IsOpeningsWorkset(string name) =>
        ChecksOpenings && string.Equals(name, OpeningsWorkset, StringComparison.OrdinalIgnoreCase);

    /// <summary>РН, в котором должна лежать связь Revit с этим именем файла (без «.rvt»).</summary>
    public static string LinkWorkset(string fileName) => LinkWorksetPrefix + fileName;

    public static bool IsLinkFileWorkset(string name) =>
        name.StartsWith(LinkWorksetPrefix, StringComparison.OrdinalIgnoreCase);
}
