using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Alexpivo394.Revit.ParamCheck.Core;
using Alexpivo394.Revit.ParamCheck.Profiles;
using ToadTools.UI.Models;
using Condition = Alexpivo394.Revit.ParamCheck.Profiles.Condition;

namespace ParamChecker.ViewModels;

/// <summary>
///     Правило в профиле. Фильтр и параметры показываются кратко, редактируются в своих окнах, а между правилами и
///     профилями переносятся через буфер обмена: «Копировать» кладёт туда правило, «Вставить фильтр / параметры» берут из него часть.
/// </summary>
public sealed partial class RuleViewModel : ObservableObject
{
    private readonly CheckerContext _context;
    private readonly ProfileViewModel _owner;
    private RuleFilter _filter;
    private List<string> _parameters;

    [ObservableProperty] private string _title;

    public RuleViewModel(CheckerContext context, ProfileViewModel owner, CheckRule rule)
    {
        _context = context;
        _owner = owner;
        _title = rule.Title;
        _filter = rule.Filter;
        _parameters = rule.Parameters.ToList();
    }

    public string FilterSummary
    {
        get
        {
            var categories = _filter.Categories.Select(_context.Categories.NameOf).ToList();
            var conditions = _filter.Conditions.Count(ConditionRules.IsComplete);
            var categoryText = categories.Count == 0 ? "категории не выбраны" : Words.List(categories, 2);
            var conditionText = conditions == 0 ? "без условий" : Words.Count(conditions, "условие", "условия", "условий");

            return _filter.Mode switch
            {
                SelectionMode.CategoriesOnly => categoryText,
                SelectionMode.ParametersOnly => conditions == 0 ? "условия не заданы" : conditionText,
                SelectionMode.CategoriesOrParameters => conditions == 0 ? categoryText : $"{categoryText} или {conditionText}",
                _ => conditions == 0 ? categoryText : $"{categoryText} · {conditionText}"
            };
        }
    }

    public string FilterTooltip
    {
        get
        {
            var lines = new List<string> { EnumText(_filter.Mode) };
            if (_filter.UsesCategories)
                lines.Add("Категории: " + (_filter.Categories.Count == 0 ? "—" : string.Join(", ", _filter.Categories.Select(_context.Categories.NameOf))));
            if (_filter.UsesConditions)
            {
                foreach (var condition in _filter.Conditions.Where(ConditionRules.IsComplete))
                    lines.Add((_filter.Logic == ConditionLogic.Or ? "или " : "и ") + Describe(condition));
            }

            return string.Join("\n", lines);
        }
    }

    public bool FilterHasProblem => _filter.Mode switch
    {
        SelectionMode.CategoriesOnly or SelectionMode.CategoriesAndParameters => _filter.Categories.Count == 0,
        SelectionMode.ParametersOnly => !ConditionRules.HasConditions(_filter),
        _ => _filter.Categories.Count == 0 && !ConditionRules.HasConditions(_filter)
    };

    public string ParametersSummary => _parameters.Count == 0 ? "параметры не выбраны" : Words.List(_parameters, 3);

    public string ParametersTooltip => _parameters.Count == 0 ? "Нажмите, чтобы выбрать параметры" : string.Join("\n", _parameters);

    public bool ParametersHaveProblem => _parameters.Count == 0;

    public CheckRule ToModel() => new()
    {
        Title = Title.Trim(),
        Filter = ProfileSerializer.Clone(new CheckRule { Filter = _filter }).Filter,
        Parameters = _parameters.ToList()
    };

    partial void OnTitleChanged(string value) => _context.NotifyChanged();

    [RelayCommand]
    private void EditFilter()
    {
        var editor = new FilterEditorViewModel(_context, Title, _filter);
        if (!_context.Windows.EditFilter(editor)) return;

        _filter = editor.ToModel();
        RefreshFilter();
    }

    [RelayCommand]
    private void EditParameters()
    {
        var editor = new ParametersEditorViewModel(_context, Title, _parameters);
        if (!_context.Windows.EditParameters(editor)) return;

        _parameters = editor.ToModel();
        RefreshParameters();
    }

    [RelayCommand]
    private void Duplicate() => _owner.InsertRule(this, ProfileSerializer.Clone(ToModel()), " (копия)");

    [RelayCommand]
    private void Remove() => _owner.RemoveRule(this);

    [RelayCommand]
    private void Copy()
    {
        try
        {
            Clipboard.SetText(ProfileSerializer.SerializeRule(ToModel()));
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            _context.Windows.Show("Буфер обмена занят другой программой — попробуйте ещё раз.", DialogButtons.OK, DialogIcon.Warning);
        }
    }

    [RelayCommand]
    private void PasteFilter()
    {
        if (ClipboardRule(_context) is not { } rule) return;

        _filter = rule.Filter;
        RefreshFilter();
    }

    [RelayCommand]
    private void PasteParameters()
    {
        if (ClipboardRule(_context) is not { } rule) return;

        _parameters = rule.Parameters.ToList();
        RefreshParameters();
    }

    /// <summary>Правило из буфера обмена; если там не правило — объясняет, как его туда положить.</summary>
    internal static CheckRule? ClipboardRule(CheckerContext context)
    {
        string? text = null;
        try
        {
            if (Clipboard.ContainsText()) text = Clipboard.GetText();
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
        }

        var rule = ProfileSerializer.DeserializeRule(text);
        if (rule is null)
            context.Windows.Show("В буфере обмена нет правила. Скопируйте правило командой «Копировать» в его меню.",
                DialogButtons.OK, DialogIcon.Info);
        return rule;
    }

    internal static string EnumText(Enum value) =>
        value.GetType().GetField(value.ToString())?
            .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
            .OfType<System.ComponentModel.DescriptionAttribute>().FirstOrDefault()?.Description ?? value.ToString();

    private static string Describe(Condition condition)
    {
        if (condition.IsGroup) return "(" + string.Join(" и ", condition.Group!.Where(ConditionRules.IsComplete).Select(Describe)) + ")";

        var op = EnumText(condition.Operator!.Value).ToLowerInvariant();
        return ConditionRules.NeedsValue(condition.Operator.Value)
            ? $"{condition.Parameter} {op} «{condition.Value}»"
            : $"{condition.Parameter} — {op}";
    }

    private void RefreshFilter()
    {
        OnPropertyChanged(nameof(FilterSummary));
        OnPropertyChanged(nameof(FilterTooltip));
        OnPropertyChanged(nameof(FilterHasProblem));
        _context.NotifyChanged();
    }

    private void RefreshParameters()
    {
        OnPropertyChanged(nameof(ParametersSummary));
        OnPropertyChanged(nameof(ParametersTooltip));
        OnPropertyChanged(nameof(ParametersHaveProblem));
        _context.NotifyChanged();
    }
}
