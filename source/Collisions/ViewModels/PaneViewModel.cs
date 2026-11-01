using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Collisions.Core;
using ToadTools.Connection;

namespace Collisions.ViewModels;

/// <summary>Экран панели.</summary>
public enum PaneScreen
{
    NotConnected,
    NoDocument,
    Resolving,
    NotFound,
    Choose,
    Ready,
    Error
}

/// <summary>Что панель помнит до закрытия Revit: отмечен ли заход и выбранные фильтры.</summary>
public sealed class PaneSession
{
    public bool Visited { get; set; }
    public CollisionQuery Query { get; set; } = new();
}

/// <summary>Вариант фильтра: значение для сервера и подпись с числом.</summary>
public sealed record FilterOption(string? Value, string Label);

/// <summary>
///     Панель «Коллизии ToadFlow»: какая модель открыта, её коллизии из ToadFlow и фильтры. Сеть ждётся без блокировки, а
///     продолжения идут в контексте вызова (в Revit — UI-поток панели). Revit — только через <see cref="IRevitBridge" />.
/// </summary>
public sealed partial class PaneViewModel : ObservableObject
{
    private readonly IClashApi _api;
    private readonly IRevitBridge _revit;
    private readonly ConnectionStore _store;
    private readonly ModelChoices _choices;
    private readonly ThumbnailCache _thumbnails;
    private readonly PaneSession _session;
    private readonly Func<bool> _connect;

    private CollisionListState? _state;
    private string? _documentPath;
    private int _version;
    private bool _quiet;

    [ObservableProperty] private PaneScreen _screen = PaneScreen.NoDocument;
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string? _searchedPath;
    [ObservableProperty] private IReadOnlyList<ModelMatch> _matches = [];
    [ObservableProperty] private string _header = "";
    [ObservableProperty] private string _parts = "";
    [ObservableProperty] private string _counters = "";
    [ObservableProperty] private bool _hasMore;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private CollisionItemViewModel? _selectedItem;

    [ObservableProperty] private IReadOnlyList<FilterOption> _statusOptions = [];
    [ObservableProperty] private IReadOnlyList<FilterOption> _pairOptions = [];
    [ObservableProperty] private IReadOnlyList<FilterOption> _levelOptions = [];
    [ObservableProperty] private string? _statusFilter;
    [ObservableProperty] private string? _groupFilter;
    [ObservableProperty] private string? _pairFilter;
    [ObservableProperty] private string? _levelFilter;
    [ObservableProperty] private string _search = "";

    public PaneViewModel(IClashApi api, IRevitBridge revit, ConnectionStore store, ModelChoices choices, ThumbnailCache thumbnails, PaneSession session,
        Func<bool> connect)
    {
        (_api, _revit, _store, _choices, _thumbnails, _session, _connect) = (api, revit, store, choices, thumbnails, session, connect);
        ShowQuery(session.Query);
    }

    public ObservableCollection<CollisionItemViewModel> Items { get; } = [];

    public IReadOnlyList<FilterOption> GroupOptions { get; } =
        [new("all", "Все"), new("mine", CollisionItemViewModel.MineGroup), new("other", CollisionItemViewModel.OtherGroup)];

    /// <summary>Выбрана строка: панель Revit переходит к коллизии (задача карточки).</summary>
    public event EventHandler<ModelCollisionRow>? NavigateRequested;

    /// <summary>Модель ToadFlow открытого документа; null — не определена.</summary>
    public ModelMatch? Model { get; private set; }

    /// <summary>Открытие панели или смена документа: определить модель и показать её коллизии.</summary>
    public Task ActivateAsync() => RunAsync(async version =>
    {
        if (_store.Load() is null)
        {
            Show(PaneScreen.NotConnected, "Войдите в ToadFlow, чтобы видеть коллизии");
            return;
        }

        var document = await _revit.ActiveDocumentAsync();
        if (version != _version) return;
        if (document is not { } doc)
        {
            Show(PaneScreen.NoDocument, "Откройте модель проекта");
            return;
        }

        _documentPath = doc.CentralPath ?? doc.FileName;
        Show(PaneScreen.Resolving, "Ищем модель в ToadFlow…");
        var resolved = await _api.ResolveAsync(doc.CentralPath, doc.FileName, CancellationToken.None);
        if (version != _version) return;

        if (resolved.Matches.Count == 0)
        {
            SearchedPath = _documentPath;
            Show(PaneScreen.NotFound, "Модель не участвует в проверках коллизий ToadFlow");
            return;
        }

        var match = resolved.Matches.Count == 1
            ? resolved.Matches[0]
            : _choices.Get(_documentPath) is { } key ? resolved.Matches.FirstOrDefault(m => m.Key == key) : null;
        if (match is null)
        {
            Matches = resolved.Matches;
            Show(PaneScreen.Choose, "Таких моделей в ToadFlow несколько — выберите свою");
            return;
        }

        await LoadModelAsync(match, version);
    });

    [RelayCommand]
    private Task ChooseModel(ModelMatch? match) => match is null
        ? Task.CompletedTask
        : RunAsync(version =>
        {
            if (_documentPath is not null) _choices.Set(_documentPath, match.Key);
            return LoadModelAsync(match, version);
        });

    [RelayCommand]
    private Task Refresh() => Screen == PaneScreen.Ready && _state is { } state
        ? RunAsync(async version =>
        {
            await state.RefreshAsync();
            if (version == _version) ShowList(state, rebuild: true);
        })
        : ActivateAsync();

    [RelayCommand]
    private Task Connect() => _connect() ? ActivateAsync() : Task.CompletedTask;

    [RelayCommand]
    private Task LoadMore() => _state is { HasMore: true } state
        ? RunAsync(async version =>
        {
            await state.MoreAsync();
            if (version == _version) ShowList(state, rebuild: false);
        })
        : Task.CompletedTask;

    [RelayCommand]
    private Task ApplyFilters()
    {
        var query = new CollisionQuery(StatusFilter ?? "open", GroupFilter ?? "all", PairFilter, LevelFilter, string.IsNullOrWhiteSpace(Search) ? null : Search.Trim());
        _session.Query = query;
        return _state is { } state && Screen == PaneScreen.Ready
            ? RunAsync(async version =>
            {
                await state.SetQueryAsync(query);
                if (version == _version) ShowList(state, rebuild: true);
            })
            : Task.CompletedTask;
    }

    partial void OnSelectedItemChanged(CollisionItemViewModel? value)
    {
        if (_state is { } state) state.Selected = value?.Row;
        if (!_quiet && value is not null) NavigateRequested?.Invoke(this, value.Row);
    }

    partial void OnStatusFilterChanged(string? value) => FilterChanged();
    partial void OnGroupFilterChanged(string? value) => FilterChanged();
    partial void OnPairFilterChanged(string? value) => FilterChanged();
    partial void OnLevelFilterChanged(string? value) => FilterChanged();

    private void FilterChanged()
    {
        if (!_quiet) _ = ApplyFiltersCommand.ExecuteAsync(null);
    }

    private async Task LoadModelAsync(ModelMatch match, int version)
    {
        Model = match;
        Header = string.IsNullOrWhiteSpace(match.Discipline) ? match.Name : $"{match.Name} · {match.Discipline}";
        Parts = string.Join("; ", match.Parts.Select(p => p.LastApprovedRunAt is { } at ? $"{p.Name} — прогон {at.ToLocalTime():dd.MM.yyyy HH:mm}" : p.Name));

        var state = new CollisionListState(_api, match.Key);
        await state.SetQueryAsync(_session.Query);
        if (version != _version) return;
        _state = state;
        ShowList(state, rebuild: true);
        Show(PaneScreen.Ready, "");

        if (_session.Visited) return;
        try
        {
            await _api.VisitAsync(CancellationToken.None);
            _session.Visited = true;
        }
        catch (Exception exception) when (exception is ToadFlowUnavailableException or ToadFlowRequestException)
        {
            // заход отметится при следующем открытии списка
        }
    }

    private void ShowList(CollisionListState state, bool rebuild)
    {
        _quiet = true;
        try
        {
            if (rebuild)
            {
                Items.Clear();
                foreach (var row in state.Items) Items.Add(new CollisionItemViewModel(row, _thumbnails));
            }
            else
            {
                foreach (var row in state.Items.Skip(Items.Count)) Items.Add(new CollisionItemViewModel(row, _thumbnails));
            }

            SelectedItem = state.Selected is { } selected ? Items.FirstOrDefault(i => i.Row.Collision.Id == selected.Collision.Id) : null;
            HasMore = state.HasMore;
            Counters = $"мне править {state.Mine}, взяла другая сторона {state.Other}, новых с прошлого захода {state.NewSinceVisit}";

            StatusOptions = state.Facets.Statuses.Select(f => new FilterOption(f.Value, $"{f.Label} ({f.Count})")).ToList();
            PairOptions = [new FilterOption(null, "Все пары"), ..state.Facets.Pairs.Select(f => new FilterOption(f.Value, $"{f.Label} ({f.Count})"))];
            LevelOptions = [new FilterOption(null, "Все уровни"), ..state.Facets.Levels.Select(f => new FilterOption(f.Value, $"{f.Label} ({f.Count})"))];
            ShowQuery(state.Query);
        }
        finally
        {
            _quiet = false;
        }
    }

    private void ShowQuery(CollisionQuery query)
    {
        var quiet = _quiet;
        _quiet = true;
        (StatusFilter, GroupFilter, PairFilter, LevelFilter, Search) = (query.Status, query.Group, query.Pair, query.Level, query.Search ?? "");
        _quiet = quiet;
    }

    private void Show(PaneScreen screen, string message) => (Screen, Message) = (screen, message);

    /// <summary>Шаг панели с общей обработкой ошибок; результаты устаревшего шага отбрасываются по номеру версии.</summary>
    private async Task RunAsync(Func<int, Task> step)
    {
        var version = ++_version;
        IsBusy = true;
        try
        {
            await step(version);
        }
        catch (ToadFlowAuthRequiredException exception)
        {
            if (version == _version) Show(PaneScreen.NotConnected, exception.Message);
        }
        catch (Exception exception) when (exception is ToadFlowUnavailableException or ToadFlowRequestException)
        {
            if (version == _version) Show(PaneScreen.Error, exception.Message);
        }
        finally
        {
            if (version == _version) IsBusy = false;
        }
    }
}
