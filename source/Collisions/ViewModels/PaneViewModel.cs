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

/// <summary>Фильтры одной модели: пара и уровень — из её фасетов, в другой модели их может не быть.</summary>
public sealed record ListQuery(string? Pair = null, string? Level = null, string? Search = null);

/// <summary>
///     Что панель помнит до закрытия Revit: отмечен ли заход; статус, группа и «новые для меня» — на сеанс, пара, уровень и
///     поиск — по ключу модели. Иначе фильтр пары одной модели молча пустит список другой.
/// </summary>
public sealed class PaneSession
{
    public bool Visited { get; set; }
    public string Status { get; set; } = "open";
    public string Group { get; set; } = "all";
    public string? Since { get; set; }
    public Dictionary<string, ListQuery> ByModel { get; } = [];

    /// <summary>Запрос списка модели: общие фильтры сеанса и её собственные.</summary>
    public CollisionQuery QueryFor(string? modelKey)
    {
        var own = modelKey is not null && ByModel.TryGetValue(modelKey, out var saved) ? saved : new ListQuery();
        return new CollisionQuery(Status, Group, own.Pair, own.Level, own.Search, Since);
    }

    /// <summary>Запомнить фильтры: общие — на сеанс, пару, уровень и поиск — для модели (если она известна).</summary>
    public void Remember(string? modelKey, CollisionQuery query)
    {
        (Status, Group, Since) = (query.Status, query.Group, query.Since);
        if (modelKey is not null) ByModel[modelKey] = new ListQuery(query.Pair, query.Level, query.Search);
    }
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
    private readonly ICardDialogs _dialogs;
    private UserInfo? _me;

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
    [ObservableProperty] private CardViewModel? _card;

    [ObservableProperty] private IReadOnlyList<FilterOption> _statusOptions = [];
    [ObservableProperty] private IReadOnlyList<FilterOption> _pairOptions = [];
    [ObservableProperty] private IReadOnlyList<FilterOption> _levelOptions = [];
    [ObservableProperty] private string? _statusFilter;
    [ObservableProperty] private string? _groupFilter;
    [ObservableProperty] private string? _pairFilter;
    [ObservableProperty] private string? _levelFilter;
    [ObservableProperty] private string _search = "";

    /// <summary>Строка состояния под счётчиками: что случилось со вставленной ссылкой.</summary>
    [ObservableProperty] private string _status = "";

    /// <summary>Кнопка «Новые для меня N»; null — сервер без этого счётчика.</summary>
    [ObservableProperty] private string? _newForMe;

    /// <summary>Кнопка-счётчик «Мне править N»: включает группу «мои и общие».</summary>
    [ObservableProperty] private string _mineCounter = "";

    /// <summary>Кнопка-счётчик «Другая сторона N»: включает группу «взяла другая сторона».</summary>
    [ObservableProperty] private string _otherCounter = "";

    [ObservableProperty] private bool _newForMeActive;

    public PaneViewModel(IClashApi api, IRevitBridge revit, ConnectionStore store, ModelChoices choices, ThumbnailCache thumbnails, PaneSession session,
        Func<bool> connect, ICardDialogs dialogs)
    {
        (_api, _revit, _store, _choices, _thumbnails, _session, _connect, _dialogs) = (api, revit, store, choices, thumbnails, session, connect, dialogs);
        ShowQuery(session.QueryFor(null));
    }

    public ObservableCollection<CollisionItemViewModel> Items { get; } = [];

    /// <summary>Последняя отметка просмотра (идёт в фоне) — для тестов.</summary>
    public Task SeenTask { get; private set; } = Task.CompletedTask;

    public IReadOnlyList<FilterOption> GroupOptions { get; } =
        [new("all", "Все"), new("mine", CollisionItemViewModel.MineGroup), new("other", CollisionItemViewModel.OtherGroup)];

    /// <summary>Модель ToadFlow открытого документа; null — не определена.</summary>
    public ModelMatch? Model { get; private set; }

    /// <summary>
    ///     Открытие панели, смена документа или подключения: определить модель и показать её коллизии. Кто вошёл, спрашивается
    ///     заново — после выхода и входа другим пользователем кнопки карточки по его роли и разделам.
    /// </summary>
    public Task ActivateAsync() => RunAsync(async version =>
    {
        // Карточка открытой коллизии — прежнего документа, модели или пользователя: её кнопки уже не про то, что на экране.
        _me = null;
        Card = null;
        Status = "";
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
            Card = null;
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
    private Task Connect()
    {
        bool connected;
        try
        {
            connected = _connect();
        }
        catch (Exception exception)
        {
            Show(PaneScreen.Error, ToadFlowErrors.Describe(exception));
            return Task.CompletedTask;
        }

        return connected ? ActivateAsync() : Task.CompletedTask;
    }

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
        var query = new CollisionQuery(StatusFilter ?? "open", GroupFilter ?? "all", PairFilter, LevelFilter, string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            NewForMeActive ? "unseen" : null);
        _session.Remember(Model?.Key, query);
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
        if (_quiet || value is null) return;
        _ = OpenAsync(value);
    }

    /// <summary>Перейти к соседней строке (↑/↓); ниже последней строки — «Показать ещё» и дальше.</summary>
    public async Task MoveAsync(int step)
    {
        var next = (SelectedItem is { } current ? Items.IndexOf(current) : -1) + step;
        if (next >= Items.Count && HasMore) await LoadMoreCommand.ExecuteAsync(null);
        if (next >= 0 && next < Items.Count) SelectedItem = Items[next];
    }

    /// <summary>
    ///     Коллизия в Revit (вид «ToadClash», куб, выбор) и её карточка. Ошибка Revit не мешает карточке, ошибка сети не
    ///     мешает виду. Не бросает: вызывается и без ожидания — при выборе строки.
    /// </summary>
    public async Task OpenAsync(CollisionItemViewModel item)
    {
        try
        {
            await OpenCoreAsync(item);
        }
        catch (Exception exception)
        {
            ReportUnexpected(exception);
        }
    }

    private async Task OpenCoreAsync(CollisionItemViewModel item)
    {
        if (!ReferenceEquals(SelectedItem, item))
        {
            _quiet = true;
            SelectedItem = item;
            _quiet = false;
        }

        var show = NavigateAsync(item);
        try
        {
            _me ??= await _api.MeAsync(CancellationToken.None);
        }
        catch (ToadFlowAuthRequiredException exception)
        {
            Show(PaneScreen.NotConnected, exception.Message);
            return;
        }
        catch (Exception exception)
        {
            Show(PaneScreen.Error, ToadFlowErrors.Describe(exception));
            return;
        }

        if (!ReferenceEquals(SelectedItem, item)) return;
        var card = new CardViewModel(_api, item.Row, _me, _dialogs, _thumbnails, _revit);
        card.Changed += OnCardChanged;
        Card = card;
        SeenTask = MarkSeenAsync(item);
        var load = card.LoadAsync();
        var outcome = await show;
        // Пропущенный переход (строку уже сменили) и устаревшая карточка ничего не пишут.
        if (!outcome.Skipped && ReferenceEquals(Card, card))
        {
            if (outcome.Result is { } result) card.ShowNavigation(result);
            else card.NavigateMessage = $"Не получилось показать коллизию в Revit: {outcome.Error}";
        }

        await load;
    }

    /// <summary>Действие в карточке прошло — список обновляется. async void: исключение отсюда уронило бы Revit.</summary>
    private async void OnCardChanged(object? sender, EventArgs e)
    {
        try
        {
            await RefreshCommand.ExecuteAsync(null);
        }
        catch (Exception exception)
        {
            ReportUnexpected(exception);
        }
    }

    /// <summary>
    ///     Непредвиденная ошибка там, где её некому показать (обработчики клавиш, события): на списке — строкой состояния,
    ///     на других экранах — экраном ошибки.
    /// </summary>
    public void ReportUnexpected(Exception exception)
    {
        var text = ToadFlowErrors.Describe(exception);
        if (Screen == PaneScreen.Ready) Status = text;
        else Show(PaneScreen.Error, text);
    }

    /// <summary>
    ///     Показанный документ закрывается: список, карточка и выбор модели сбрасываются, начатые шаги отбрасываются. Модель
    ///     определится заново, когда активным станет другой документ.
    /// </summary>
    public void DocumentClosed()
    {
        _version++;
        IsBusy = false;
        (_state, Model, _documentPath, Card) = (null, null, null, null);
        _quiet = true;
        try
        {
            SelectedItem = null;
            Items.Clear();
        }
        finally
        {
            _quiet = false;
        }

        Status = "";
        Show(PaneScreen.NoDocument, "Откройте модель проекта");
    }


    /// <summary>«Новые для меня»: включить или снять фильтр <c>since=unseen</c>.</summary>
    [RelayCommand]
    private Task NewForMeFilter()
    {
        NewForMeActive = !NewForMeActive;
        return ApplyFilters();
    }

    /// <summary>
    ///     Ctrl+V в панели: номер — поиск по номеру в открытой модели и переход к коллизии; ссылка — карточка по Id, и если
    ///     коллизия этой модели — переход как по щелчку, иначе подсказка, какую модель открыть.
    /// </summary>
    public async Task PasteAsync(string? text)
    {
        Status = "";
        if (ClashLink.Parse(text) is not { } link)
        {
            Status = "В буфере нет номера коллизии или ссылки на неё";
            return;
        }

        if (Screen != PaneScreen.Ready || Model is not { } model)
        {
            Status = "Коллизию из буфера можно открыть, когда в панели показан список коллизий открытой модели";
            return;
        }

        var number = link.Number;
        Guid? id = link.CollisionId;
        if (id is { } collisionId)
        {
            CollisionRowDto collision;
            try
            {
                collision = (await _api.CardAsync(collisionId, CancellationToken.None)).Collision;
            }
            catch (ToadFlowAuthRequiredException exception)
            {
                Show(PaneScreen.NotConnected, exception.Message);
                return;
            }
            catch (Exception exception)
            {
                Status = ToadFlowErrors.Describe(exception);
                return;
            }

            if (collision.ModelA != model.Key && collision.ModelB != model.Key)
            {
                Status = $"Коллизия №{collision.Number} в моделях «{collision.ModelNameA}» и «{collision.ModelNameB}» — откройте одну из них";
                return;
            }

            number = collision.Number;
        }

        Search = number!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await ApplyFilters();
        var item = Items.FirstOrDefault(i => id is { } wanted ? i.Row.Collision.Id == wanted : i.Number == number);
        if (item is null)
        {
            Status = $"Коллизии №{number} нет в списке этой модели с выбранными фильтрами";
            return;
        }

        await OpenAsync(item);
    }

    /// <summary>
    ///     Отметка просмотра в фоне: точка у строки гаснет сразу, как открыта карточка. Сервер не отметил (нет сети) — точка
    ///     возвращается: коллизия для него всё ещё «новая для меня».
    /// </summary>
    private async Task MarkSeenAsync(CollisionItemViewModel item)
    {
        var wasUnseen = item.Unseen;
        item.Unseen = false;
        try
        {
            await _api.SeenAsync(item.Row.Collision.Id, CancellationToken.None);
        }
        catch (Exception)
        {
            if (wasUnseen) item.Unseen = true;
        }
    }

    /// <summary>Итог перехода в Revit: результат, причина неудачи или пропуск (пока ждали, выбрали другую строку).</summary>
    private sealed record NavigateOutcome(NavigateResult? Result, string? Error, bool Skipped = false);

    private Task<NavigateOutcome>? _navigation;

    /// <summary>
    ///     Переход в Revit по одному: пока идёт переход (транзакция вида), новые ждут, а дождавшись, идёт только последняя
    ///     выбранная строка. Зажатая ↑/↓ не гоняет Revit по каждой промежуточной строке.
    /// </summary>
    private async Task<NavigateOutcome> NavigateAsync(CollisionItemViewModel item)
    {
        while (_navigation is { IsCompleted: false } running) await running;
        if (SelectedItem?.Row.Collision.Id != item.Row.Collision.Id) return new NavigateOutcome(null, null, Skipped: true);

        var task = ShowInRevitAsync(item.Row);
        _navigation = task;
        return await task;
    }

    private async Task<NavigateOutcome> ShowInRevitAsync(ModelCollisionRow row)
    {
        try
        {
            return new NavigateOutcome(await _revit.ShowAsync(CardViewModel.Request(row)), null);
        }
        catch (Exception exception)
        {
            return new NavigateOutcome(null, exception.Message);
        }
    }

    /// <summary>Счётчик «Мне править» нажат: список — только мои и общие.</summary>
    public bool MineActive => GroupFilter == "mine";

    /// <summary>Счётчик «Другая сторона» нажат.</summary>
    public bool OtherActive => GroupFilter == "other";

    /// <summary>«Мне править N»: включить группу «мои и общие» или снять её (все).</summary>
    [RelayCommand]
    private Task MineFilter() => ToggleGroup("mine");

    /// <summary>«Другая сторона N»: включить группу «взяла другая сторона» или снять её.</summary>
    [RelayCommand]
    private Task OtherFilter() => ToggleGroup("other");

    private Task ToggleGroup(string group)
    {
        _quiet = true;
        try
        {
            GroupFilter = GroupFilter == group ? "all" : group;
        }
        finally
        {
            _quiet = false;
        }

        return ApplyFilters();
    }

    partial void OnStatusFilterChanged(string? value) => FilterChanged();

    partial void OnGroupFilterChanged(string? value)
    {
        OnPropertyChanged(nameof(MineActive));
        OnPropertyChanged(nameof(OtherActive));
        FilterChanged();
    }

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
        await state.SetQueryAsync(_session.QueryFor(match.Key));
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
        catch (Exception)
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
            // Счётчики — кнопки, как в вебе: нажатие включает фильтр. Новый сервер считает «новые для меня» по отметкам
            // просмотра; у старого — только текст «новых с прошлого захода».
            MineCounter = $"Мне править {state.Mine}";
            OtherCounter = $"Другая сторона {state.Other}";
            if (state.NewForMe is { } fresh)
            {
                Counters = "";
                NewForMe = $"Новые для меня {fresh}";
            }
            else
            {
                Counters = $"новых с прошлого захода {state.NewSinceVisit}";
                NewForMe = null;
            }

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
        NewForMeActive = query.Since == "unseen";
        _quiet = quiet;
    }

    /// <summary>Экран панели; не список — карточка закрывается: вернувшись к списку, человек не увидит чужую карточку.</summary>
    private void Show(PaneScreen screen, string message)
    {
        if (screen != PaneScreen.Ready) Card = null;
        (Screen, Message) = (screen, message);
    }

    /// <summary>
    ///     Шаг панели с общей обработкой ошибок; результаты устаревшего шага отбрасываются по номеру версии. Не бросает:
    ///     шаги запускаются и без ожидания (смена документа, фильтры), и панель не должна застрять на «Ищем модель…».
    /// </summary>
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
        catch (Exception exception)
        {
            if (version == _version) Show(PaneScreen.Error, ToadFlowErrors.Describe(exception));
        }
        finally
        {
            if (version == _version) IsBusy = false;
        }
    }
}
