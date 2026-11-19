using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Library;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Editor.ViewModels;

/// <summary>Раздел редактора в левом списке.</summary>
/// <param name="Hint">Что здесь делают и когда сюда идти — строка над вкладкой.</param>
/// <param name="Advanced">Видна только при «Все настройки».</param>
public sealed record EditorTab(string Title, string Icon, object Content, string Hint, bool Advanced = false);

/// <summary>
///     Редактор профиля: правит рабочую копию, после каждой правки пересобирает её и показывает ошибки.
///     Встроенный и общий «только чтение» профиль можно править, но сохранить — только копией.
/// </summary>
public sealed partial class ProfileEditorViewModel : ObservableObject
{
    private readonly ProfileLibrary _library;
    private readonly Action<Action> _schedule;
    private readonly ClassifierSettings _settings;
    private readonly string? _settingsPath;
    private bool _recompilePending;
    private bool _showAll;

    [ObservableProperty] private ProfileEntry _entry;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private EditorTab? _selectedTab;
    [ObservableProperty] private CompiledProfile? _compiled;

    /// <param name="settingsPath">Куда сохранять «Все настройки»; null — не сохранять.</param>
    public ProfileEditorViewModel(ProfileEntry entry, ProfileLibrary library, IEditorHost host, IDialogs dialogs, Action<Action>? schedule = null,
        ClassifierSettings? settings = null, string? settingsPath = null)
    {
        _entry = entry;
        _library = library;
        _settings = settings ?? new ClassifierSettings();
        _settingsPath = settingsPath;
        _showAll = _settings.ShowAllEditorTabs;
        _schedule = schedule ?? (action => action());
        Host = host;
        Dialogs = dialogs;
        Profile = ProfileSerializer.Clone(entry.Profile ?? throw new ArgumentException("Профиль не прочитан", nameof(entry)));

        General = new GeneralTabViewModel(this);
        Positions = new PositionsTabViewModel(this);
        Mapping = new MappingTabViewModel(this);
        Dictionaries = new DictionariesTabViewModel(this);
        Modes = new ModesTabViewModel(this);
        Exceptions = new ExceptionsTabViewModel(this);
        Logic = new LogicTabViewModel(this);
        Check = new CheckTabViewModel(this);
        Tabs =
        [
            new EditorTab("Проверка", "ListCheck", Check,
                "Почему элемент получил такой код: прогон по модели или выбранным элементам и трасса решения. Код неверный — начинайте отсюда."),
            new EditorTab("Слова и шифры", "Tags", Dictionaries,
                "Слова в наименованиях и шифры систем, по которым узнаются тип и раздел. Сюда — если элемент не распознан или попал не в тот раздел."),
            new EditorTab("Сопоставление", "LayoutList", Mapping,
                "Таблица «раздел + тип → код». Сюда — если тип и раздел определились верно, а код не тот."),
            new EditorTab("Исключения", "Filter", Exceptions,
                "Правила объекта, которые срабатывают раньше распознавания: «это всегда такой код» или «не заполнять». Для частных случаев объекта."),
            new EditorTab("Общее", "Settings", General,
                "Название профиля, параметры, куда пишутся код и описание, обрабатываемые категории.", true),
            new EditorTab("Классификатор", "List", Positions,
                "Позиции заказчика: код и описание. Импорт из Excel.", true),
            new EditorTab("Режимы", "Adjustments", Modes,
                "Разделы моделей в окне запуска и их шифры в имени файла.", true),
            new EditorTab("Логика", "Tool", Logic,
                "Порядок правил, которыми определяются тип и раздел. Для тонкой настройки.", true)
        ];
        SelectedTab = Tabs[0];
        Recompile();
    }

    /// <summary>Показывать все вкладки; по умолчанию — четыре основные. Запоминается.</summary>
    public bool ShowAll
    {
        get => _showAll;
        set
        {
            if (!SetProperty(ref _showAll, value)) return;
            OnPropertyChanged(nameof(VisibleTabs));
            if (SelectedTab is { Advanced: true } && !value) SelectedTab = Tabs[0];
            _settings.ShowAllEditorTabs = value;
            if (_settingsPath != null) _settings.Save(_settingsPath);
        }
    }

    public IReadOnlyList<EditorTab> VisibleTabs => ShowAll ? Tabs : Tabs.Where(t => !t.Advanced).ToList();

    public IEditorHost Host { get; }
    public IDialogs Dialogs { get; }

    /// <summary>Рабочая копия; вкладки правят её напрямую и зовут <see cref="MarkChanged" />.</summary>
    public ClassifierProfile Profile { get; }

    public IReadOnlyList<EditorTab> Tabs { get; }
    public GeneralTabViewModel General { get; }
    public PositionsTabViewModel Positions { get; }
    public MappingTabViewModel Mapping { get; }
    public DictionariesTabViewModel Dictionaries { get; }
    public ModesTabViewModel Modes { get; }
    public ExceptionsTabViewModel Exceptions { get; }
    public LogicTabViewModel Logic { get; }
    public CheckTabViewModel Check { get; }

    public ObservableCollection<ProfileIssue> Issues { get; } = [];
    public bool HasIssues => Issues.Count > 0;
    public string IssuesLabel => Issues.Count == 0 ? "Ошибок нет" : $"Ошибок: {Issues.Count}";

    public bool IsReadOnly => Entry.ReadOnly || Entry.Origin == ProfileOrigin.Builtin;

    public string ReadOnlyHint => Entry.Origin == ProfileOrigin.Builtin
        ? "Встроенный профиль не меняется: правки сохраняются копией в вашу папку."
        : "Профиль в папке только для чтения: правки сохраняются копией в вашу папку.";

    public string Title => "Профиль: " + Profile.Name + (IsDirty ? " *" : string.Empty);

    /// <summary>Id сохранённого профиля — чтобы окно запуска выбрало его.</summary>
    public Guid? SavedProfileId { get; private set; }

    /// <summary>Профиль пересобран (вкладки обновляют вычисляемое: пробелы, проверку).</summary>
    public event EventHandler? Recompiled;

    partial void OnEntryChanged(ProfileEntry value)
    {
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(ReadOnlyHint));
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(Title));
        SaveCommand.NotifyCanExecuteChanged();
    }

    public void MarkChanged()
    {
        IsDirty = true;
        OnPropertyChanged(nameof(Title));
        if (_recompilePending) return;
        _recompilePending = true;
        _schedule(() =>
        {
            _recompilePending = false;
            Recompile();
        });
    }

    public void Recompile()
    {
        var (compiled, issues) = ProfileCompiler.Compile(Profile);
        Compiled = compiled;
        Issues.Clear();
        foreach (var issue in issues) Issues.Add(issue);
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(IssuesLabel));
        Recompiled?.Invoke(this, EventArgs.Empty);
    }

    private bool CanSave() => !IsReadOnly && IsDirty;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => TrySave(Entry.Path);

    [RelayCommand]
    private void SaveAsCopy()
    {
        // Копия — та же рабочая копия с новым Id: вкладки держат её объекты, поэтому граф не заменяется.
        // Переименованный перед сохранением профиль сохраняет имя пользователя; иначе — «… (копия)».
        var (oldId, oldName) = (Profile.Id, Profile.Name);
        Profile.Id = Guid.NewGuid();
        if (Profile.Name == Entry.Profile?.Name) Profile.Name += " (копия)";
        if (!TrySave(null))
        {
            Profile.Id = oldId;
            Profile.Name = oldName;
        }

        General.Reload();
        OnPropertyChanged(nameof(Title));
    }

    [RelayCommand]
    private void Export()
    {
        var path = Host.AskSavePath(Profile.Name + ProfileLibrary.Extension);
        if (path == null) return;
        _library.Export(Profile, path);
    }

    private bool TrySave(string? path)
    {
        try
        {
            // Своя же запись: проверяем, что файл не изменили с момента открытия (общая папка).
            Entry = _library.Save(Profile, path, path != null && path == Entry.Path ? Entry.FileTimeUtc : null);
            SavedProfileId = Profile.Id;
            IsDirty = false;
            return true;
        }
        catch (Exception exception) when (exception is ProfileSaveException or IOException or UnauthorizedAccessException)
        {
            Dialogs.Info("Профиль не сохранён", exception.Message);
            return false;
        }
    }

    /// <summary>Можно ли закрыть окно: при несохранённых правках спрашивает.</summary>
    public bool ConfirmClose()
    {
        if (!IsDirty) return true;
        var answer = Dialogs.Ask("Несохранённые правки",
            IsReadOnly ? "Сохранить правки копией профиля в вашу папку?" : "Сохранить изменения профиля?");
        if (answer == null) return false;
        if (answer == false) return true;
        if (IsReadOnly)
        {
            SaveAsCopy();
            return !IsDirty;
        }

        return TrySave(Entry.Path);
    }

    /// <summary>Перейти к вкладке, где правится место ошибки (по первому сегменту пути).</summary>
    public void GoToIssue(ProfileIssue issue)
    {
        var first = issue.Path.Split('›')[0].Trim();
        object target = first switch
        {
            "Параметры" => General,
            "Классификатор" => Positions,
            "Таблицы" => Mapping,
            "Разделы" or "Типы" or "Словари" or "Наборы шаблонов" => Dictionaries,
            "Режимы" when issue.Path.Contains("правила раздела") => Logic,
            "Режимы" => Modes,
            "Исключения" => Exceptions,
            _ => Logic
        };
        var tab = Tabs.First(t => t.Content == target);
        // Ошибка на скрытой вкладке — показать все вкладки на этот раз, не меняя запомненный выбор.
        if (tab.Advanced && !_showAll)
        {
            _showAll = true;
            OnPropertyChanged(nameof(ShowAll));
            OnPropertyChanged(nameof(VisibleTabs));
        }
        SelectedTab = tab;
    }

    [RelayCommand]
    private void OpenIssue(ProfileIssue? issue)
    {
        if (issue != null) GoToIssue(issue);
    }
}
