using System.Collections.ObjectModel;
using ToadDesign;
using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Library;
using ClassifierCode.Engine.Profile;
using ClassifierCode.Engine.Running;
using ClassifierCode.Models;
using ClassifierCode.Services;
using ToadTools.UI.Models;
using ToadTools.UI.Services;

namespace ClassifierCode.ViewModels;

public sealed partial class ClassifierCodeViewModel : ObservableObject
{
    private readonly ClassifierCodeService _service;
    private readonly ClassifierSettings _settings;
    private CompiledProfile? _compiled;
    private string? _preferredMode;

    /// <summary>Раздел из привязки пропал из профиля — не подставлять никакой: пусть выберут сами, а не получат чужие коды.</summary>
    private bool _noAutoMode;

    [ObservableProperty] private ProfileEntry? _selectedProfile;
    [ObservableProperty] private IReadOnlyList<ModeDef> _sections = [];
    [ObservableProperty] private ModeDef? _selectedSection;
    [ObservableProperty] private bool _isSectionDetected;
    [ObservableProperty] private RunScope _scope;
    [ObservableProperty] private bool _keepExisting;
    [ObservableProperty] private string? _warning;
    [ObservableProperty] private IReadOnlyList<ProfileIssue> _issues = [];

    /// <summary>Окно нужно закрыть, заполнение выполнено — показать отчёт.</summary>
    public event EventHandler<ClassifierReport>? Completed;

    /// <summary>Открыть редактор профиля (на первой ошибке, если она передана).</summary>
    public event EventHandler<ProfileIssue?>? ConfigureRequested;

    public event EventHandler? ImportRequested;
    public event EventHandler? FoldersRequested;
    public event EventHandler? AiExportRequested;

    public ClassifierCodeViewModel(ClassifierCodeService service)
    {
        _service = service;
        // Имена категорий в профиле сверяются с этой версией Revit: опечатка «OST_PipeCurve» молча не совпала бы никогда.
        ProfileCompiler.KnownCategory ??= name => ElementCollector.Categories([name]).Count == 1;
        _settings = ClassifierSettings.Load(ClassifierSettings.DefaultPath);
        Library = CreateLibrary();

        var uiDocument = RevitContext.ActiveUiDocument!;
        DocumentTitle = uiDocument.Document.Title;
        SelectedCount = uiDocument.Selection.GetElementIds().Count;
        Scope = SelectedCount > 0 ? RunScope.Selection : RunScope.Model;

        Profiles = new ObservableCollection<ProfileEntry>(Library.List());
        var (entry, mode, warning, modeMissing) = ProfileChoice.Resolve(Profiles, ReadBinding(uiDocument.Document), DocumentTitle, _settings.LastProfileId);
        Warning = warning;
        _preferredMode = mode;
        _noAutoMode = modeMissing;
        SelectedProfile = entry;
    }

    public ProfileLibrary Library { get; private set; }

    public IReadOnlyList<string> SharedFolders => _settings.SharedFolders;

    public ClassifierSettings Settings => _settings;

    private ProfileLibrary CreateLibrary() => new(ProfileLibrary.DefaultPersonalFolder, _settings.SharedFolders, BuiltinProfiles.Load);

    /// <summary>Сохранить общие папки и перечитать библиотеку.</summary>
    public void SetSharedFolders(IReadOnlyList<string> folders)
    {
        _settings.SharedFolders = folders.ToList();
        _settings.Save(ClassifierSettings.DefaultPath);
        Library = CreateLibrary();
        Reload(SelectedProfile?.Profile?.Id);
    }

    [RelayCommand]
    private void Import() => ImportRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Folders() => FoldersRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void AiExport() => AiExportRequested?.Invoke(this, EventArgs.Empty);
    public ObservableCollection<ProfileEntry> Profiles { get; }
    public string DocumentTitle { get; }
    public int SelectedCount { get; }
    public bool HasSelection => SelectedCount > 0;
    public string SelectionLabel => $"Выбранные элементы ({SelectedCount})";

    public bool HasIssues => Issues.Count > 0;
    public string IssuesLabel => $"Ошибок в профиле: {Issues.Count} — открыть";
    public bool HasWarning => !string.IsNullOrEmpty(Warning);
    public bool CanStart => _compiled != null && SelectedSection != null;

    /// <summary>Рабочая копия выбранного профиля (для редактора).</summary>
    public ClassifierProfile? Profile => SelectedProfile?.Profile;

    partial void OnWarningChanged(string? value) => OnPropertyChanged(nameof(HasWarning));

    partial void OnIssuesChanged(IReadOnlyList<ProfileIssue> value)
    {
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(IssuesLabel));
    }

    partial void OnSelectedSectionChanged(ModeDef? value) => StartCommand.NotifyCanExecuteChanged();

    partial void OnSelectedProfileChanged(ProfileEntry? value)
    {
        if (value?.Profile == null)
        {
            _compiled = null;
            Issues = value?.Error != null ? [new ProfileIssue("Файл", value.Error)] : [];
            Sections = [];
            SelectedSection = null;
            StartCommand.NotifyCanExecuteChanged();
            return;
        }

        var (compiled, issues) = ProfileCompiler.Compile(value.Profile);
        _compiled = compiled;
        Issues = issues;
        Sections = value.Profile.Modes;

        var detected = compiled != null ? ModeDetector.DetectMode(compiled, DocumentTitle) : null;
        var key = _preferredMode ?? detected;
        _preferredMode = null;
        IsSectionDetected = key != null && key == detected && !_noAutoMode;
        SelectedSection = _noAutoMode ? null : Sections.FirstOrDefault(s => s.Key == key) ?? Sections.FirstOrDefault();
        _noAutoMode = false;
        StartCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Перечитать библиотеку (после редактора) и выбрать профиль по Id.</summary>
    public void Reload(Guid? select)
    {
        var mode = SelectedSection?.Key;
        Profiles.Clear();
        foreach (var entry in Library.List()) Profiles.Add(entry);
        _preferredMode = mode;
        var chosen = Profiles.FirstOrDefault(p => p.Profile?.Id == select) ?? Profiles.FirstOrDefault(p => p.Profile?.Id == SelectedProfile?.Profile?.Id) ??
                     Profiles.First();
        SelectedProfile = null;
        SelectedProfile = chosen;
    }

    private static ModelBinding? ReadBinding(Document document)
    {
        try
        {
            return ProfileBinding.Read(document);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return null;
        }
    }

    [RelayCommand]
    private void Configure() => ConfigureRequested?.Invoke(this, null);

    [RelayCommand]
    private void OpenIssues() => ConfigureRequested?.Invoke(this, Issues.FirstOrDefault());

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        if (_compiled == null || SelectedSection == null) return;

        ClassifierReport? report;
        try
        {
            report = _service.Run(_compiled, SelectedSection.Key, Scope, KeepExisting);
        }
        catch (OperationCanceledException)
        {
            ToadDialogService.Show("Прервано", "Заполнение прервано, изменения в модели откачены.",
                DialogButtons.OK, DialogIcon.Info);
            return;
        }
        catch (Exception ex)
        {
            ToadDialogService.Show("Ошибка!", "Не удалось заполнить код по классификатору:\n" + ex.Message,
                DialogButtons.OK, DialogIcon.Error);
            return;
        }

        // Пользователь отказался добавлять параметры — остаёмся в окне.
        if (report == null)
            return;

        _settings.LastProfileId = _compiled.Source.Id;
        _settings.Save(ClassifierSettings.DefaultPath);

        if (report.Total == 0)
        {
            var code = _compiled.Source.Outputs.First(o => o.Field == "code").Parameter;
            ToadDialogService.Show("Нет элементов", $"Не найдено элементов с параметром \"{code}\".", DialogButtons.OK, DialogIcon.Warning);
            return;
        }

        Completed?.Invoke(this, report);
    }
}
