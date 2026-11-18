using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Collisions.Core;
using ToadTools.Connection;

namespace Collisions.ViewModels;

/// <summary>
///     Выделение нескольких строк (Ctrl/Shift+щелчок, Ctrl+A) и массовые действия над ними — те же, что в вебе: «Беру»,
///     «Передать…», «Фиктивные…», «Исправлены», «Примечание…». Сервер принимает действие для одной части, поэтому выделенное
///     уходит запросом на каждую часть; права и переходы проверяет он, по каждой коллизии отдельно.
/// </summary>
public sealed partial class PaneViewModel
{
    /// <summary>Выделенные строки списка; при одной строке — её карточка, при нескольких — полоса массовых действий.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMultiSelect), nameof(SelectionText), nameof(CardVisible))]
    private IReadOnlyList<CollisionItemViewModel> _checkedItems = [];

    /// <summary>Наблюдатель коллизии не меняет; пока неизвестно, кто вошёл, кнопки показаны — отказ всё равно скажет сервер.</summary>
    [ObservableProperty] private bool _canBulkEdit = true;

    public bool IsMultiSelect => CheckedItems.Count > 1;
    public string SelectionText => $"Выделено {CheckedItems.Count}";

    /// <summary>Карточка показана: коллизия открыта и выделена одна строка.</summary>
    public bool CardVisible => Card is not null && !IsMultiSelect;

    /// <summary>
    ///     Выделение списка изменилось. Из нескольких строк осталась одна из них — открывается её карточка: список сам сделал
    ///     её выбранной, пока выделение было множественным, и переход тогда пропущен.
    /// </summary>
    public void SetSelection(IReadOnlyList<CollisionItemViewModel> items)
    {
        var before = CheckedItems;
        CheckedItems = items.ToList();
        if (before.Count > 1 && items.Count == 1 && before.Contains(items[0]) && !ReferenceEquals(Card?.Row, items[0].Row)) _ = OpenAsync(items[0]);
        if (items.Count > 1 && _me is null) _ = LoadMeAsync();
    }

    /// <summary>Переход к строке пропускается: она часть множественного выделения.</summary>
    private bool InMultiSelection(CollisionItemViewModel item) => CheckedItems.Count > 1 && CheckedItems.Contains(item);

    private async Task LoadMeAsync()
    {
        try
        {
            _me ??= await _api.MeAsync(CancellationToken.None);
            CanBulkEdit = !_me.IsViewer;
        }
        catch (Exception)
        {
            // кто вошёл, спросится при открытии карточки; права проверит сервер
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        SelectedItem = null;
        CheckedItems = [];
    }

    [RelayCommand]
    private Task BulkTake() => Confirmed("«Беру»") ? RunBulkAsync("Take", null) : Task.CompletedTask;

    [RelayCommand]
    private Task BulkFixed() => Confirmed($"«{CardViewModel.FixedLabel}»") ? RunBulkAsync("FixedPending", null) : Task.CompletedTask;

    [RelayCommand]
    private Task BulkFake() => _dialogs.AskNote(CardViewModel.FakeTitle) is { } note && !string.IsNullOrWhiteSpace(note)
        ? RunBulkAsync("Fake", note.Trim())
        : Task.CompletedTask;

    [RelayCommand]
    private Task BulkNote() => _dialogs.AskNote(CardViewModel.NoteTitle) is { } note && !string.IsNullOrWhiteSpace(note)
        ? RunBulkAsync("Note", note.Trim())
        : Task.CompletedTask;

    /// <summary>
    ///     «Передать…»: своя сторона, другая или обе. Открытая модель бывает у строк и стороной A, и стороной B, поэтому в окне
    ///     <see cref="CollisionAssignee.SideA" /> значит «своя», <see cref="CollisionAssignee.SideB" /> — «другая», а сторона для
    ///     сервера выбирается по каждой строке. Примечание к смене «кто правит» сервер в массовом действии не пишет — оно уходит
    ///     отдельной записью в ленту.
    /// </summary>
    [RelayCommand]
    private async Task BulkPass()
    {
        var rows = CheckedItems.Select(i => i.Row).ToList();
        var options = new[]
        {
            new SideOption(CollisionAssignee.SideA, Labelled("своя сторона", rows.Select(r => r.Own.Discipline))),
            new SideOption(CollisionAssignee.SideB, Labelled("другая сторона", rows.Select(r => r.Other.Discipline))),
            new SideOption(CollisionAssignee.Both, "обе стороны")
        };
        if (_dialogs.AskPass(options) is not { } choice) return;
        var note = string.IsNullOrWhiteSpace(choice.Note) ? null : choice.Note.Trim();
        await RunBulkAsync("Assignee", null, row => choice.Assignee switch
        {
            CollisionAssignee.SideA => OwnSide(row),
            CollisionAssignee.SideB => OwnSide(row) == "SideA" ? "SideB" : "SideA",
            _ => "Both"
        }, note);
    }

    /// <summary>Сторона открытой модели для сервера; коллизия внутри одной модели — сторона A, как в списке.</summary>
    private static string OwnSide(ModelCollisionRow row) => row.Side == "B" ? "SideB" : "SideA";

    /// <summary>«своя сторона (ОВ)», если раздел у всех строк один, иначе без раздела.</summary>
    private static string Labelled(string side, IEnumerable<string> disciplines)
    {
        var distinct = disciplines.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return distinct.Count == 1 ? $"{side} ({distinct[0]})" : side;
    }

    private bool Confirmed(string action)
    {
        var count = CheckedItems.Count;
        return _dialogs.Confirm($"Изменить {count} {CollisionsWord(count)}: {action}. Продолжить?");
    }

    /// <summary>
    ///     Действие над выделенным: запрос на каждую часть (и сторону), итог — в строке состояния, затем список обновляется.
    ///     Сбой посреди — сказано, сколько уже изменено.
    /// </summary>
    private async Task RunBulkAsync(string action, string? note, Func<ModelCollisionRow, string?>? assignee = null, string? feedNote = null)
    {
        var rows = CheckedItems.Select(i => i.Row).ToList();
        if (rows.Count == 0) return;
        Status = "";
        IsBusy = true;
        var (done, unchanged, refused) = (0, 0, new List<BulkRefusal>());
        try
        {
            foreach (var group in rows.GroupBy(r => (r.PartId, Assignee: assignee?.Invoke(r))))
            {
                var result = await _api.BulkAsync(group.Key.PartId, action, group.Select(r => r.Collision.Id).ToList(), note, group.Key.Assignee,
                    CancellationToken.None);
                (done, unchanged) = (done + result.Done, unchanged + result.Unchanged);
                refused.AddRange(result.Refused);
            }

            if (feedNote is not null)
                foreach (var part in rows.GroupBy(r => r.PartId))
                    await _api.BulkAsync(part.Key, "Note", part.Select(r => r.Collision.Id).ToList(), feedNote, null, CancellationToken.None);

            Status = Summary(done, rows.Count, unchanged, refused);
        }
        catch (ToadFlowAuthRequiredException exception)
        {
            IsBusy = false;
            Show(PaneScreen.NotConnected, exception.Message);
            return;
        }
        catch (Exception exception)
        {
            Status = done > 0 ? $"{ToadFlowErrors.Describe(exception)}. Уже изменено {done}" : ToadFlowErrors.Describe(exception);
        }
        finally
        {
            IsBusy = false;
        }

        if (done > 0 || feedNote is not null) await RefreshCommand.ExecuteAsync(null);
    }

    /// <summary>«Изменено 5 из 7. Уже были такими: 1. Не изменены: №12 — причина; …» — первые три отказа, остальные числом.</summary>
    private static string Summary(int done, int total, int unchanged, IReadOnlyList<BulkRefusal> refused)
    {
        var parts = new List<string> { $"Изменено {done} из {total}" };
        if (unchanged > 0) parts.Add($"Уже были такими: {unchanged}");
        if (refused.Count > 0)
        {
            var shown = string.Join("; ", refused.Take(3).Select(r => $"№{r.Number.ToString(CultureInfo.InvariantCulture)} — {r.Reason}"));
            parts.Add($"Не изменены: {shown}{(refused.Count > 3 ? $" и ещё {refused.Count - 3}" : "")}");
        }

        return string.Join(". ", parts);
    }

    /// <summary>«коллизию», «коллизии», «коллизий» — после числа в винительном падеже.</summary>
    private static string CollisionsWord(int count) => (count % 10, count % 100) switch
    {
        (1, not 11) => "коллизию",
        (>= 2 and <= 4, < 12 or > 14) => "коллизии",
        _ => "коллизий"
    };
}
