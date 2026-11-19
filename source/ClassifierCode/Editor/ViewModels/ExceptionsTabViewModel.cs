using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Editor.ViewModels;

public enum ConditionKind
{
    Category,
    Text,
    Parameter,
    Mode,
    Type,
    Other
}

public sealed record ConditionKindOption(ConditionKind Kind, string Title);

/// <summary>Условие исключения в конструкторе: вид, «не», значение.</summary>
public sealed partial class ConditionViewModel : ObservableObject
{
    private readonly ClassifierProfile _profile;
    private readonly Action _changed;

    public ConditionViewModel(ConditionDef source, ClassifierProfile profile, Action changed)
    {
        Source = source;
        _profile = profile;
        _changed = changed;
    }

    public ConditionDef Source { get; }

    public static IReadOnlyList<ConditionKindOption> Kinds { get; } =
    [
        new(ConditionKind.Category, "Категория"),
        new(ConditionKind.Text, "Текст элемента содержит"),
        new(ConditionKind.Parameter, "Параметр содержит"),
        new(ConditionKind.Mode, "Режим"),
        new(ConditionKind.Type, "Тип элемента"),
        new(ConditionKind.Other, "Словарь (правится в JSON)")
    ];

    public IReadOnlyList<string> SourceNames => _profile.Texts.Keys.OrderBy(k => k).ToList();

    public ConditionKind Kind
    {
        get
        {
            var c = Source;
            if (c.Category != null) return ConditionKind.Category;
            if (c.Mode != null) return ConditionKind.Mode;
            if (c.Type != null) return ConditionKind.Type;
            if (c.Matches != null) return c.Source != null && !c.Source.StartsWith("param:") ? ConditionKind.Text : ConditionKind.Parameter;
            return c.Source == null && c.Dictionary == null && c.Domain == null ? ConditionKind.Category : ConditionKind.Other;
        }
        set
        {
            if (value == Kind || value == ConditionKind.Other) return;
            var c = Source;
            c.Category = null;
            c.Mode = null;
            c.Type = null;
            c.Domain = null;
            c.Dictionary = null;
            c.Gives = null;
            c.GivesNothing = false;
            c.Source = null;
            c.Matches = null;
            switch (value)
            {
                case ConditionKind.Category: c.Category = []; break;
                case ConditionKind.Mode: c.Mode = []; break;
                case ConditionKind.Type: c.Type = []; break;
                case ConditionKind.Text:
                    c.Source = _profile.Texts.ContainsKey("fullName") ? "fullName" : SourceNames.FirstOrDefault();
                    c.Matches = string.Empty;
                    break;
                case ConditionKind.Parameter:
                    c.Matches = string.Empty;
                    break;
            }

            Changed();
        }
    }

    public bool IsEditable => Kind != ConditionKind.Other;
    public bool IsText => Kind == ConditionKind.Text;
    public bool IsParameter => Kind == ConditionKind.Parameter;

    public bool Not
    {
        get => Source.Not;
        set
        {
            Source.Not = value;
            Changed();
        }
    }

    /// <summary>Список через запятую (категории, режимы, типы) или шаблон (текст, параметр).</summary>
    public string Value
    {
        get => Kind switch
        {
            ConditionKind.Category => string.Join(", ", Source.Category ?? []),
            ConditionKind.Mode => string.Join(", ", Source.Mode ?? []),
            ConditionKind.Type => string.Join(", ", Source.Type ?? []),
            ConditionKind.Text or ConditionKind.Parameter => Source.Matches ?? string.Empty,
            _ => RuleDescriber.Conditions([Source], _profile)
        };
        set
        {
            var list = value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            switch (Kind)
            {
                case ConditionKind.Category: Source.Category = list; break;
                case ConditionKind.Mode: Source.Mode = list; break;
                case ConditionKind.Type: Source.Type = list; break;
                case ConditionKind.Text:
                case ConditionKind.Parameter:
                    Source.Matches = value.Trim();
                    break;
                default: return;
            }

            Changed();
        }
    }

    public string? SourceName
    {
        get => Source.Source;
        set
        {
            Source.Source = value;
            Changed();
        }
    }

    /// <summary>Параметр экземпляра или типа: для него заводится источник «param:имя».</summary>
    public string ParameterName
    {
        get => Source.Source != null && Source.Source.StartsWith("param:") ? Source.Source.Substring(6) : string.Empty;
        set
        {
            var name = value.Trim();
            if (name.Length == 0) return;
            var key = "param:" + name;
            if (!_profile.Texts.ContainsKey(key))
                _profile.Texts[key] = new TextSourceDef { Parts = ["instanceOrType:" + name] };
            Source.Source = key;
            Changed();
        }
    }

    private void Changed()
    {
        OnPropertyChanged(string.Empty);
        _changed();
    }
}

public sealed record ActionOption(string Kind, string Title);

/// <summary>Исключение: условия и что сделать.</summary>
public sealed partial class ExceptionRuleViewModel : ObservableObject
{
    private readonly ClassifierProfile _profile;
    private readonly Action _changed;

    public ExceptionRuleViewModel(RuleDef source, ClassifierProfile profile, Action changed)
    {
        Source = source;
        _profile = profile;
        _changed = changed;
        foreach (var condition in source.When) Conditions.Add(new ConditionViewModel(condition, profile, Changed));
    }

    public RuleDef Source { get; }
    public ObservableCollection<ConditionViewModel> Conditions { get; } = [];

    public static IReadOnlyList<ActionOption> Actions { get; } =
    [
        new("position", "Записать позицию"),
        new("skip", "Пропустить с причиной"),
        new("discipline", "Задать раздел"),
        new("type", "Задать тип")
    ];

    /// <summary>Правило в простой форме «условия → действие»; иначе правится на вкладке «Логика».</summary>
    public bool IsSimple => Source.Steps.Count == 0 && Source.Allow == null && Source.Refine.Count == 0 && Source.Default != null;

    public string Title
    {
        get => Source.Title ?? string.Empty;
        set
        {
            Source.Title = value;
            Changed();
        }
    }

    public string ActionKind
    {
        get
        {
            var d = Source.Default;
            if (d?.Skip != null) return "skip";
            if (d?.Discipline != null) return "discipline";
            if (d?.Type != null) return "type";
            return "position";
        }
        set
        {
            var current = ActionValue;
            Source.Default = value switch
            {
                "skip" => new ResultDef { Skip = current },
                "discipline" => new ResultDef { Discipline = current },
                "type" => new ResultDef { Type = current },
                _ => new ResultDef { Position = current }
            };
            Changed();
        }
    }

    public string ActionValue
    {
        get => Source.Default?.Skip ?? Source.Default?.Position ?? Source.Default?.Discipline ?? Source.Default?.Type ?? string.Empty;
        set
        {
            var kind = ActionKind;
            Source.Default = kind switch
            {
                "skip" => new ResultDef { Skip = value },
                "discipline" => new ResultDef { Discipline = value.Trim() },
                "type" => new ResultDef { Type = value.Trim() },
                _ => new ResultDef { Position = value.Trim() }
            };
            Changed();
        }
    }

    public string Summary => string.Join(Environment.NewLine, RuleDescriber.Describe(Source, _profile, isTypeStage: true));

    [RelayCommand]
    private void AddCondition()
    {
        var condition = new ConditionDef { Category = [] };
        Source.When.Add(condition);
        Conditions.Add(new ConditionViewModel(condition, _profile, Changed));
        Changed();
    }

    [RelayCommand]
    private void RemoveCondition(ConditionViewModel? condition)
    {
        if (condition == null) return;
        Source.When.Remove(condition.Source);
        Conditions.Remove(condition);
        Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ActionKind));
        OnPropertyChanged(nameof(ActionValue));
        _changed();
    }
}

/// <summary>Вкладка «Исключения»: правила объекта, которые срабатывают раньше распознавания, по порядку.</summary>
public sealed partial class ExceptionsTabViewModel : ObservableObject
{
    private readonly ProfileEditorViewModel _owner;

    [ObservableProperty] private ExceptionRuleViewModel? _selectedRule;

    public ExceptionsTabViewModel(ProfileEditorViewModel owner)
    {
        _owner = owner;
        Reload();
    }

    private ClassifierProfile P => _owner.Profile;
    public ObservableCollection<ExceptionRuleViewModel> Rules { get; } = [];
    public IReadOnlyList<string> ParameterNames => _owner.General.ParameterNames;
    public IReadOnlyList<string> SourceNames => P.Texts.Keys.OrderBy(k => k).ToList();

    public void Reload()
    {
        var selected = SelectedRule?.Source;
        Rules.Clear();
        foreach (var rule in P.Exceptions) Rules.Add(new ExceptionRuleViewModel(rule, P, _owner.MarkChanged));
        SelectedRule = Rules.FirstOrDefault(r => r.Source == selected) ?? Rules.FirstOrDefault();
    }

    [RelayCommand]
    private void Add()
    {
        var rule = new RuleDef { Title = "Новое исключение", Default = new ResultDef { Skip = "заполняется вручную" } };
        P.Exceptions.Add(rule);
        Reload();
        SelectedRule = Rules.Last();
        _owner.MarkChanged();
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (SelectedRule == null) return;
        var copy = ProfileSerializer.ReadFragment<RuleDef>(ProfileSerializer.WriteFragment(SelectedRule.Source));
        copy.Title += " (копия)";
        P.Exceptions.Insert(P.Exceptions.IndexOf(SelectedRule.Source) + 1, copy);
        Reload();
        SelectedRule = Rules.First(r => r.Source == copy);
        _owner.MarkChanged();
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedRule == null) return;
        if (_owner.Dialogs.Ask("Удалить исключение", $"Удалить «{SelectedRule.Title}»?") != true) return;
        P.Exceptions.Remove(SelectedRule.Source);
        Reload();
        _owner.MarkChanged();
    }

    [RelayCommand]
    private void MoveUp() => Move(-1);

    [RelayCommand]
    private void MoveDown() => Move(1);

    private void Move(int delta)
    {
        if (SelectedRule == null) return;
        var rule = SelectedRule.Source;
        var index = P.Exceptions.IndexOf(rule);
        var target = index + delta;
        if (target < 0 || target >= P.Exceptions.Count) return;
        P.Exceptions.RemoveAt(index);
        P.Exceptions.Insert(target, rule);
        Reload();
        SelectedRule = Rules.First(r => r.Source == rule);
        _owner.MarkChanged();
    }
}
