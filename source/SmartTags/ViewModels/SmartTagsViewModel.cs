using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using SmartTags.Layout;
using SmartTags.Models;
using SmartTags.Services;
using ToadTools.UI.Services;
using Wpf.Ui.Appearance;

namespace SmartTags.ViewModels;

public sealed partial class SmartTagsViewModel : ObservableObject
{
    private readonly SmartTagsSession _session;
    private readonly SmartTagsSettingsStore _store;
    private SmartTagsFormState _state;

    [ObservableProperty] private bool _darkTheme = true;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ScopeTitle), nameof(ScopeButtonText))]
    private bool _onlySelected;

    [ObservableProperty] private bool _showRunOptions;
    [ObservableProperty] private bool _showSameSection;
    [ObservableProperty] private string _minRunLength = string.Empty;
    [ObservableProperty] private bool _tagVerticalRuns;
    [ObservableProperty] private bool _skipSameDuctSection;

    [ObservableProperty] private ExistingTagMode _existingMode;
    [ObservableProperty] private string _preferredLeader = string.Empty;
    [ObservableProperty] private string _maxLeader = string.Empty;
    [ObservableProperty] private double _quality;

    [ObservableProperty] private bool _advancedExpanded;
    [ObservableProperty] private string _gap = string.Empty;
    [ObservableProperty] private string _minLeader = string.Empty;
    [ObservableProperty] private bool _angle30;
    [ObservableProperty] private bool _angle45;
    [ObservableProperty] private bool _angle60;
    [ObservableProperty] private bool _avoidTextOverElements;
    [ObservableProperty] private bool _useCrop;
    [ObservableProperty] private bool _writeLog;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    /// <summary>Настройки собраны — окно нужно закрыть и запустить расстановку.</summary>
    public event EventHandler? StartRequested;

    /// <summary>Пользователь хочет выбрать элементы на виде — окно нужно закрыть на время выбора.</summary>
    public event EventHandler? PickRequested;

    /// <summary>Окно закрыто ради выбора элементов на виде (а не отменой).</summary>
    public bool IsPicking { get; private set; }

    public SmartTagsViewModel(SmartTagsSession session, SmartTagsSettingsStore store)
    {
        _session = session;
        _store = store;
        _state = store.Load();

        ApplyState(_state);

        // Есть подходящее выделение — по умолчанию работаем только с ним (смена режима сама перестроит строки).
        if (session.HasSelection)
            OnlySelected = true;
        else
            RebuildRows();
    }

    public ObservableCollection<CategoryRowViewModel> Rows { get; } = [];

    /// <summary>Заполняется по нажатию «Запустить».</summary>
    public SmartTagsSettings? Result { get; private set; }

    public string ViewDescription => "Вид: " + _session.View.Description;
    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool ShowLockWarning => _session.View.IsUnlocked3D;

    public string LockWarning => ViewLocker.IsDefaultName(_session.View.Name)
        ? $"3D-вид не заблокирован. Стандартный вид «{_session.View.Name}» нельзя заблокировать без имени: " +
          $"при запуске он получит имя «{ViewLocker.LockedViewName}», ориентация сохранится и вид заблокируется."
        : "3D-вид не заблокирован: при запуске ориентация вида будет сохранена и вид заблокируется.";

    public bool HasSelection => _session.HasSelection;

    public string ScopeTitle
    {
        get
        {
            if (!OnlySelected) return "Весь вид — все видимые элементы отмеченных категорий";

            var count = _session.SelectionCategories.Sum(category => category.ElementCount);
            return count == _session.SelectedIds.Count
                ? $"Только выбранные — {count} эл."
                : $"Только выбранные — {count} из {_session.SelectedIds.Count} эл. подходят для маркировки";
        }
    }

    public string ScopeButtonText => OnlySelected ? "Весь вид" : "Только выбранные";

    public bool CanStart => Rows.Any(row => row.IsReady);

    partial void OnDarkThemeChanged(bool value)
    {
        ThemeWatcherService.ApplyTheme(value ? ApplicationTheme.Dark : ApplicationTheme.Light);
    }

    partial void OnOnlySelectedChanged(bool value) => RebuildRows();

    [RelayCommand]
    private void ToggleScope() => OnlySelected = !OnlySelected;

    [RelayCommand]
    private void PickElements()
    {
        SaveState();
        IsPicking = true;
        PickRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var row in Rows) row.IsSelected = true;
    }

    [RelayCommand]
    private void ClearAll()
    {
        foreach (var row in Rows) row.IsSelected = false;
    }

    [RelayCommand]
    private void ResetSettings()
    {
        var categories = _state.Categories;
        ApplyState(new SmartTagsFormState { AdvancedExpanded = true });
        _state.Categories = categories;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        Error = BuildSettings(out var settings);
        if (settings is null) return;

        Result = settings;
        SaveState();
        StartRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Сохраняет выбор пользователя (при запуске и при закрытии окна).</summary>
    public void SaveState()
    {
        var state = new SmartTagsFormState
        {
            ExistingMode = ExistingMode,
            TagVerticalRuns = TagVerticalRuns,
            SkipSameDuctSection = SkipSameDuctSection,
            Quality = (int)Math.Round(Quality),
            AdvancedExpanded = AdvancedExpanded,
            Angle30 = Angle30,
            Angle45 = Angle45,
            Angle60 = Angle60,
            AvoidTextOverElements = AvoidTextOverElements,
            UseCrop = UseCrop,
            WriteLog = WriteLog,
            MinRunLength = ParseOr(MinRunLength, _state.MinRunLength),
            PreferredLeader = ParseOr(PreferredLeader, _state.PreferredLeader),
            MaxLeader = _state.MaxLeader,
            MaxLeader3D = _state.MaxLeader3D,
            Gap = ParseOr(Gap, _state.Gap),
            MinLeader = ParseOr(MinLeader, _state.MinLeader)
        };

        if (_session.View.Kind == ViewKind.ThreeD)
            state.MaxLeader3D = ParseOr(MaxLeader, _state.MaxLeader3D);
        else
            state.MaxLeader = ParseOr(MaxLeader, _state.MaxLeader);

        // Отметки категорий, которых нет на этом виде, не теряем.
        var picks = _state.Categories
            .GroupBy(pick => pick.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.CurrentCultureIgnoreCase);
        foreach (var row in Rows)
        {
            picks[row.Category.Name] = new CategoryPick
            {
                Name = row.Category.Name,
                // В режиме «только выбранные» все строки отмечены автоматически — это не выбор пользователя.
                Selected = OnlySelected ? picks.TryGetValue(row.Category.Name, out var old) && old.Selected : row.IsSelected,
                TagTypeName = row.SelectedTagType?.DisplayName
            };
        }

        state.Categories = picks.Values.ToList();
        _state = state;
        _store.Save(state);
    }

    private void ApplyState(SmartTagsFormState state)
    {
        _state = state;
        ExistingMode = state.ExistingMode;
        MinRunLength = Format(state.MinRunLength);
        TagVerticalRuns = state.TagVerticalRuns;
        SkipSameDuctSection = state.SkipSameDuctSection;
        PreferredLeader = Format(state.PreferredLeader);
        MaxLeader = Format(_session.View.Kind == ViewKind.ThreeD ? state.MaxLeader3D : state.MaxLeader);
        Quality = Math.Max(0, Math.Min(2, state.Quality));
        AdvancedExpanded = state.AdvancedExpanded;
        Gap = Format(state.Gap);
        MinLeader = Format(state.MinLeader);
        Angle30 = state.Angle30;
        Angle45 = state.Angle45;
        Angle60 = state.Angle60;
        AvoidTextOverElements = state.AvoidTextOverElements;
        UseCrop = state.UseCrop;
        WriteLog = state.WriteLog;
    }

    private void RebuildRows()
    {
        foreach (var row in Rows) row.PropertyChanged -= OnRowChanged;
        Rows.Clear();

        var categories = OnlySelected ? _session.SelectionCategories : _session.ViewCategories;
        foreach (var category in categories)
        {
            var pick = _state.Categories.FirstOrDefault(p =>
                string.Equals(p.Name, category.Name, StringComparison.CurrentCultureIgnoreCase));
            var row = new CategoryRowViewModel(category)
            {
                IsSelected = OnlySelected || pick is { Selected: true }
            };
            if (pick?.TagTypeName is { } typeName &&
                category.TagTypes.FirstOrDefault(type => type.DisplayName == typeName) is { } saved)
                row.SelectedTagType = saved;

            row.PropertyChanged += OnRowChanged;
            Rows.Add(row);
        }

        // Ничего не отмечено (первый запуск) — отмечаем первую категорию, чтобы было с чего начать.
        if (Rows.Count > 0 && Rows.All(row => !row.IsSelected)) Rows[0].IsSelected = true;

        UpdateOptions();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e) => UpdateOptions();

    private void UpdateOptions()
    {
        var selected = Rows.Where(row => row.IsSelected).ToList();
        ShowRunOptions = selected.Any(row => row.Category.IsLinear);
        ShowSameSection = selected.Any(row => row.Category.IsDucts);
        Error = null;
        OnPropertyChanged(nameof(CanStart));
        StartCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Проверяет ввод. Возвращает текст ошибки или null; при успехе — настройки.</summary>
    private string? BuildSettings(out SmartTagsSettings? settings)
    {
        settings = null;
        var rows = Rows.Where(row => row.IsReady).ToList();
        if (rows.Count == 0) return "Отметьте хотя бы одну категорию и выберите для неё тип марки.";

        if (!TryParse(MinRunLength, 0, 100_000, out var minRun))
            return "Минимальная длина участка: число от 0 до 100000 мм.";
        if (!TryParse(PreferredLeader, 1, 500, out var preferred))
            return "Расстояние до марки: число от 1 до 500 мм.";
        if (!TryParse(MaxLeader, 1, 1000, out var max))
            return "Максимальное расстояние: число от 1 до 1000 мм.";
        if (!TryParse(Gap, 0, 50, out var gap))
            return "Зазор между марками: число от 0 до 50 мм.";
        if (!TryParse(MinLeader, 0.5, 200, out var minLeader))
            return "Минимальная длина выноски: число от 0,5 до 200 мм.";
        if (max < preferred)
            return "Максимальное расстояние не может быть меньше расстояния до марки.";
        if (minLeader > preferred)
            return "Минимальная длина выноски не может быть больше расстояния до марки.";

        settings = new SmartTagsSettings
        {
            ExistingMode = ExistingMode,
            MinRunLength = minRun,
            TagVerticalRuns = TagVerticalRuns,
            SkipSameDuctSection = SkipSameDuctSection,
            PreferredLeader = preferred,
            MaxLeader = max,
            MinLeader = minLeader,
            Gap = gap,
            Quality = (LayoutQuality)(int)Math.Round(Quality),
            Angle30 = Angle30,
            Angle45 = Angle45,
            Angle60 = Angle60,
            AvoidTextOverElements = AvoidTextOverElements,
            UseCrop = UseCrop,
            WriteLog = WriteLog,
            OnlyElements = OnlySelected ? new HashSet<ElementId>(_session.SelectedIds) : null
        };

        foreach (var row in rows)
            settings.Choices.Add(new CategoryChoice(row.Category.Category, row.Category.Name,
                row.Category.DuctProfile, row.SelectedTagType!));

        return null;
    }

    private static bool TryParse(string text, double min, double max, out double value)
    {
        return double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture,
                   out value) && value >= min && value <= max;
    }

    private static double ParseOr(string text, double fallback)
    {
        return TryParse(text, double.MinValue, double.MaxValue, out var value) ? value : fallback;
    }

    private static string Format(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);
}
