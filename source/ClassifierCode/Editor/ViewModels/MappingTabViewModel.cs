using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassifierCode.Engine.Library;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Editor.ViewModels;

public sealed record KeyOption(string? Key, string Title)
{
    public override string ToString() => Title;
}

public sealed partial class MappingRowViewModel(TableRow row, MappingTabViewModel owner) : ObservableObject
{
    public TableRow Source { get; } = row;

    public string Discipline
    {
        get => Source.Discipline;
        set => Set(() => Source.Discipline = value);
    }

    public string Type
    {
        get => Source.Type;
        set => Set(() => Source.Type = value);
    }

    public string Position
    {
        get => Source.Position;
        set => Set(() => Source.Position = value.Trim());
    }

    public string DisciplineTitle => owner.DisciplineTitle(Source.Discipline);
    public string TypeTitle => owner.TypeTitle(Source.Type);
    public string DescriptionPreview => owner.Describe(Source.Position);

    private void Set(Action apply)
    {
        apply();
        OnPropertyChanged(string.Empty);
        owner.RowChanged();
    }
}

public sealed record GapViewModel(string Discipline, string Type, string Title);

public sealed partial class FallbackRowViewModel(string type, List<string> alternatives, MappingTabViewModel owner) : ObservableObject
{
    public string Type { get; } = type;
    public string TypeTitle => owner.TypeTitle(Type);

    /// <summary>Замены по названиям типов через «;» (в названиях бывают запятые); можно вписать и ключ.</summary>
    public string AlternativesText
    {
        get => string.Join("; ", alternatives.Select(owner.TypeTitle));
        set
        {
            alternatives.Clear();
            alternatives.AddRange(value.Split([';'], StringSplitOptions.RemoveEmptyEntries).Select(s => owner.TypeKey(s.Trim())).Where(s => s.Length > 0));
            OnPropertyChanged();
            owner.RowChanged();
        }
    }
}

/// <summary>Вкладка «Сопоставление»: строки таблицы «раздел + тип → позиция», замены типов, возможные пробелы.</summary>
public sealed partial class MappingTabViewModel : ObservableObject
{
    private readonly ProfileEditorViewModel _owner;

    [ObservableProperty] private ModeDef? _selectedMode;
    [ObservableProperty] private KeyOption? _disciplineFilter;
    [ObservableProperty] private IReadOnlyList<MappingRowViewModel> _rows = [];
    [ObservableProperty] private IReadOnlyList<GapViewModel> _gaps = [];
    [ObservableProperty] private IReadOnlyList<FallbackRowViewModel> _fallbacks = [];
    [ObservableProperty] private IReadOnlyList<KeyOption> _disciplineFilters = [];
    [ObservableProperty] private string? _sharedNote;

    public MappingTabViewModel(ProfileEditorViewModel owner)
    {
        _owner = owner;
        owner.Recompiled += (_, _) => UpdateGaps();
        SelectedMode = Modes.FirstOrDefault();
    }

    private ClassifierProfile P => _owner.Profile;
    public IReadOnlyList<ModeDef> Modes => P.Modes;
    public IReadOnlyList<KeyTitle> DisciplineKeys => P.Disciplines.Prepend(new KeyTitle { Key = "*", Title = "любой раздел" }).ToList();
    public IReadOnlyList<KeyTitle> TypeKeys => P.Types;

    private TableDef? Table => SelectedMode != null && P.Tables.TryGetValue(SelectedMode.Table, out var table) ? table : null;

    partial void OnSelectedModeChanged(ModeDef? value)
    {
        var filters = new List<KeyOption> { new(null, "все разделы") };
        if (value != null)
            filters.AddRange(ProfileAnalysis.ReachableDisciplines(P, value).Concat(Table?.Rows.Select(r => r.Discipline) ?? [])
                .Distinct().OrderBy(DisciplineTitle).Select(d => new KeyOption(d, DisciplineTitle(d))));
        DisciplineFilters = filters;
        DisciplineFilter = filters[0];

        var users = P.Modes.Where(m => value != null && m.Table == value.Table && m != value).Select(m => m.Title).ToList();
        SharedNote = users.Count > 0 ? $"Таблица «{value!.Table}» общая с режимами: {string.Join(", ", users)} — правка меняет и их." : null;

        Fallbacks = Table?.Fallbacks.Select(f => new FallbackRowViewModel(f.Key, f.Value, this)).ToList() ?? [];
        ReloadRows();
    }

    partial void OnDisciplineFilterChanged(KeyOption? value) => ReloadRows();

    private void ReloadRows()
    {
        var table = Table;
        Rows = table == null
            ? []
            : table.Rows.Where(r => DisciplineFilter?.Key == null || r.Discipline == DisciplineFilter.Key)
                .Select(r => new MappingRowViewModel(r, this)).ToList();
        UpdateGaps();
    }

    private void UpdateGaps()
    {
        Gaps = SelectedMode == null
            ? []
            : ProfileAnalysis.Gaps(P, SelectedMode)
                .Where(g => DisciplineFilter?.Key == null || g.Discipline == DisciplineFilter.Key)
                .Select(g => new GapViewModel(g.Discipline, g.Type, DisciplineTitle(g.Discipline) + " + " + TypeTitle(g.Type))).ToList();
    }

    internal void RowChanged()
    {
        _owner.MarkChanged();
        UpdateGaps();
    }

    public string DisciplineTitle(string key) => key == "*" ? "любой раздел" : P.Disciplines.FirstOrDefault(d => d.Key == key)?.Title ?? key;
    public string TypeTitle(string key) => P.Types.FirstOrDefault(t => t.Key == key)?.Title ?? key;

    /// <summary>Ключ типа по названию или ключу.</summary>
    public string TypeKey(string titleOrKey) =>
        P.Types.FirstOrDefault(t => t.Title == titleOrKey)?.Key ?? P.Types.FirstOrDefault(t => t.Key == titleOrKey)?.Key ?? titleOrKey;
    public string Describe(string code) => P.Positions.FirstOrDefault(p => p.Code == code)?.Description ?? "нет в классификаторе";

    /// <summary>Коды позиций, начинающиеся с введённого текста (или содержащие его в описании).</summary>
    public IReadOnlyList<string> Suggest(string prefix) =>
        P.Positions.Where(p => p.Code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                               p.Description.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(p => p.Code).Take(30).ToList();

    public IReadOnlyList<string> AllCodes => P.Positions.Select(p => p.Code).ToList();

    [RelayCommand]
    private void AddRow()
    {
        var table = Table;
        if (table == null) return;
        var row = new TableRow { Discipline = DisciplineFilter?.Key ?? P.Disciplines.FirstOrDefault()?.Key ?? "*", Type = P.Types.FirstOrDefault()?.Key ?? string.Empty };
        table.Rows.Add(row);
        ReloadRows();
        RowChanged();
    }

    [RelayCommand]
    private void AddFromGap(GapViewModel? gap)
    {
        var table = Table;
        if (gap == null || table == null) return;
        table.Rows.Add(new TableRow { Discipline = gap.Discipline, Type = gap.Type, Position = string.Empty });
        ReloadRows();
        RowChanged();
    }

    [RelayCommand]
    private void DeleteRow(MappingRowViewModel? row)
    {
        if (row == null) return;
        Table?.Rows.Remove(row.Source);
        ReloadRows();
        RowChanged();
    }

    [RelayCommand]
    private void AddFallback(KeyTitle? type)
    {
        var table = Table;
        if (type == null || table == null || table.Fallbacks.ContainsKey(type.Key)) return;
        table.Fallbacks[type.Key] = [];
        Fallbacks = table.Fallbacks.Select(f => new FallbackRowViewModel(f.Key, f.Value, this)).ToList();
        RowChanged();
    }

    [RelayCommand]
    private void DeleteFallback(FallbackRowViewModel? row)
    {
        var table = Table;
        if (row == null || table == null) return;
        table.Fallbacks.Remove(row.Type);
        Fallbacks = table.Fallbacks.Select(f => new FallbackRowViewModel(f.Key, f.Value, this)).ToList();
        RowChanged();
    }
}
