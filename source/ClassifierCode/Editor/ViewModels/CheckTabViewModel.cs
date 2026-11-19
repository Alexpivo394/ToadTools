using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Matching;
using ClassifierCode.Engine.Profile;
using ClassifierCode.Engine.Running;
using ClassifierCode.Models;

namespace ClassifierCode.Editor.ViewModels;

public sealed record FactRow(string Name, string Value);

/// <summary>Элемент на проверке: факты, трасса и итог.</summary>
public sealed record ElementCheck(long Id, string Label, IReadOnlyList<FactRow> Facts, IReadOnlyList<TraceStep> Trace, string ResultText, EngineStatus Status);

public sealed record DryRunRow(long Id, string Label, string Current, string New, EngineStatus Status, string Reason)
{
    /// <summary>Записанный код изменится.</summary>
    public bool IsChange => Status == EngineStatus.Classified && New != Current;

    public string StatusText => Status switch
    {
        EngineStatus.Classified => IsChange ? "изменится" : "без изменений",
        EngineStatus.Skipped => "пропущен",
        _ => "не классифицирован"
    };
}

public sealed record CodeGroup(string Code, string Description, int Count);

/// <summary>
///     Вкладка «Проверка»: почему элемент получил такой код и что изменится при заполнении. Работает на несохранённой
///     рабочей копии — правка словаря сразу видна здесь.
/// </summary>
public sealed partial class CheckTabViewModel : ObservableObject
{
    private readonly ProfileEditorViewModel _owner;
    private List<long> _inspectedIds = [];

    [ObservableProperty] private ModeDef? _selectedMode;
    [ObservableProperty] private RunScope _scope = RunScope.Model;
    [ObservableProperty] private IReadOnlyList<ElementCheck> _inspected = [];
    [ObservableProperty] private IReadOnlyList<DryRunRow> _rows = [];
    [ObservableProperty] private IReadOnlyList<DryRunRow> _visibleRows = [];
    [ObservableProperty] private IReadOnlyList<CodeGroup> _groups = [];
    [ObservableProperty] private DryRunRow? _selectedRow;
    [ObservableProperty] private bool _onlyChanges;
    [ObservableProperty] private bool _onlyUnclassified;
    [ObservableProperty] private bool _onlySkipped;
    [ObservableProperty] private string _summary = string.Empty;

    public CheckTabViewModel(ProfileEditorViewModel owner)
    {
        _owner = owner;
        SelectedMode = owner.Profile.Modes.FirstOrDefault();
        owner.Recompiled += (_, _) => Refresh();
    }

    public IReadOnlyList<ModeDef> Modes => _owner.Profile.Modes;
    public bool HasSelection => _owner.Host.CurrentSelection().Count > 0;
    public string SelectionLabel => $"Проверить выделенные ({_owner.Host.CurrentSelection().Count})";

    partial void OnSelectedModeChanged(ModeDef? value) => Refresh();
    partial void OnOnlyChangesChanged(bool value) => Filter();
    partial void OnOnlyUnclassifiedChanged(bool value) => Filter();
    partial void OnOnlySkippedChanged(bool value) => Filter();

    partial void OnSelectedRowChanged(DryRunRow? value)
    {
        if (value != null) Inspect([value.Id]);
    }

    /// <summary>Выбрать режим по ключу (из окна запуска).</summary>
    public void SelectMode(string? key) => SelectedMode = Modes.FirstOrDefault(m => m.Key == key) ?? SelectedMode;

    private ClassificationEngine? CreateEngine(bool tell)
    {
        var compiled = _owner.Compiled;
        if (compiled == null || SelectedMode == null || !compiled.Modes.ContainsKey(SelectedMode.Key))
        {
            if (tell) _owner.Dialogs.Info("Проверка недоступна", "В профиле есть ошибки — исправьте их (список внизу окна), и проверка заработает.");
            return null;
        }

        return new ClassificationEngine(compiled, SelectedMode.Key);
    }

    [RelayCommand]
    private void InspectSelection() => Inspect(_owner.Host.CurrentSelection());

    [RelayCommand]
    private void Pick()
    {
        var ids = _owner.Host.PickElements();
        if (ids.Count > 0) Inspect(ids);
    }

    public void Inspect(IEnumerable<long> ids)
    {
        _inspectedIds = ids.Take(50).ToList();
        Refresh();
    }

    /// <summary>Пересчитать проверяемые элементы (после правки профиля или смены режима).</summary>
    private void Refresh()
    {
        var engine = CreateEngine(tell: false);
        if (engine == null || _inspectedIds.Count == 0)
        {
            if (_inspectedIds.Count == 0) Inspected = [];
            return;
        }

        var compiled = _owner.Compiled!;
        var checks = new List<ElementCheck>();
        foreach (var id in _inspectedIds)
        {
            var facts = _owner.Host.Facts(id);
            if (facts == null) continue;
            var result = engine.Classify(facts);
            checks.Add(new ElementCheck(id, _owner.Host.Label(id), FactsOf(facts, compiled), result.Trace, ResultText(result), result.Status));
        }

        Inspected = checks;
    }

    private IReadOnlyList<FactRow> FactsOf(IElementFacts facts, CompiledProfile compiled)
    {
        var rows = new List<FactRow>
        {
            new("Категория", RuleDescriber.CategoryTitle(facts.Category) + " (" + facts.Category + ")"),
            new("Семейство / тип", facts.FamilyName + " / " + facts.TypeName),
            new("Имя", facts.InstanceName),
            new("Сеть", facts.Domain switch { NetworkDomain.Air => "воздуховоды", NetworkDomain.Pipe => "трубы", _ => "не подключён" }),
            new("Системы", facts.SystemText),
            new("Рабочий набор", facts.Workset ?? "—"),
            new("Родитель", facts.Parent != null ? facts.Parent.FamilyName + " / " + facts.Parent.TypeName : "—")
        };

        var ctx = new MatchContext(facts, SelectedMode!.Key, facts.Domain);
        foreach (var name in _owner.Profile.Texts.Keys.OrderBy(k => k))
        {
            string value;
            try
            {
                value = compiled.Runtime.ReadSource(name, ctx);
            }
            catch (Exception exception) when (exception is System.Text.RegularExpressions.RegexMatchTimeoutException or ClassifierCode.Engine.Patterns.PatternTimeoutException)
            {
                value = "шаблон проверялся слишком долго";
            }

            rows.Add(new FactRow(name, value));
        }

        return rows;
    }

    private static string ResultText(EngineResult result) => result.Status switch
    {
        EngineStatus.Classified => result.Position!.Code + " " + result.Position.Description,
        EngineStatus.Skipped => "Пропущен: " + result.Reason,
        _ => "Не классифицирован: " + result.Reason
    };

    /// <summary>Прогон по области без записи: что изменится.</summary>
    [RelayCommand]
    private void DryRun()
    {
        var engine = CreateEngine(tell: true);
        if (engine == null)
        {
            Rows = [];
            Filter();
            return;
        }

        var profile = _owner.Profile;
        var rows = new List<DryRunRow>();
        foreach (var id in _owner.Host.CollectScope(Scope, profile))
        {
            var facts = _owner.Host.Facts(id);
            if (facts == null) continue;
            var result = engine.Classify(facts);
            rows.Add(new DryRunRow(id, _owner.Host.Label(id), _owner.Host.CurrentCode(id, profile),
                result.Position?.Code ?? string.Empty, result.Status, result.Reason ?? string.Empty));
        }

        Rows = rows;
        Groups = rows.Where(r => r.Status == EngineStatus.Classified).GroupBy(r => r.New)
            .Select(g => new CodeGroup(g.Key, profile.Positions.FirstOrDefault(p => p.Code == g.Key)?.Description ?? string.Empty, g.Count()))
            .OrderBy(g => g.Code, StringComparer.Ordinal).ToList();
        Summary = $"Элементов: {rows.Count}; изменится: {rows.Count(r => r.IsChange)}; не классифицировано: {rows.Count(r => r.Status == EngineStatus.Unclassified)}; " +
                  $"пропущено: {rows.Count(r => r.Status == EngineStatus.Skipped)}";
        Filter();
    }

    private void Filter()
    {
        IEnumerable<DryRunRow> rows = Rows;
        if (OnlyChanges) rows = rows.Where(r => r.IsChange);
        if (OnlyUnclassified) rows = rows.Where(r => r.Status == EngineStatus.Unclassified);
        if (OnlySkipped) rows = rows.Where(r => r.Status == EngineStatus.Skipped);
        VisibleRows = rows.ToList();
    }

    [RelayCommand]
    private void SelectInModel() => _owner.Host.Select(VisibleRows.Select(r => r.Id));
}
