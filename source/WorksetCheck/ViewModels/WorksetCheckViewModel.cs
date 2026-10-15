using ToadTools.UI.Services;
using WorksetCheck.Models;
using WorksetCheck.Services;
using Wpf.Ui.Appearance;
using Settings = WorksetCheck.Configuration.Settings;

namespace WorksetCheck.ViewModels;

/// <summary>
///     Окно перед проверкой: имена РН, уже подставленные автоопределением или из прошлого запуска.
///     Подсказки под полями пересчитываются при вводе.
/// </summary>
public sealed partial class WorksetCheckViewModel : ObservableObject
{
    private const string Skipped = "Поле пустое — проверка пропускается";

    [ObservableProperty] private bool _darkTheme = true;
    [ObservableProperty] private string _linksPrefix = string.Empty;
    [ObservableProperty] private string _gridsWorkset = string.Empty;
    [ObservableProperty] private string _openingsWorkset = string.Empty;
    [ObservableProperty] private string _linksSource = string.Empty;
    [ObservableProperty] private string _gridsSource = string.Empty;
    [ObservableProperty] private string _openingsSource = string.Empty;

    private readonly bool _initialized;

    public WorksetCheckViewModel(IReadOnlyList<string> worksets, DetectedWorksets detected, Settings settings)
    {
        Worksets = worksets;
        DarkTheme = settings.DarkTheme;

        (LinksPrefix, LinksSource) = Pick(settings.LinksPrefix, detected.LinksPrefix,
            saved => worksets.Any(name => name.StartsWith(saved, StringComparison.OrdinalIgnoreCase)));
        (GridsWorkset, GridsSource) = Pick(settings.GridsWorkset, detected.GridsWorkset, Exists);
        (OpeningsWorkset, OpeningsSource) = Pick(settings.OpeningsWorkset, detected.OpeningsWorkset, Exists);

        _initialized = true;
    }

    public event EventHandler? StartRequested;

    /// <summary>Пользовательские РН модели — для выпадающих списков.</summary>
    public IReadOnlyList<string> Worksets { get; }

    public string LinksHint
    {
        get
        {
            if (LinksPrefix.Trim().Length == 0) return Skipped;
            var matches = Worksets.Where(name => name.StartsWith(LinksPrefix.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0) return "РН с таким началом нет — нужные будут созданы при исправлении";

            var shown = string.Join(", ", matches.Take(4)) + (matches.Count > 4 ? ", …" : string.Empty);
            return $"РН связей: {matches.Count} — {shown}";
        }
    }

    public string GridsHint => GridsWorkset.Trim().Length == 0
        ? Skipped
        : Exists(GridsWorkset.Trim())
            ? "РН есть в модели"
            : "Такого РН нет — будет создан при исправлении";

    public string OpeningsHint => OpeningsWorkset.Trim().Length == 0
        ? Skipped
        : Exists(OpeningsWorkset.Trim())
            ? "РН есть в модели"
            : "Такого РН нет в модели — проверьте имя";

    public WorksetRules ToRules() => new(LinksPrefix, GridsWorkset, OpeningsWorkset);

    public Settings ToSettings() => new()
    {
        DarkTheme = DarkTheme,
        LinksPrefix = LinksPrefix.Trim(),
        GridsWorkset = GridsWorkset.Trim(),
        OpeningsWorkset = OpeningsWorkset.Trim()
    };

    [RelayCommand]
    private void Start() => StartRequested?.Invoke(this, EventArgs.Empty);

    partial void OnDarkThemeChanged(bool value)
    {
        ThemeWatcherService.ApplyTheme(value ? ApplicationTheme.Dark : ApplicationTheme.Light);
    }

    partial void OnLinksPrefixChanged(string value)
    {
        OnPropertyChanged(nameof(LinksHint));
        if (_initialized) LinksSource = string.Empty;
    }

    partial void OnGridsWorksetChanged(string value)
    {
        OnPropertyChanged(nameof(GridsHint));
        if (_initialized) GridsSource = string.Empty;
    }

    partial void OnOpeningsWorksetChanged(string value)
    {
        OnPropertyChanged(nameof(OpeningsHint));
        if (_initialized) OpeningsSource = string.Empty;
    }

    private bool Exists(string name) => Worksets.Any(workset => string.Equals(workset, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Прошлое значение — если оно подходит этой модели; иначе найденное автоматически.</summary>
    private static (string Value, string Source) Pick(string? saved, string? detected, Func<string, bool> fitsModel)
    {
        if (!string.IsNullOrWhiteSpace(saved) && fitsModel(saved!)) return (saved!, "Как в прошлый раз");
        if (!string.IsNullOrWhiteSpace(detected)) return (detected!, "Определено автоматически");
        return (string.Empty, "Не удалось определить — введите вручную");
    }
}
