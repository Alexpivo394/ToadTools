namespace WorksetCheck.Models;

/// <summary>Имена рабочих наборов, по которым идёт проверка. Пустое значение — эта проверка пропускается.</summary>
public sealed class WorksetRules
{
    public WorksetRules(string? linksPrefix, string? gridsWorkset, string? openingsWorkset)
    {
        LinksPrefix = linksPrefix?.Trim() ?? string.Empty;
        GridsWorkset = gridsWorkset?.Trim() ?? string.Empty;
        OpeningsWorkset = openingsWorkset?.Trim() ?? string.Empty;
    }

    /// <summary>Начало имён РН связей, например «#Связи».</summary>
    public string LinksPrefix { get; }

    /// <summary>Полное имя РН осей и уровней.</summary>
    public string GridsWorkset { get; }

    /// <summary>Полное имя РН заданий на отверстия.</summary>
    public string OpeningsWorkset { get; }

    public bool ChecksLinks => LinksPrefix.Length > 0;
    public bool ChecksGrids => GridsWorkset.Length > 0;
    public bool ChecksOpenings => OpeningsWorkset.Length > 0;

    public bool IsGridsWorkset(string name) =>
        ChecksGrids && string.Equals(name, GridsWorkset, StringComparison.OrdinalIgnoreCase);

    public bool IsOpeningsWorkset(string name) =>
        ChecksOpenings && string.Equals(name, OpeningsWorkset, StringComparison.OrdinalIgnoreCase);

    /// <summary>РН связей — по префиксу, но РН осей и отверстий им не считаются, даже если начинаются так же.</summary>
    public bool IsLinksWorkset(string name) =>
        ChecksLinks &&
        !IsGridsWorkset(name) &&
        !IsOpeningsWorkset(name) &&
        name.StartsWith(LinksPrefix, StringComparison.OrdinalIgnoreCase);
}
