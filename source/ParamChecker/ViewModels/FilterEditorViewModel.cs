using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ParamChecker.Core.Checking;
using ParamChecker.Core.Profiles;

namespace ParamChecker.ViewModels;

/// <summary>Окно фильтра правила: категории, логика отбора и условия на параметры.</summary>
public sealed partial class FilterEditorViewModel : ObservableObject
{
    private readonly CheckerContext _context;
    private readonly List<CategoryItemViewModel> _categories;

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private bool _selectedOnly;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowCategories), nameof(ShowConditions), nameof(ModeHint))]
    private SelectionMode _mode;
    [ObservableProperty] private ConditionLogic _logic;
    [ObservableProperty] private int _selectedCount;

    public FilterEditorViewModel(CheckerContext context, string ruleTitle, RuleFilter filter)
    {
        _context = context;
        Title = ruleTitle.Trim().Length == 0 ? "Фильтр правила" : $"Фильтр правила «{ruleTitle.Trim()}»";
        _mode = filter.Mode;
        _logic = filter.Logic;

        var selected = new HashSet<string>(filter.Categories);
        _categories = context.Categories.All.Select(c => new CategoryItemViewModel(this, c.Key, c.Name, selected.Contains(c.Key))).ToList();
        // Категории из профиля, которых нет в этой версии Revit, не теряются.
        foreach (var key in filter.Categories.Where(key => _categories.All(c => c.Key != key)))
            _categories.Add(new CategoryItemViewModel(this, key, key, true));

        foreach (var condition in filter.Conditions) Conditions.Add(ConditionNodeViewModel.From(this, condition));
        RefreshCategories();
    }

    public string Title { get; }

    public ObservableCollection<CategoryItemViewModel> VisibleCategories { get; } = [];

    public ObservableCollection<ConditionNodeViewModel> Conditions { get; } = [];

    public IReadOnlyList<SelectionMode> Modes { get; } = Enum.GetValues(typeof(SelectionMode)).Cast<SelectionMode>().ToList();

    public IReadOnlyList<ConditionLogic> Logics { get; } = Enum.GetValues(typeof(ConditionLogic)).Cast<ConditionLogic>().ToList();

    public IReadOnlyList<ConditionOperator> Operators { get; } = Enum.GetValues(typeof(ConditionOperator)).Cast<ConditionOperator>().ToList();

    public IReadOnlyList<string> ParameterNames => _context.Parameters.Names;

    public bool ShowCategories => Mode != SelectionMode.ParametersOnly;

    public bool ShowConditions => Mode != SelectionMode.CategoriesOnly;

    public string ModeHint => Mode switch
    {
        SelectionMode.CategoriesOnly => "Проверяются все элементы выбранных категорий.",
        SelectionMode.ParametersOnly => "Проверяются элементы любых категорий, подходящие под условия.",
        SelectionMode.CategoriesOrParameters => "Проверяются элементы выбранных категорий и, кроме них, любые элементы, подходящие под условия.",
        _ => "Проверяются элементы выбранных категорий, подходящие под условия."
    };

    internal bool IsSuspicious(string name) => _context.Parameters.IsSuspicious(name);

    public RuleFilter ToModel() => new()
    {
        Categories = _categories.Where(c => c.IsSelected).Select(c => c.Key).ToList(),
        Mode = Mode,
        Logic = Logic,
        Conditions = Conditions.Select(c => c.ToModel()).Where(ConditionEvaluator.IsComplete).ToList()
    };

    internal void RefreshCount() => SelectedCount = _categories.Count(c => c.IsSelected);

    partial void OnSearchChanged(string value) => RefreshCategories();

    partial void OnSelectedOnlyChanged(bool value) => RefreshCategories();

    private void RefreshCategories()
    {
        var text = Search.Trim();
        VisibleCategories.Clear();
        foreach (var category in _categories
                     .Where(c => (!SelectedOnly || c.IsSelected) &&
                                 (text.Length == 0 || c.Name.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0))
                     .OrderByDescending(c => c.IsSelected && text.Length == 0 && !SelectedOnly)
                     .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
            VisibleCategories.Add(category);

        RefreshCount();
    }

    [RelayCommand]
    private void ClearCategories()
    {
        foreach (var category in _categories) category.IsSelected = false;
        RefreshCategories();
    }

    [RelayCommand]
    private void AddCondition() => Conditions.Add(new ConditionItemViewModel(this, null, Condition.Simple("", ConditionOperator.Equals)));

    [RelayCommand]
    private void AddGroup()
    {
        var group = new ConditionGroupViewModel(this, []);
        group.AddCommand.Execute(null);
        group.AddCommand.Execute(null);
        Conditions.Add(group);
    }

    internal void Remove(ConditionNodeViewModel node) => Conditions.Remove(node);
}

public sealed partial class CategoryItemViewModel(FilterEditorViewModel owner, string key, string name, bool selected) : ObservableObject
{
    [ObservableProperty] private bool _isSelected = selected;

    public string Key { get; } = key;
    public string Name { get; } = name;

    partial void OnIsSelectedChanged(bool value) => owner.RefreshCount();
}

public abstract class ConditionNodeViewModel : ObservableObject
{
    public abstract Condition ToModel();

    internal static ConditionNodeViewModel From(FilterEditorViewModel editor, Condition condition) =>
        condition.IsGroup ? new ConditionGroupViewModel(editor, condition.Group!) : new ConditionItemViewModel(editor, null, condition);
}

/// <summary>Условие «параметр — оператор — значение»; <c>group</c> — группа, в которой оно лежит.</summary>
public sealed partial class ConditionItemViewModel : ConditionNodeViewModel
{
    private readonly FilterEditorViewModel _editor;
    private readonly ConditionGroupViewModel? _group;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsUnknown))] private string _parameter;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(NeedsValue))] private ConditionOperator _operator;
    [ObservableProperty] private string _value;

    public ConditionItemViewModel(FilterEditorViewModel editor, ConditionGroupViewModel? group, Condition condition)
    {
        _editor = editor;
        _group = group;
        _parameter = condition.Parameter ?? "";
        _operator = condition.Operator ?? ConditionOperator.Equals;
        _value = condition.Value ?? "";
    }

    public FilterEditorViewModel Editor => _editor;

    public bool NeedsValue => ConditionEvaluator.NeedsValue(Operator);

    /// <summary>Имени нет ни в ФОП, ни в проекте, ни среди системных — похоже на опечатку.</summary>
    public bool IsUnknown => _editor.IsSuspicious(Parameter);

    public override Condition ToModel() =>
        Condition.Simple(Parameter.Trim(), Operator, NeedsValue ? Value.Trim() : "");

    [RelayCommand]
    private void Remove()
    {
        if (_group is null) _editor.Remove(this);
        else _group.Remove(this);
    }
}

/// <summary>Группа условий: все её условия связаны через «И», а сама она — одно условие верхнего уровня.</summary>
public sealed partial class ConditionGroupViewModel : ConditionNodeViewModel
{
    private readonly FilterEditorViewModel _editor;

    public ConditionGroupViewModel(FilterEditorViewModel editor, IEnumerable<Condition> children)
    {
        _editor = editor;
        // Вложенные группы в окне не создаются; если пришли из файла — их условия поднимаются в эту группу (логика та же — «И»).
        foreach (var child in Flatten(children)) Children.Add(new ConditionItemViewModel(editor, this, child));
    }

    public ObservableCollection<ConditionItemViewModel> Children { get; } = [];

    public override Condition ToModel() => new() { Group = Children.Select(c => c.ToModel()).ToList() };

    internal void Remove(ConditionItemViewModel child)
    {
        Children.Remove(child);
        if (Children.Count == 0) _editor.Remove(this);
    }

    [RelayCommand]
    private void Add() => Children.Add(new ConditionItemViewModel(_editor, this, Condition.Simple("", ConditionOperator.Equals)));

    [RelayCommand]
    private void RemoveGroup() => _editor.Remove(this);

    private static IEnumerable<Condition> Flatten(IEnumerable<Condition> conditions) =>
        conditions.SelectMany(c => c.IsGroup ? Flatten(c.Group!) : [c]);
}
