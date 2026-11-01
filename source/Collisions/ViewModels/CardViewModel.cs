using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Collisions.Core;
using ToadTools.Connection;

namespace Collisions.ViewModels;

/// <summary>Выбор в окне «Передать…»: кто правит и примечание.</summary>
public sealed record PassChoice(CollisionAssignee Assignee, string Note);

/// <summary>Окна, которые карточка открывает: примечание, передача, ссылка в браузере. В тестах — подставные.</summary>
public interface ICardDialogs
{
    /// <returns>Текст примечания; null — человек передумал.</returns>
    string? AskNote(string title);

    /// <returns>null — человек передумал.</returns>
    PassChoice? AskPass(CollisionAssignee current);

    void OpenUrl(string url);
}

/// <summary>
///     Карточка выбранной коллизии: картинка, параметры обоих элементов, лента и действия — те же маршруты и те же правила
///     показа кнопок, что в веб-карточке. Права всё равно проверяет сервер: его отказ показывается текстом.
/// </summary>
public sealed partial class CardViewModel : ObservableObject
{
    public const string FakeTitle = "Почему фиктивная";
    public const string NoteTitle = "Примечание";

    private readonly IClashApi _api;
    private readonly ModelCollisionRow _row;
    private readonly UserInfo _me;
    private readonly ICardDialogs _dialogs;
    private readonly ThumbnailCache _images;

    [ObservableProperty] private CollisionRowDto _collision;
    [ObservableProperty] private byte[]? _image;
    [ObservableProperty] private IReadOnlyList<KeyValuePair<string, string>> _ownParameters = [];
    [ObservableProperty] private IReadOnlyList<KeyValuePair<string, string>> _otherParameters = [];
    [ObservableProperty] private IReadOnlyList<string> _feed = [];
    [ObservableProperty] private string? _navigateMessage;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _isBusy;

    public CardViewModel(IClashApi api, ModelCollisionRow row, UserInfo me, ICardDialogs dialogs, ThumbnailCache images)
    {
        (_api, _row, _me, _dialogs, _images) = (api, row, me, dialogs, images);
        _collision = row.Collision;
    }

    /// <summary>Действие прошло: список надо обновить.</summary>
    public event EventHandler? Changed;

    public ModelCollisionRow Row => _row;
    public int Number => Collision.Number;
    public string Status => CollisionItemViewModel.StatusLabel(Collision.Status);
    public string Depth => $"{Collision.DepthMm:0} мм";
    public string Own => _row.Own.Title;
    public string Other => $"{_row.Other.ModelName}: {_row.Other.Title}";
    public string Assignee => Collision.Assignee switch
    {
        CollisionAssignee.SideA => "сторона A",
        CollisionAssignee.SideB => "сторона B",
        _ => "обе стороны"
    };

    private bool ReadOnly => _me.IsViewer;
    private bool Mine => _me.Disciplines.Contains(Collision.DisciplineA) || _me.Disciplines.Contains(Collision.DisciplineB);
    private bool CanChange => !ReadOnly && (_me.IsStaff || Mine);
    private bool IsOpen => Collision.Status is CollisionStatus.New or CollisionStatus.Active or CollisionStatus.Returned;

    public bool CanTake => !ReadOnly && Mine;
    public bool CanPass => CanChange;
    public bool CanFixed => CanChange && IsOpen;
    public bool CanFake => CanChange && Collision.Status != CollisionStatus.Fake;
    public bool CanUnfake => CanChange && Collision.Status == CollisionStatus.Fake;
    public bool CanNote => !ReadOnly;

    /// <summary>Почему кнопок статуса нет — как в вебе; у наблюдателя подсказки нет.</summary>
    public string? Hint => !ReadOnly && !CanChange ? "Статус и «кто правит» меняет проектировщик раздела этой коллизии или координатор" : null;

    partial void OnCollisionChanged(CollisionRowDto value)
    {
        foreach (var name in new[]
                 {
                     nameof(Number), nameof(Status), nameof(Depth), nameof(Assignee), nameof(CanTake), nameof(CanPass), nameof(CanFixed),
                     nameof(CanFake), nameof(CanUnfake), nameof(CanNote), nameof(Hint)
                 })
            OnPropertyChanged(name);
    }

    /// <summary>Карточка, параметры элементов и картинка. Нет выгрузки элемента — его таблица просто пустая.</summary>
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var image = LoadImageAsync();
            var own = ParametersAsync(_row.Own);
            var other = ParametersAsync(_row.Other);
            await ReloadAsync();
            OwnParameters = await own;
            OtherParameters = await other;
            Image = await image;
        }
        catch (Exception exception) when (exception is ToadFlowRequestException or ToadFlowUnavailableException or ToadFlowAuthRequiredException)
        {
            Error = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Итог перехода в Revit — сообщения про свой и второй элемент.</summary>
    public void ShowNavigation(NavigateResult result)
    {
        var lines = new List<string>();
        if (!result.OwnFound) lines.Add("Элемент не найден — возможно, уже удалён или заменён. Исправление подтвердит следующий прогон");
        var link = result.OtherLinkName ?? _row.Other.ModelName;
        switch (result.Other)
        {
            case OtherState.InLinkNotSelectable:
                lines.Add($"Второй элемент — в связи {link}: в этой версии Revit выделить его нельзя, данные — ниже");
                break;
            case OtherState.NoLink:
                lines.Add($"Модель {_row.Other.ModelName} не подгружена связью");
                break;
            case OtherState.NotFoundInLink:
                lines.Add($"Второй элемент не найден в связи {link}");
                break;
        }

        NavigateMessage = lines.Count == 0 ? null : string.Join("\n", lines);
    }

    [RelayCommand]
    private Task Take() => ActAsync(() => _api.TakeAsync(Collision.Id, CancellationToken.None));

    [RelayCommand]
    private Task Pass()
    {
        if (_dialogs.AskPass(Collision.Assignee) is not { } choice || string.IsNullOrWhiteSpace(choice.Note)) return Task.CompletedTask;
        return ActAsync(() => _api.PassAsync(Collision.Id, choice.Assignee.ToString(), choice.Note.Trim(), CancellationToken.None));
    }

    [RelayCommand]
    private Task Fake()
    {
        if (_dialogs.AskNote(FakeTitle) is not { } note || string.IsNullOrWhiteSpace(note)) return Task.CompletedTask;
        return ActAsync(() => _api.SetStatusAsync(Collision.Id, nameof(CollisionStatus.Fake), note.Trim(), CancellationToken.None));
    }

    [RelayCommand]
    private Task Fixed() => ActAsync(() => _api.SetStatusAsync(Collision.Id, nameof(CollisionStatus.FixedPending), null, CancellationToken.None));

    [RelayCommand]
    private Task Unfake() => ActAsync(() => _api.SetStatusAsync(Collision.Id, nameof(CollisionStatus.Active), null, CancellationToken.None));

    [RelayCommand]
    private Task Note()
    {
        if (_dialogs.AskNote(NoteTitle) is not { } note || string.IsNullOrWhiteSpace(note)) return Task.CompletedTask;
        return ActAsync(() => _api.NoteAsync(Collision.Id, note.Trim(), CancellationToken.None));
    }

    [RelayCommand]
    private void OpenIn3D() => _dialogs.OpenUrl(_api.ViewerUrl(_row.PartId, Collision.Id));

    private async Task ActAsync(Func<Task> action)
    {
        Error = null;
        IsBusy = true;
        try
        {
            await action();
            await ReloadAsync();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is ToadFlowRequestException or ToadFlowUnavailableException or ToadFlowAuthRequiredException)
        {
            Error = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadAsync()
    {
        var card = await _api.CardAsync(Collision.Id, CancellationToken.None);
        Collision = card.Collision;
        Feed = card.Feed.Select(FeedLine).ToList();
    }

    /// <summary>Строка ленты как в веб-карточке: смена статуса «было → стало», затем текст; без автора — «система».</summary>
    private static string FeedLine(FeedEntryDto entry)
    {
        var status = entry is { Kind: "Status", FromStatus: { } from, ToStatus: { } to }
            ? $"{CollisionItemViewModel.StatusLabel(from)} → {CollisionItemViewModel.StatusLabel(to)}"
            : null;
        var text = string.IsNullOrWhiteSpace(entry.Text) ? null : entry.Text.Trim();
        var body = status is null ? text ?? "" : text is null ? status : $"{status} — {text}";
        return $"{entry.At.ToLocalTime():dd.MM.yyyy HH:mm} · {entry.UserName ?? "система"}: {body}";
    }

    private async Task<IReadOnlyList<KeyValuePair<string, string>>> ParametersAsync(CollisionSide side)
    {
        try
        {
            return await _api.ElementAsync(Collision.Id, side.ModelKey, side.UniqueId, CancellationToken.None);
        }
        catch (ToadFlowRequestException)
        {
            return [];
        }
    }

    private async Task<byte[]?> LoadImageAsync()
    {
        try
        {
            return await _images.GetAsync(Collision.Id, Collision.ImageVersion, CancellationToken.None);
        }
        catch (Exception)
        {
            return null; // картинки нет — карточка без неё
        }
    }
}

/// <summary>Окно примечания: «Сохранить» недоступна, пока поле пустое. Для «Передать…» ещё выбор стороны.</summary>
public sealed partial class NoteViewModel(string title, CollisionAssignee? current = null) : ObservableObject
{
    public string Title => title;

    /// <summary>Окно передачи: показывается выбор стороны.</summary>
    public bool ChoosesSide => current is not null;

    public IReadOnlyList<KeyValuePair<CollisionAssignee, string>> Sides { get; } =
        new[]
            {
                new KeyValuePair<CollisionAssignee, string>(CollisionAssignee.Both, "обе стороны"),
                new KeyValuePair<CollisionAssignee, string>(CollisionAssignee.SideA, "сторона A"),
                new KeyValuePair<CollisionAssignee, string>(CollisionAssignee.SideB, "сторона B")
            }
            .Where(s => s.Key != current).ToList();

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SaveCommand))] private string _text = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SaveCommand))] private CollisionAssignee? _side;

    /// <summary>Окно закрывается с результатом.</summary>
    public event EventHandler? Saved;

    private bool CanSave() => !string.IsNullOrWhiteSpace(Text) && (!ChoosesSide || Side is not null);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => Saved?.Invoke(this, EventArgs.Empty);
}
