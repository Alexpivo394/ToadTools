using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Alexpivo394.Revit.ParamCheck.Profiles;
using Alexpivo394.Revit.Export.Sources;

namespace ParamChecker.ViewModels;

/// <summary>
///     Выбор моделей с Revit Server. Дерево берётся из <see cref="ServerCatalog" />: сервер обходится в фоне один раз
///     за сеанс Revit (и хранится на диске), поэтому окно открывается сразу, а поиск ищет по всему серверу.
///     Отмечать можно сколько угодно моделей и целые папки — в профиль они добавляются одной кнопкой.
/// </summary>
public sealed partial class ServerPickerViewModel : ObservableObject
{
    private const int MaxSearchResults = 300;

    private readonly CheckerContext _context;
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _refreshTimer;
    private ServerCatalog? _catalog;
    private bool _refreshPending;

    [ObservableProperty] private string _server;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsSearching), nameof(EmptyText))] private string _search = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasError), nameof(EmptyText))] private string? _error;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(EmptyText))] private bool _isRefreshing;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AddText))] [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    private int _selectedCount;

    /// <param name="existing">Модели профиля (ключи <see cref="ModelNames.Key" />).</param>
    /// <param name="knownPaths">Пути моделей всех профилей — из них берутся серверы, если их нет в RSN.ini.</param>
    public ServerPickerViewModel(CheckerContext context, IReadOnlyCollection<string> existing, IEnumerable<string> knownPaths)
    {
        _context = context;
        Existing = existing;
        foreach (var server in KnownServers(context.RevitYear, knownPaths)) Servers.Add(server);
        _server = context.Settings.Server.Trim().Length > 0 ? context.Settings.Server.Trim() : Servers.FirstOrDefault() ?? "";

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _refreshTimer.Tick += (_, _) => ApplyCatalogChanges();
        Attach();
    }

    public ObservableCollection<string> Servers { get; } = [];
    public ObservableCollection<ServerNodeViewModel> Roots { get; } = [];
    public ObservableCollection<ServerNodeViewModel> SearchResults { get; } = [];

    /// <summary>Модели, которые уже есть в профиле, — они показаны отмеченными и не меняются.</summary>
    internal IReadOnlyCollection<string> Existing { get; }

    internal ServerCatalog? Catalog => _catalog;

    public string ServerVersionText => $"Revit Server {_context.RevitYear}";

    public bool IsSearching => Search.Trim().Length > 0;

    public bool HasError => Error is not null;

    public string AddText => SelectedCount == 0 ? "Добавить" : $"Добавить ({SelectedCount})";

    public string EmptyText => IsSearching ? "" : _catalog is null
        ? "Выберите сервер в списке или впишите его имя"
        : IsRefreshing ? "Читаем сервер…" : HasError ? "" : "На сервере нет моделей";

    public IReadOnlyList<string> SelectedPaths() => _selected.ToList();

    /// <summary>Серверы из RSN.ini и из путей моделей в профилях — без RSN.ini (например, из дома через VPN) список не пустой.</summary>
    public static IReadOnlyList<string> KnownServers(string revitYear, IEnumerable<string> paths) =>
        RsnConfig.ReadServers(RsnConfig.DefaultPath(revitYear))
            .Concat(paths.Select(ModelNames.ServerOf).OfType<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    internal bool IsSelected(string modelPath) => _selected.Contains(modelPath) || Existing.Contains(modelPath);

    internal void SetSelected(string modelPath, bool value)
    {
        if (Existing.Contains(modelPath)) return;
        if (value) _selected.Add(modelPath);
        else _selected.Remove(modelPath);
        SelectedCount = _selected.Count;
    }

    /// <summary>Перерисовать галочки после отметки: у папок они — сводка по содержимому.</summary>
    internal void RefreshChecks()
    {
        foreach (var node in Roots.SelectMany(root => root.Descendants().Prepend(root))) node.RefreshCheck();
        foreach (var node in SearchResults) node.RefreshCheck();
    }

    internal void ReportError(string message) => Error = message;

    /// <summary>Окно закрыто — каталогу больше незачем звать это окно.</summary>
    public void Detach()
    {
        if (_catalog is not null) _catalog.Changed -= OnCatalogChanged;
        _refreshTimer.Stop();
    }

    private bool CanAccept() => SelectedCount > 0;

    [RelayCommand(CanExecute = nameof(CanAccept))]
    private void Accept()
    {
        // Команда только разрешает кнопку; закрывает окно сам вид.
    }

    [RelayCommand]
    private void Refresh() => _ = _catalog?.RefreshAsync();

    [RelayCommand]
    private void ClearSelection()
    {
        _selected.Clear();
        SelectedCount = 0;
        RefreshChecks();
    }

    partial void OnServerChanged(string value)
    {
        _context.Settings.Server = value.Trim();
        Attach();
    }

    partial void OnSearchChanged(string value) => RebuildSearch();

    private void Attach()
    {
        Detach();
        Roots.Clear();
        SearchResults.Clear();
        Error = null;

        var server = Server.Trim();
        if (server.Length == 0)
        {
            _catalog = null;
            Status = "";
            OnPropertyChanged(nameof(EmptyText));
            return;
        }

        _catalog = ServerCatalog.For(server, _context.RevitYear);
        _catalog.Changed += OnCatalogChanged;
        RebuildTree();
        _ = _catalog.EnsureFreshAsync();
        UpdateStatus();
    }

    /// <summary>Каталог сообщает о каждой прочитанной папке из фонового потока — собираем это в одно обновление окна.</summary>
    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        if (_refreshPending) return;
        _refreshPending = true;
        _refreshTimer.Dispatcher.BeginInvoke(() => _refreshTimer.Start());
    }

    private void ApplyCatalogChanges()
    {
        _refreshTimer.Stop();
        _refreshPending = false;
        if (_catalog is null) return;

        if (!_catalog.IsRefreshing || Roots.Count == 0) RebuildTree();
        else foreach (var node in Roots.SelectMany(root => root.Descendants().Prepend(root))) node.FillFromCatalog();

        if (IsSearching) RebuildSearch();
        UpdateStatus();
    }

    private void RebuildTree()
    {
        if (_catalog is null) return;

        var expanded = new HashSet<string>(Roots.SelectMany(root => root.Descendants().Prepend(root))
            .Where(node => node.IsFolder && node.IsExpanded).Select(node => node.FolderPath), StringComparer.OrdinalIgnoreCase);

        var entries = _catalog.TryGetChildren(_catalog.RootPath);
        if (entries is null)
        {
            if (!_catalog.IsRefreshing) _ = LoadRootAsync(_catalog);
            return;
        }

        Roots.Clear();
        foreach (var entry in entries) Roots.Add(new ServerNodeViewModel(this, entry, 0, expanded));
    }

    private async Task LoadRootAsync(ServerCatalog catalog)
    {
        try
        {
            await catalog.GetChildrenAsync(catalog.RootPath, CancellationToken.None);
            if (ReferenceEquals(catalog, _catalog)) RebuildTree();
        }
        catch (SourceException exception)
        {
            if (ReferenceEquals(catalog, _catalog)) Error = Unreachable(exception.Message);
        }
    }

    private void RebuildSearch()
    {
        SearchResults.Clear();
        var text = Search.Trim();
        if (text.Length == 0 || _catalog is null) return;

        foreach (var entry in _catalog.AllModels()
                     .Where(entry => entry.ModelPath.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0)
                     .Take(MaxSearchResults))
            SearchResults.Add(new ServerNodeViewModel(this, entry, 0, null));
    }

    private static string Unreachable(string error) => $"{error.TrimEnd('.')}. Проверьте имя сервера и подключение к сети (VPN).";

    private void UpdateStatus()
    {
        if (_catalog is null) return;

        IsRefreshing = _catalog.IsRefreshing;
        OnPropertyChanged(nameof(EmptyText));
        var models = _catalog.AllModels().Count;
        var known = models == 0 ? "" : Words.Count(models, "модель", "модели", "моделей");

        if (_catalog.IsRefreshing)
            Status = $"Обновляем каталог… прочитано {Words.Count(_catalog.FoldersRead, "папка", "папки", "папок")}" + (known.Length > 0 ? $", известно {known}" : "");
        else if (_catalog.UpdatedAt is { } updated)
            Status = $"{known} · каталог от {updated:dd.MM HH:mm}";
        else
            Status = known;

        if (!_catalog.IsRefreshing && _catalog.LastError is { } error)
            Error = _catalog.TryGetChildren(_catalog.RootPath) is null
                ? Unreachable(error)
                : $"Часть папок не прочиталась, для них показано прежнее содержимое: {error}";
    }
}

/// <summary>Папка или модель в дереве выбора. Галочка папки — сводка по её моделям: все, ни одной или часть.</summary>
public sealed partial class ServerNodeViewModel : ObservableObject
{
    private readonly ServerPickerViewModel _owner;
    private readonly HashSet<string>? _expandedOnBuild;
    private bool _filled;

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isLoading;

    internal ServerNodeViewModel(ServerPickerViewModel owner, SourceEntry entry, int depth, HashSet<string>? expanded)
    {
        _owner = owner;
        _expandedOnBuild = expanded;
        Entry = entry;
        Depth = depth;
        if (entry.IsFolder)
        {
            Children.Add(Placeholder);
            if (expanded?.Contains(entry.FolderPath) == true) IsExpanded = true;
        }
    }

    private ServerNodeViewModel(ServerPickerViewModel owner)
    {
        _owner = owner;
        Entry = new SourceEntry("Загрузка…", "", "", false);
        IsPlaceholder = true;
    }

    private ServerNodeViewModel Placeholder => new(_owner);

    public SourceEntry Entry { get; }
    public string Name => Entry.Name;
    public bool IsFolder => Entry.IsFolder;
    public string FolderPath => Entry.FolderPath;
    public bool IsPlaceholder { get; }
    public int Depth { get; }
    public ObservableCollection<ServerNodeViewModel> Children { get; } = [];

    /// <summary>Модель уже в профиле.</summary>
    public bool IsExisting => !IsFolder && _owner.Existing.Contains(Entry.ModelPath);

    public string Hint => IsExisting ? "уже в профиле" : "";

    public bool? IsChecked
    {
        get
        {
            if (IsPlaceholder) return false;
            if (!IsFolder) return _owner.IsSelected(Entry.ModelPath);

            var models = ModelsBelow();
            if (models is null || models.Count == 0) return false;
            var selected = models.Count(_owner.IsSelected);
            return selected == 0 ? false : selected == models.Count ? true : null;
        }
        set => _ = SetCheckedAsync(IsChecked != true);
    }

    internal IEnumerable<ServerNodeViewModel> Descendants() =>
        Children.Where(c => !c.IsPlaceholder).SelectMany(c => c.Descendants().Prepend(c));

    internal void RefreshCheck() => OnPropertyChanged(nameof(IsChecked));

    /// <summary>Раскрытая папка, содержимое которой ещё не было известно, — заполнить из каталога, когда оно появилось.</summary>
    internal void FillFromCatalog()
    {
        if (IsFolder && IsExpanded && !_filled && _owner.Catalog?.TryGetChildren(FolderPath) is { } entries) Fill(entries);
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && IsFolder && !_filled) _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_owner.Catalog is not { } catalog) return;
        if (catalog.TryGetChildren(FolderPath) is { } cached)
        {
            Fill(cached);
            return;
        }

        IsLoading = true;
        try
        {
            Fill(await catalog.GetChildrenAsync(FolderPath, CancellationToken.None));
        }
        catch (SourceException exception)
        {
            _owner.ReportError(exception.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Fill(IReadOnlyList<SourceEntry> entries)
    {
        _filled = true;
        Children.Clear();
        foreach (var entry in entries) Children.Add(new ServerNodeViewModel(_owner, entry, Depth + 1, _expandedOnBuild));
        RefreshCheck();
    }

    /// <summary>Модели в папке и во всех вложенных; null — часть папок ещё не прочитана.</summary>
    private List<string>? ModelsBelow()
    {
        if (_owner.Catalog is not { } catalog) return null;

        var result = new List<string>();
        var folders = new Stack<string>(new[] { FolderPath });
        while (folders.Count > 0)
        {
            var entries = catalog.TryGetChildren(folders.Pop());
            if (entries is null) return null;
            foreach (var entry in entries)
            {
                if (entry.IsFolder) folders.Push(entry.FolderPath);
                else result.Add(entry.ModelPath);
            }
        }

        return result;
    }

    private async Task SetCheckedAsync(bool value)
    {
        if (!IsFolder)
        {
            _owner.SetSelected(Entry.ModelPath, value);
            _owner.RefreshChecks();
            return;
        }

        IsLoading = true;
        try
        {
            foreach (var model in await LoadModelsBelowAsync()) _owner.SetSelected(model, value);
        }
        catch (SourceException exception)
        {
            _owner.ReportError(exception.Message);
        }
        finally
        {
            IsLoading = false;
            _owner.RefreshChecks();
        }
    }

    /// <summary>Все модели папки, догружая с сервера ещё не прочитанные вложенные папки.</summary>
    private async Task<List<string>> LoadModelsBelowAsync()
    {
        var result = new List<string>();
        if (_owner.Catalog is not { } catalog) return result;

        var folders = new Stack<string>(new[] { FolderPath });
        while (folders.Count > 0)
        {
            foreach (var entry in await catalog.GetChildrenAsync(folders.Pop(), CancellationToken.None))
            {
                if (entry.IsFolder) folders.Push(entry.FolderPath);
                else result.Add(entry.ModelPath);
            }
        }

        return result;
    }
}
