using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Editor.ViewModels;

public sealed partial class ModeItemViewModel(ModeDef source, Action changed) : ObservableObject
{
    public ModeDef Source { get; } = source;
    public string Key => Source.Key;

    public string Title { get => Source.Title; set => Set(() => Source.Title = value); }
    public string Hint { get => Source.Hint; set => Set(() => Source.Hint = value); }
    public string ReportMode { get => Source.ReportMode; set => Set(() => Source.ReportMode = value); }
    public bool UnconnectedAsAir { get => Source.UnconnectedAsAir; set => Set(() => Source.UnconnectedAsAir = value); }
    public string TypeRules { get => Source.TypeRules; set => Set(() => Source.TypeRules = value); }
    public string Table { get => Source.Table; set => Set(() => Source.Table = value); }

    /// <summary>Пустая строка — «не классифицировано».</summary>
    public string DefaultDiscipline
    {
        get => Source.DefaultDiscipline ?? string.Empty;
        set => Set(() => Source.DefaultDiscipline = string.IsNullOrEmpty(value) ? null : value);
    }

    /// <summary>Шифры в имени файла через запятую.</summary>
    public string FileTokensText
    {
        get => string.Join(", ", Source.FileTokens);
        set => Set(() => Source.FileTokens = value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList());
    }

    private void Set(Action apply)
    {
        apply();
        OnPropertyChanged(string.Empty);
        changed();
    }
}

/// <summary>Вкладка «Режимы»: разделы модели в окне запуска.</summary>
public sealed partial class ModesTabViewModel : ObservableObject
{
    private readonly ProfileEditorViewModel _owner;

    [ObservableProperty] private ModeItemViewModel? _selectedMode;

    public ModesTabViewModel(ProfileEditorViewModel owner)
    {
        _owner = owner;
        Reload();
    }

    private ClassifierProfile P => _owner.Profile;
    public ObservableCollection<ModeItemViewModel> Modes { get; } = [];
    public IReadOnlyList<string> RuleSetNames => P.RuleSets.Keys.OrderBy(k => k).ToList();
    public IReadOnlyList<string> TableNames => P.Tables.Keys.OrderBy(k => k).ToList();

    public IReadOnlyList<KeyOption> DisciplineOptions =>
        P.Disciplines.Select(d => new KeyOption(d.Key, d.Title)).Prepend(new KeyOption(string.Empty, "— не классифицировано —")).ToList();

    public void Reload()
    {
        var key = SelectedMode?.Key;
        Modes.Clear();
        foreach (var mode in P.Modes) Modes.Add(new ModeItemViewModel(mode, _owner.MarkChanged));
        SelectedMode = Modes.FirstOrDefault(m => m.Key == key) ?? Modes.FirstOrDefault();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Новый режим — копия выбранного (с его правилами раздела).</summary>
    [RelayCommand]
    private void Add()
    {
        var source = SelectedMode?.Source ?? P.Modes.FirstOrDefault();
        var copy = source != null
            ? ProfileSerializer.ReadFragment<ModeDef>(ProfileSerializer.WriteFragment(source))
            : new ModeDef { Title = "Новый режим" };
        var baseKey = source?.Key ?? "mode";
        var n = 2;
        while (P.Modes.Any(m => m.Key == baseKey + n)) n++;
        copy.Key = baseKey + n;
        copy.Title = (source?.Title ?? "Режим") + " (копия)";
        P.Modes.Add(copy);
        Reload();
        SelectedMode = Modes.Last();
        _owner.MarkChanged();
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedMode == null || P.Modes.Count <= 1) return;
        if (_owner.Dialogs.Ask("Удалить режим", $"Удалить режим «{SelectedMode.Title}»?") != true) return;
        P.Modes.Remove(SelectedMode.Source);
        Reload();
        _owner.MarkChanged();
    }

    [RelayCommand]
    private void MoveUp() => Move(-1);

    [RelayCommand]
    private void MoveDown() => Move(1);

    private void Move(int delta)
    {
        if (SelectedMode == null) return;
        var index = P.Modes.IndexOf(SelectedMode.Source);
        var target = index + delta;
        if (target < 0 || target >= P.Modes.Count) return;
        P.Modes.RemoveAt(index);
        P.Modes.Insert(target, SelectedMode.Source);
        Reload();
        _owner.MarkChanged();
    }
}
