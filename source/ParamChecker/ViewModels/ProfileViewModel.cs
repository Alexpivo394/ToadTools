using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Alexpivo394.Revit.ParamCheck.Profiles;
using ToadTools.UI.Models;

namespace ParamChecker.ViewModels;

/// <summary>Профиль: модели и правила. Вид и ключевое слово РН профиля действуют на все его модели, если у модели не задано своё.</summary>
public sealed partial class ProfileViewModel : ObservableObject
{
    private readonly CheckerContext _context;
    private readonly MainViewModel _owner;

    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _viewName;
    [ObservableProperty] private string _worksetKeyword;

    public ProfileViewModel(CheckerContext context, MainViewModel owner, CheckProfile profile)
    {
        _context = context;
        _owner = owner;
        _name = profile.Name;
        _isEnabled = profile.IsEnabled;
        _viewName = profile.ViewName;
        _worksetKeyword = profile.WorksetKeyword;

        foreach (var model in profile.Models) Models.Add(new ModelItemViewModel(context, this, model));
        foreach (var rule in profile.Rules) Rules.Add(new RuleViewModel(context, this, rule));

        Models.CollectionChanged += OnCollectionChanged;
        Rules.CollectionChanged += OnCollectionChanged;
    }

    public ObservableCollection<ModelItemViewModel> Models { get; } = [];
    public ObservableCollection<RuleViewModel> Rules { get; } = [];

    public string Summary => $"{Words.Count(Models.Count, "модель", "модели", "моделей")} · {Words.Count(Rules.Count, "правило", "правила", "правил")}";

    public string DisplayName => Name.Trim().Length == 0 ? "Без имени" : Name.Trim();

    public CheckProfile ToModel() => new()
    {
        Name = Name.Trim(),
        IsEnabled = IsEnabled,
        ViewName = ViewName.Trim(),
        WorksetKeyword = WorksetKeyword.Trim(),
        Models = Models.Select(model => model.ToModel()).ToList(),
        Rules = Rules.Select(rule => rule.ToModel()).ToList()
    };

    internal void InsertRule(RuleViewModel after, CheckRule rule, string titleSuffix)
    {
        rule.Title += titleSuffix;
        Rules.Insert(Rules.IndexOf(after) + 1, new RuleViewModel(_context, this, rule));
    }

    internal void RemoveRule(RuleViewModel rule) => Rules.Remove(rule);

    internal void RemoveModel(ModelItemViewModel model) => Models.Remove(model);

    /// <summary>Добавляет модели, которых в профиле ещё нет; возвращает, сколько добавлено.</summary>
    internal int AddModels(IEnumerable<string> paths)
    {
        var existing = new HashSet<string>(Models.Select(m => ModelNames.Key(m.Path)), StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var path in paths.Select(p => p.Trim()).Where(p => p.Length > 0 && existing.Add(ModelNames.Key(p))))
        {
            Models.Add(new ModelItemViewModel(_context, this, new ModelSource { Path = path }));
            added++;
        }

        return added;
    }

    internal IReadOnlyCollection<string> ModelPaths() =>
        new HashSet<string>(Models.Select(m => ModelNames.Key(m.Path)), StringComparer.OrdinalIgnoreCase);

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
        _context.NotifyChanged();
    }

    partial void OnIsEnabledChanged(bool value)
    {
        _owner.RefreshRunState();
        _context.NotifyChanged();
    }

    partial void OnViewNameChanged(string value)
    {
        foreach (var model in Models) model.RefreshPlaceholders();
        _context.NotifyChanged();
    }

    partial void OnWorksetKeywordChanged(string value)
    {
        foreach (var model in Models) model.RefreshPlaceholders();
        _context.NotifyChanged();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Summary));
        _owner.RefreshRunState();
        _context.NotifyChanged();
    }

    [RelayCommand]
    private void AddFromServer()
    {
        var picker = new ServerPickerViewModel(_context, ModelPaths(), _owner.AllModelPaths());
        if (!_context.Windows.PickServerModels(picker)) return;

        _context.Settings.Server = picker.Server.Trim();
        AddModels(picker.SelectedPaths());
    }

    [RelayCommand]
    private void AddFiles() => AddModels(_context.Windows.PickModelFiles());

    [RelayCommand]
    private void AddPath() => Models.Add(new ModelItemViewModel(_context, this, new ModelSource()));

    [RelayCommand]
    private void ClearModels()
    {
        if (Models.Count == 0) return;
        if (_context.Windows.Show($"Убрать из профиля все модели ({Models.Count})?", DialogButtons.YesNo, DialogIcon.Warning) == "Yes")
            Models.Clear();
    }

    [RelayCommand]
    private void AddRule()
    {
        var rule = new RuleViewModel(_context, this, new CheckRule { Title = $"Правило {Rules.Count + 1}" });
        Rules.Add(rule);
        rule.EditFilterCommand.Execute(null);
    }

    [RelayCommand]
    private void PasteRule()
    {
        if (RuleViewModel.ClipboardRule(_context) is { } rule) Rules.Add(new RuleViewModel(_context, this, rule));
    }

    [RelayCommand]
    private void Duplicate() => _owner.DuplicateProfile(this);

    [RelayCommand]
    private void Remove() => _owner.RemoveProfile(this);
}
