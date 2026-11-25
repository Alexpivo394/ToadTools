using System.Collections.ObjectModel;
using System.IO;
using Alexpivo394.Revit.Families.Files;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Families.Core;
using ToadTools.Connection;
using ToadTools.UI.Services;

namespace Families.ViewModels;

/// <summary>Экран панели.</summary>
public enum PaneScreen
{
    NotConnected,
    NotConfigured,
    Ready,
    Error
}

/// <summary>Вариант фильтра: значение для сервера (null — без фильтра) и подпись с числом.</summary>
public sealed record FilterOption(string? Value, string Label);

/// <summary>
///     Панель «Семейства»: поиск по библиотеке ToadFlow, карточка с выбором типоразмеров и загрузка в проект, вкладка «В проекте»,
///     публикация для координатора и отметки «заявка готова». Сеть ждётся без блокировки, продолжения — в UI-потоке панели;
///     Revit — только через <see cref="IFamilyBridge" />.
/// </summary>
public sealed partial class FamiliesPaneViewModel : ObservableObject
{
    private readonly IFamiliesApi _api;
    private readonly IFamilyBridge _revit;
    private readonly ConnectionStore _store;
    private readonly PreviewCache _previews;
    private readonly Func<bool> _connect;
    private readonly IFamilyDialogs _dialogs;
    private readonly Func<string, Task<string>> _hashFile;
    private int _version;
    private int _page = 1;
    private int _searchVersion;
    private bool _quiet;

    [ObservableProperty] private PaneScreen _screen = PaneScreen.NotConnected;
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private IReadOnlyList<FilterOption> _sectionOptions = [new(null, "Все разделы")];
    [ObservableProperty] private IReadOnlyList<FilterOption> _categoryOptions = [new(null, "Все категории")];
    [ObservableProperty] private string? _section;
    [ObservableProperty] private string? _category;
    [ObservableProperty] private bool _showUnreviewed;
    [ObservableProperty] private string _found = "";
    [ObservableProperty] private bool _hasMore;
    [ObservableProperty] private FamilyItemViewModel? _selectedItem;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CardVisible))] private FamilyCardViewModel? _card;

    /// <summary>Строка «заявки: есть новое» над поиском; пусто — новостей нет.</summary>
    [ObservableProperty] private string _requestNews = "";

    /// <summary>Открыт редактор семейства, и вошедший может публиковать.</summary>
    [ObservableProperty] private bool _canPublish;

    /// <summary>Открыт проект: можно загружать и смотреть «В проекте».</summary>
    [ObservableProperty] private bool _isProject;

    [ObservableProperty] private int _selectedTab;

    public FamiliesPaneViewModel(IFamiliesApi api, IFamilyBridge revit, ConnectionStore store, PreviewCache previews, Func<bool> connect, IFamilyDialogs dialogs,
        Func<string, Task<string>>? hashFile = null)
    {
        (_api, _revit, _store, _previews, _connect, _dialogs) = (api, revit, store, previews, connect, dialogs);
        _hashFile = hashFile ?? (path => Task.Run(() => FamilyHash.OfFile(path)));
        Publish = new PublishViewModel(api, revit, dialogs, _hashFile);
    }

    public ObservableCollection<FamilyItemViewModel> Items { get; } = [];

    public ObservableCollection<RequestItem> RequestUpdates { get; } = [];

    public PublishViewModel Publish { get; }

    public bool CardVisible => Card is not null;

    /// <summary>Библиотека из последнего открытия панели.</summary>
    public LibraryInfo? Library { get; private set; }

    public UserInfo? Me { get; private set; }

    /// <summary>Документ активного окна из последнего открытия панели.</summary>
    public ActiveDocument? Document { get; private set; }

    /// <summary>Последний поиск (для тестов: поиск при вводе идёт в фоне).</summary>
    public Task SearchTask { get; private set; } = Task.CompletedTask;

    /// <summary>Открытие панели, смена документа или подключения: кто вошёл, библиотека, документ, поиск, новости заявок.</summary>
    public Task ActivateAsync() => RunAsync(async version =>
    {
        if (_store.Load() is null)
        {
            Show(PaneScreen.NotConnected, "Войдите в ToadFlow, чтобы искать семейства библиотеки");
            return;
        }

        Me = await _api.MeAsync(CancellationToken.None);
        Library = await _api.LibraryAsync(CancellationToken.None);
        if (version != _version) return;
        ToadLog.Debug("Семейства", $"библиотека загружена, настроена: {Library.Configured}");
        if (!Library.Configured)
        {
            Show(PaneScreen.NotConfigured, "Библиотека семейств в ToadFlow ещё не настроена: администратор задаёт её папку в вебе, «Семейства» → «Параметры»");
            return;
        }

        Document = await _revit.ActiveDocumentAsync();
        IsProject = Document is { IsFamily: false };
        CanPublish = Document is { IsFamily: true } && Library.CanPublish;
        if (CanPublish) await Publish.PrepareAsync(Library, Document!);
        else if (SelectedTab == 2) SelectedTab = 0;
        Show(PaneScreen.Ready, "");

        await LoadFacetsAsync();
        await SearchCoreAsync(version, reset: true);
        await RefreshRequestsAsync();
        if (SelectedTab == 1 && IsProject) await RefreshProjectCoreAsync(version);
    });

    [RelayCommand]
    private Task Refresh() => ActivateAsync();

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

    /// <summary>Поиск при вводе: через 300 мс после остановки.</summary>
    partial void OnSearchTextChanged(string value) => ScheduleSearch();

    partial void OnSectionChanged(string? value) => ScheduleSearch(0);

    partial void OnCategoryChanged(string? value) => ScheduleSearch(0);

    partial void OnShowUnreviewedChanged(bool value)
    {
        if (_quiet || Screen != PaneScreen.Ready) return;
        SearchTask = RunAsync(async version =>
        {
            await LoadFacetsAsync();
            await SearchCoreAsync(version, reset: true);
        });
    }

    private void ScheduleSearch(int delayMs = 300)
    {
        if (_quiet || Screen != PaneScreen.Ready) return;
        var mine = ++_searchVersion;
        SearchTask = Task.Run(async () => await Task.Delay(delayMs).ConfigureAwait(false)).ContinueWith(_ => mine == _searchVersion
            ? RunAsync(version => SearchCoreAsync(version, reset: true))
            : Task.CompletedTask, TaskSchedulers.FromCurrentSynchronizationContextOrDefault()).Unwrap();
    }

    /// <summary>Поиск сейчас (Enter в строке поиска).</summary>
    [RelayCommand]
    private Task Search()
    {
        ++_searchVersion;
        return SearchTask = RunAsync(version => SearchCoreAsync(version, reset: true));
    }

    [RelayCommand]
    private Task More() => HasMore ? RunAsync(version => SearchCoreAsync(version, reset: false)) : Task.CompletedTask;

    private async Task SearchCoreAsync(int version, bool reset)
    {
        if (reset) _page = 1;
        else _page++;
        var page = await _api.SearchAsync(new FamilySearch(SearchText, Section, Category, ShowUnreviewed), _page, CancellationToken.None);
        if (version != _version) return;
        ToadLog.Debug("Семейства", $"страница {_page} поиска: {page.Items.Count} из {page.Total}");

        if (reset)
        {
            Items.Clear();
            _quiet = true;
            SelectedItem = null;
            _quiet = false;
        }

        foreach (var hit in page.Items)
        {
            var item = new FamilyItemViewModel(hit, _revit.RevitYear);
            Items.Add(item);
            _ = item.LoadPreviewAsync(_previews);
        }

        HasMore = Items.Count < page.Total;
        Found = page.Total == 0
            ? string.IsNullOrWhiteSpace(SearchText) && Section is null && Category is null ? "В библиотеке пока нет семейств" : "Ничего не нашлось — попробуйте часть слова"
            : $"Найдено: {page.Total}";
    }

    private async Task LoadFacetsAsync()
    {
        var facets = await _api.FacetsAsync(ShowUnreviewed, CancellationToken.None);
        _quiet = true;
        try
        {
            SectionOptions = [new FilterOption(null, "Все разделы"), .. facets.Sections.Select(f => new FilterOption(f.Value, $"{f.Value} ({f.Count})"))];
            CategoryOptions = [new FilterOption(null, "Все категории"), .. facets.Categories.Select(f => new FilterOption(f.Value, $"{f.Value} ({f.Count})"))];
            if (Section is not null && SectionOptions.All(o => o.Value != Section)) Section = null;
            if (Category is not null && CategoryOptions.All(o => o.Value != Category)) Category = null;
        }
        finally
        {
            _quiet = false;
        }
    }

    partial void OnSelectedItemChanged(FamilyItemViewModel? value)
    {
        if (_quiet || value is null) return;
        _ = OpenCardAsync(value.Hit.Id);
    }

    /// <summary>Карточка семейства. Не бросает: вызывается и без ожидания — при выборе строки.</summary>
    public Task OpenCardAsync(Guid id) => RunAsync(async version =>
    {
        var dto = await _api.CardAsync(id, unreviewed: true, CancellationToken.None);
        if (version != _version) return;
        var card = new FamilyCardViewModel(dto, _revit.RevitYear, Library?.CanPublish == true);
        Card = card;
        // Другой вариант — своя картинка: у итерации своя действующая версия.
        card.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FamilyCardViewModel.SelectedVariant)) _ = LoadVariantPreviewAsync(card);
        };
        if (dto.Current is { } current && dto.Versions.Any(v => v.IsCurrent))
            card.Preview = Items.FirstOrDefault(i => i.Hit.Id == id)?.Preview ?? await _previews.GetAsync(id, current.Hash);
    });

    private async Task LoadVariantPreviewAsync(FamilyCardViewModel card)
    {
        var variant = card.SelectedVariant;
        try
        {
            card.Preview = variant.Hash is { } hash ? await _previews.GetAsync(card.Card.Id, hash) : null;
        }
        catch (Exception exception) when (exception is ToadFlowUnavailableException or ToadFlowRequestException or IOException)
        {
            card.Preview = null;
        }
    }

    [RelayCommand]
    private void CloseCard()
    {
        Card = null;
        _quiet = true;
        SelectedItem = null;
        _quiet = false;
    }

    /// <summary>
    ///     «Загрузить»: файл из папки библиотеки сверяется с карточкой (не совпал — его только что меняли: предупреждение и выбор),
    ///     выбранные типоразмеры грузятся в проект, на семейство ставится метка, загрузка сообщается серверу.
    /// </summary>
    [RelayCommand]
    private Task Load() => Card is not { } card ? Task.CompletedTask : RunAsync(async _ =>
    {
        if (!IsProject)
        {
            Status = "Откройте модель проекта — загрузить семейство можно только в проект";
            return;
        }

        if (card.BlockedReason is { } reason)
        {
            Status = reason;
            return;
        }

        if (card.NothingChosen)
        {
            Status = "Отметьте хотя бы один типоразмер";
            return;
        }

        var variant = card.SelectedVariant;
        if (variant.IsFinal && !_dialogs.Confirm($"Итерация «{card.Name} · {variant.Label}» завершена: {variant.FinalComment}. Её не меняют и в новые проекты обычно не грузят. Всё равно загрузить?"))
        {
            Status = "Загрузка отменена";
            return;
        }

        var result = await LoadFamilyAsync(card.Card.Id, card.Name, card.FullPath, variant.Hash, card.ChosenTypes, variant.IterationId);
        if (result is not null)
            Status = $"«{card.Name}» {(result.Replaced ? "обновлено" : "загружено")}: {Texts.Types(result.Types.Count)}";
    });

    /// <returns>Итог загрузки; null — не загружено (человек отказался или файла нет), причина — в <see cref="Status" />.</returns>
    private async Task<LoadOutcome?> LoadFamilyAsync(Guid cardId, string name, string path, string? expectedHash, IReadOnlyCollection<string>? types, Guid? iterationId)
    {
        string hash;
        try
        {
            hash = await _hashFile(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status = $"Файл «{path}» не открывается: {exception.Message}. Проверьте доступ к папке библиотеки";
            return null;
        }

        if (expectedHash is not null && !hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)
            && !_dialogs.Confirm($"Файл «{name}» в библиотеке изменился после того, как его увидел ToadFlow: его только что правили. Загрузить то, что лежит в папке сейчас?"))
        {
            Status = "Загрузка отменена";
            return null;
        }

        var outcome = await _revit.LoadAsync(new LoadRequest(path, types, cardId, hash, iterationId));
        try
        {
            await _api.ReportLoadAsync(cardId, hash, Document?.Title, iterationId, CancellationToken.None);
        }
        catch (Exception exception) when (exception is ToadFlowUnavailableException or ToadFlowRequestException)
        {
            // учёт загрузок — не повод считать загрузку неудачной
        }

        return outcome;
    }

    [RelayCommand]
    private void ShowInFolder()
    {
        if (Card is { FullPath.Length: > 0 } card) _dialogs.ShowInExplorer(card.FullPath);
    }

    [RelayCommand]
    private void OpenInWeb()
    {
        if (Card is { } card) _dialogs.OpenUrl(_api.WebUrl($"/families/catalog?card={card.Card.Id}"));
    }

    /// <summary>
    ///     «Нет нужного» — форма заявки в браузере с контекстом: текст поиска, проект (имя открытой модели), раздел фильтра и версия Revit —
    ///     человеку остаётся описать и вставить скриншот.
    /// </summary>
    [RelayCommand]
    private void RequestFamily()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(SearchText)) parts.Add("q=" + Uri.EscapeDataString(SearchText.Trim()));
        if (Document is { IsFamily: false, Title: { Length: > 0 } title }) parts.Add("project=" + Uri.EscapeDataString(System.IO.Path.GetFileNameWithoutExtension(title)));
        if (!string.IsNullOrWhiteSpace(Section)) parts.Add("section=" + Uri.EscapeDataString(Section!));
        parts.Add("revit=" + _revit.RevitYear);
        _dialogs.OpenUrl(_api.WebUrl("/families/requests/new?" + string.Join("&", parts)));
    }

    /// <summary>«Сообщить о проблеме» в открытом семействе — форма заявки «исправить» в браузере.</summary>
    [RelayCommand]
    private void ReportProblem()
    {
        if (Card is { } card) _dialogs.OpenUrl(_api.WebUrl($"/families/requests/new?kind=fix&card={card.Card.Id}"));
    }

    /// <summary>«Подходит» — принять заявку на приёмке прямо из панели.</summary>
    [RelayCommand]
    private Task AcceptRequest(RequestItem? request) => request is null ? Task.CompletedTask : RunAsync(async _ =>
    {
        await _api.AcceptRequestAsync(request.Id, CancellationToken.None);
        RemoveUpdate(request);
        Status = $"Заявка № {request.Number} принята — спасибо!";
    });

    /// <summary>«Не то…» — объяснить, что не так, удобнее в браузере: страница заявки с перепиской и вложениями.</summary>
    [RelayCommand]
    private void DeclineRequest(RequestItem? request)
    {
        if (request is null) return;
        _dialogs.OpenUrl(_api.WebUrl($"/families/requests/{request.Id}"));
    }

    private void RemoveUpdate(RequestItem request)
    {
        RequestUpdates.Remove(request);
        if (RequestUpdates.Count == 0) RequestNews = "";
    }

    private async Task RefreshRequestsAsync()
    {
        List<RequestItem> updates;
        try
        {
            updates = await _api.RequestUpdatesAsync(CancellationToken.None);
        }
        catch (ToadFlowRequestException)
        {
            return; // сервер без заявок — панель работает и так
        }

        RequestUpdates.Clear();
        foreach (var update in updates) RequestUpdates.Add(update);
        var review = updates.Count(u => u.State == "Review");
        var ready = updates.Count(u => u.State is "Done" or "Exists");
        var other = updates.Count - review - ready;
        RequestNews = updates.Count == 0 ? ""
            : review > 0 ? $"Заявки: на приёмке {review} — подходит?" + (ready + other > 0 ? $", ещё изменений {ready + other}" : "")
            : ready > 0 ? $"Заявки: готово {ready}" + (other > 0 ? $", ещё изменений {other}" : "")
            : $"Заявки: изменений {updates.Count}";
    }

    /// <summary>Заявка из «есть новое»: готовая — карточка семейства в панели, остальные — страница заявки в вебе. Отмечается «видел».</summary>
    [RelayCommand]
    private Task OpenRequest(RequestItem? request) => request is null ? Task.CompletedTask : RunAsync(async _ =>
    {
        await _api.MarkRequestSeenAsync(request.Id, CancellationToken.None);
        RequestUpdates.Remove(request);
        if (RequestUpdates.Count == 0) RequestNews = "";
        if (request.CardId is { } cardId && request.State is "Done" or "Exists" or "Review")
        {
            SelectedTab = 0;
            await OpenCardAsync(cardId);
        }
        else
            _dialogs.OpenUrl(_api.WebUrl($"/families/requests/{request.Id}"));
    });

    /// <summary>Шаг панели: ошибки сети и сервера — в панели, а не исключением в Revit; устаревший шаг (пришёл новый) не рисует.</summary>
    private async Task RunAsync(Func<int, Task> step)
    {
        var version = ++_version;
        IsBusy = true;
        try
        {
            await step(version);
        }
        catch (ToadFlowAuthRequiredException)
        {
            ToadLog.Debug("Семейства", "подключение устарело: нужен вход в ToadFlow");
            Show(PaneScreen.NotConnected, "Подключение к ToadFlow устарело — войдите заново");
        }
        catch (ToadFlowUnavailableException exception)
        {
            ToadLog.Debug("Семейства", $"нет связи с ToadFlow: {exception.Message}");
            Show(PaneScreen.Error, $"{exception.Message}. Сами файлы библиотеки по-прежнему можно взять проводником");
        }
        catch (Exception exception) when (exception is ToadFlowRequestException or InvalidOperationException or ArgumentException)
        {
            Status = exception.Message;
        }
        catch (Exception exception)
        {
            ReportUnexpected(exception);
        }
        finally
        {
            if (version == _version) IsBusy = false;
        }
    }

    public void ReportUnexpected(Exception exception)
    {
        ToadLog.Error("Семейства", exception);
        Status = $"Не получилось: {exception.Message}";
    }

    private void Show(PaneScreen screen, string message)
    {
        Screen = screen;
        Message = message;
        if (screen != PaneScreen.Ready)
        {
            Items.Clear();
            Card = null;
        }
    }
}

internal static class TaskSchedulers
{
    public static TaskScheduler FromCurrentSynchronizationContextOrDefault() =>
        SynchronizationContext.Current is null ? TaskScheduler.Default : TaskScheduler.FromCurrentSynchronizationContext();
}
