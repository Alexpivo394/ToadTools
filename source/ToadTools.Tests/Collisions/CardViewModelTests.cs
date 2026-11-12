using Collisions.Core;
using Collisions.ViewModels;
using Shouldly;
using ToadTools.Connection;

namespace ToadTools.Tests.Collisions;

/// <summary>Подставные окна карточки: что «ввёл» человек и какие ссылки открылись.</summary>
public sealed class FakeDialogs : ICardDialogs
{
    public string? Note { get; set; } = "примечание";
    public PassChoice? Pass { get; set; } = new(CollisionAssignee.SideA, "отдаю КР");
    public List<string> Opened { get; } = [];
    public List<string> Asked { get; } = [];

    public string? AskNote(string title)
    {
        Asked.Add(title);
        return Note;
    }

    public IReadOnlyList<SideOption>? Offered { get; private set; }

    public PassChoice? AskPass(IReadOnlyList<SideOption> options)
    {
        Offered = options;
        return Pass;
    }

    public void OpenUrl(string url) => Opened.Add(url);
}

public class CardViewModelTests
{
    private static UserInfo User(string role, params string[] disciplines) => new(Guid.NewGuid(), "u", "Пользователь", role, disciplines);

    private static (CardViewModel Card, FakeClashApi Api, FakeDialogs Dialogs, ModelCollisionRow Row, TempFolder Temp) Create(UserInfo me,
        CollisionStatus status = CollisionStatus.Active)
    {
        var api = new FakeClashApi();
        var row = FakeClashApi.Row(7);
        row = row with { Collision = row.Collision with { Status = status } };
        api.Rows = [row];
        var dialogs = new FakeDialogs();
        var temp = new TempFolder();
        var card = new CardViewModel(api, row, me, dialogs, new ThumbnailCache(temp.File("images"), api.ImageAsync), new FakeRevit());
        return (card, api, dialogs, row, temp);
    }

    [Test]
    public async Task Card_SelectOther_OnlyThatElement_ReasonShown()
    {
        var api = new FakeClashApi();
        var row = FakeClashApi.Row(7);
        api.Rows = [row];
        using var temp = new TempFolder();
        var revit = new FakeRevit { SelectResult = SelectState.NoLink };
        var card = new CardViewModel(api, row, User(Roles.Designer, "ОВ"), new FakeDialogs(), new ThumbnailCache(temp.File("images"), api.ImageAsync), revit);

        await card.SelectOtherCommand.ExecuteAsync(null);

        revit.Selected.ShouldHaveSingleItem().Element.ShouldBe(CollisionElement.Other);
        revit.Selected[0].Request.OtherUniqueId.ShouldBe(row.Other.UniqueId);
        card.NavigateMessage.ShouldBe($"Модель {row.Other.ModelName} не подгружена связью");

        revit.SelectResult = SelectState.Selected;
        await card.SelectOwnCommand.ExecuteAsync(null);

        revit.Selected[1].Element.ShouldBe(CollisionElement.Own);
        card.NavigateMessage.ShouldBeNull();
    }

    [Test]
    public async Task Card_Designer_OwnDiscipline_ActionsVisible()
    {
        var (card, _, _, _, temp) = Create(User(Roles.Designer, "ОВ"));
        using var _ = temp;
        await card.LoadAsync();

        (card.CanTake, card.CanPass, card.CanFixed, card.CanFake, card.CanUnfake, card.CanNote).ShouldBe((true, true, true, true, false, true));
        card.Hint.ShouldBeNull();
    }

    [Test]
    public async Task Card_Designer_OtherDisciplines_ReadOnly()
    {
        var (card, _, _, _, temp) = Create(User(Roles.Designer, "АР"));
        using var _ = temp;
        await card.LoadAsync();

        (card.CanTake, card.CanPass, card.CanFixed, card.CanFake, card.CanUnfake).ShouldBe((false, false, false, false, false));
        card.CanNote.ShouldBeTrue(); // примечание пишет в любую, как в вебе
        card.Hint.ShouldBe("Статус и «кто правит» меняет проектировщик раздела этой коллизии или координатор");
    }

    [Test]
    public async Task Card_Viewer_ReadOnly()
    {
        var (card, _, _, _, temp) = Create(User(Roles.Viewer, "ОВ"));
        using var _ = temp;
        await card.LoadAsync();

        (card.CanTake, card.CanPass, card.CanFixed, card.CanFake, card.CanUnfake, card.CanNote).ShouldBe((false, false, false, false, false, false));
        card.Hint.ShouldBeNull();
    }

    [Test]
    public async Task Card_Coordinator_AllActions()
    {
        var (card, _, _, _, temp) = Create(User(Roles.Operator), CollisionStatus.Fake);
        using var _ = temp;
        await card.LoadAsync();

        (card.CanTake, card.CanPass, card.CanFixed, card.CanFake, card.CanUnfake, card.CanNote).ShouldBe((false, true, false, false, true, true));
    }

    [Test]
    public async Task Card_Fake_RequiresNote()
    {
        var (card, api, dialogs, _, temp) = Create(User(Roles.Designer, "ОВ"));
        using var _ = temp;
        await card.LoadAsync();
        dialogs.Note = "   ";

        await card.FakeCommand.ExecuteAsync(null);

        api.Actions.ShouldBeEmpty();
        dialogs.Asked.ShouldBe(["Почему фиктивная"]);
        new NoteViewModel("Почему фиктивная").SaveCommand.CanExecute(null).ShouldBeFalse();
        new NoteViewModel("Почему фиктивная") { Text = "нет пересечения" }.SaveCommand.CanExecute(null).ShouldBeTrue();
    }

    [Test]
    public async Task Card_Fake_PostsStatusFakeWithNote_ThenReloadsRow()
    {
        var (card, api, dialogs, row, temp) = Create(User(Roles.Designer, "ОВ"));
        using var _ = temp;
        await card.LoadAsync();
        var changed = 0;
        card.Changed += (_, _) => changed++;
        dialogs.Note = " воздух ";
        api.Card = id => new CollisionCardDto(row.Collision with { Status = CollisionStatus.Fake }, []);

        await card.FakeCommand.ExecuteAsync(null);

        api.Actions.ShouldBe([$"status {row.Collision.Id} Fake воздух"]);
        (api.CardRequests, changed, card.Status).ShouldBe((2, 1, "фиктивная"));
        card.CanUnfake.ShouldBeTrue();
    }

    [Test]
    public async Task Card_Fixed_PostsFixedPending()
    {
        var (card, api, _, row, temp) = Create(User(Roles.Designer, "ОВ"));
        using var _ = temp;
        await card.LoadAsync();

        await card.FixedCommand.ExecuteAsync(null);

        api.Actions.ShouldBe([$"status {row.Collision.Id} FixedPending "]);
    }

    [Test]
    public async Task Card_Take_PostsTake()
    {
        var (card, api, _, row, temp) = Create(User(Roles.Designer, "ОВ"));
        using var _ = temp;
        await card.LoadAsync();

        await card.TakeCommand.ExecuteAsync(null);

        api.Actions.ShouldBe([$"take {row.Collision.Id}"]);
    }

    [Test]
    public async Task Card_Pass_PostsAssigneeWithNote()
    {
        var (card, api, _, row, temp) = Create(User(Roles.Designer, "ОВ"));
        using var _ = temp;
        await card.LoadAsync();

        await card.PassCommand.ExecuteAsync(null);

        api.Actions.ShouldBe([$"assignee {row.Collision.Id} SideA отдаю КР"]);
    }

    [Test]
    public async Task Card_403_ShowsServerText()
    {
        var (card, api, _, _, temp) = Create(User(Roles.Designer, "ОВ"));
        using var _ = temp;
        await card.LoadAsync();
        api.Fail = new ToadFlowRequestException(403, "Менять статус может только проектировщик раздела");

        await card.TakeCommand.ExecuteAsync(null);

        card.Error.ShouldBe("Менять статус может только проектировщик раздела");
    }

    [Test]
    public void Card_OpenIn3D_Url()
    {
        var (card, _, dialogs, row, temp) = Create(User(Roles.Viewer));
        using var _ = temp;

        card.OpenIn3DCommand.Execute(null);

        dialogs.Opened.ShouldBe([$"http://toadflow:8080/clash/checks/{row.PartId}?collision={row.Collision.Id}"]);
    }

    [Test]
    public async Task Card_Load_ParametersAndFeed()
    {
        var (card, api, _, row, temp) = Create(User(Roles.Viewer));
        using var _ = temp;
        api.Card = _ => new CollisionCardDto(row.Collision,
            [new FeedEntryDto(new DateTimeOffset(2026, 10, 30, 10, 0, 0, TimeSpan.Zero), "Note", "Петров", "проверю", null, null, null, null, null)]);

        await card.LoadAsync();

        card.Feed.ShouldHaveSingleItem().ShouldEndWith("Петров: проверю");
        card.Image.ShouldBe(new byte[] { 1 });
    }

    /// <summary>Под картинкой — только семейство, тип, модель и ID обоих элементов, из строки списка: выгрузку элемента не запрашиваем.</summary>
    [Test]
    public async Task Card_Parameters_FamilyTypeModelIdOnly()
    {
        var (card, _, _, _, temp) = Create(User(Roles.Viewer));
        using var _ = temp;

        await card.LoadAsync();

        card.OwnParameters.ShouldBe([Pair("Семейство", "Воздуховод"), Pair("Тип", "500x300"), Pair("Модель", "DON_OV.rvt"), Pair("ID", "100007")]);
        card.OtherParameters.ShouldBe([Pair("Семейство", "Стена"), Pair("Тип", "200"), Pair("Модель", "DON_KR.rvt"), Pair("ID", "7")]);
    }

    private static KeyValuePair<string, string> Pair(string key, string value) => new(key, value);

    /// <summary>«Кто правит» и выбор в «Передать…» — разделами пары (КР × ОВ), а не «сторона A / B».</summary>
    [Test]
    public async Task Card_Sides_NamedByDisciplines()
    {
        var (card, _, dialogs, _, temp) = Create(User(Roles.Designer, "ОВ"));
        using var _ = temp;
        await card.LoadAsync();

        card.Assignee.ShouldBe("КР и ОВ");
        await card.PassCommand.ExecuteAsync(null);

        dialogs.Offered.ShouldNotBeNull().Select(o => (o.Assignee, o.Label))
            .ShouldBe([(CollisionAssignee.SideA, "КР"), (CollisionAssignee.SideB, "ОВ")]);
    }

    /// <summary>Коллизия внутри раздела: разделом стороны не различить — подписи по моделям; одна модель — «сторона A / B».</summary>
    [Test]
    public void Sides_SameDiscipline_NamedByModels()
    {
        var row = FakeClashApi.Row(7).Collision with { DisciplineA = "ОВ", ModelNameA = "DON_OV1.rvt", ModelNameB = "DON_OV2.rvt" };
        SideNames.Label(row, CollisionAssignee.SideA).ShouldBe("DON_OV1");
        SideNames.Label(row, CollisionAssignee.Both).ShouldBe("DON_OV1 и DON_OV2");

        var same = row with { ModelNameB = "DON_OV1.rvt" };
        SideNames.Label(same, CollisionAssignee.SideB).ShouldBe("сторона B");
        SideNames.Label(same, CollisionAssignee.Both).ShouldBe("обе стороны");
    }

    /// <summary>Категория из выгрузки — код Revit; в списке и карточке — по-русски, как в вебе. Незнакомая — как есть.</summary>
    [Test]
    public void Side_Title_CategoryInRussian()
    {
        var side = FakeClashApi.Row(7).A with { Category = "OST_PipeCurves" };
        side.Title.ShouldBe("Трубы · Стена · 200 · 7");
        (side with { Category = "OST_Unknown" }).Title.ShouldStartWith("OST_Unknown · ");
    }

    /// <summary>Смена статуса в ленте — «было → стало», как в вебе; запись без автора — от системы.</summary>
    [Test]
    public async Task Card_Feed_StatusChangesAndSystemEntries()
    {
        var (card, api, _, row, temp) = Create(User(Roles.Viewer));
        using var _ = temp;
        var at = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
        api.Card = _ => new CollisionCardDto(row.Collision,
        [
            new FeedEntryDto(at, "Status", "Петров", "", CollisionStatus.Active, CollisionStatus.FixedPending, null, null, null),
            new FeedEntryDto(at, "Status", "Петров", "в гильзе", CollisionStatus.Active, CollisionStatus.Fake, null, null, null),
            new FeedEntryDto(at, "Found", null, "найдена прогоном", null, null, 1, 30, null)
        ]);

        await card.LoadAsync();

        var time = at.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        card.Feed.ShouldBe([
            $"{time} · Петров: активная → исправлена (ждёт)",
            $"{time} · Петров: активная → фиктивная — в гильзе",
            $"{time} · система: найдена прогоном"
        ]);
    }

    [Test]
    public void Card_NavigateMessages()
    {
        var (card, _, _, _, temp) = Create(User(Roles.Viewer));
        using var _ = temp;

        card.ShowNavigation(new NavigateResult(false, OtherState.InLinkNotSelectable, "DON_KR.rvt"));
        card.NavigateMessage.ShouldBe("Элемент не найден — возможно, уже удалён или заменён. Исправление подтвердит следующий прогон\n" +
                                      "Второй элемент — в связи DON_KR.rvt: в этой версии Revit выделить его нельзя, данные — ниже");

        card.ShowNavigation(new NavigateResult(true, OtherState.NoLink, null));
        card.NavigateMessage.ShouldBe("Модель DON_KR.rvt не подгружена связью");

        card.ShowNavigation(new NavigateResult(true, OtherState.NotFoundInLink, "DON_KR.rvt : 1"));
        card.NavigateMessage.ShouldBe("Второй элемент не найден в связи DON_KR.rvt : 1");

        card.ShowNavigation(new NavigateResult(true, OtherState.Selected, "DON_KR.rvt"));
        card.NavigateMessage.ShouldBeNull();
    }

    [Test]
    public async Task Card_UnknownStatus_NoStatusButtons()
    {
        var (card, _, _, _, temp) = Create(User(Roles.Operator), CollisionStatus.Unknown);
        using var _ = temp;
        await card.LoadAsync();

        card.Status.ShouldBe("статус неизвестен");
        (card.CanFixed, card.CanFake, card.CanUnfake).ShouldBe((false, false, false));
        (card.CanPass, card.CanNote).ShouldBe((true, true));
    }

    [Test]
    public async Task Card_UnexpectedError_ShownNotThrown()
    {
        var (card, api, _, _, temp) = Create(User(Roles.Designer, "ОВ"));
        using var _ = temp;
        api.Fail = new InvalidOperationException("сломалось");

        await card.LoadAsync();
        card.Error.ShouldBe("Непредвиденная ошибка: сломалось");

        card.Error = null;
        await card.TakeCommand.ExecuteAsync(null);
        card.Error.ShouldBe("Непредвиденная ошибка: сломалось");
        card.IsBusy.ShouldBeFalse();
    }

    [Test]
    public void Card_OpenIn3D_NoAddress_Message()
    {
        var (card, api, dialogs, _, temp) = Create(User(Roles.Viewer));
        using var _ = temp;
        api.Url = "";

        card.OpenIn3DCommand.Execute(null);

        dialogs.Opened.ShouldBeEmpty();
        card.Error.ShouldBe("Адрес ToadFlow неизвестен — войдите в ToadFlow заново");
    }

    [Test]
    public async Task Card_PassEmptyNote_Allowed()
    {
        var (card, api, dialogs, row, temp) = Create(User(Roles.Designer, "ОВ"));
        using var _ = temp;
        await card.LoadAsync();
        dialogs.Pass = new PassChoice(CollisionAssignee.SideA, "   ");

        await card.PassCommand.ExecuteAsync(null);

        api.Actions.ShouldBe([$"assignee {row.Collision.Id} SideA "]);
    }

    [Test]
    public void PassDialog_SideChosen_SavesWithoutNote()
    {
        var pass = new NoteViewModel("Передать", [new SideOption(CollisionAssignee.SideA, "КР")]);
        pass.SaveCommand.CanExecute(null).ShouldBeFalse();
        pass.Side = CollisionAssignee.SideA;
        pass.SaveCommand.CanExecute(null).ShouldBeTrue();

        var note = new NoteViewModel(CardViewModel.NoteTitle);
        note.SaveCommand.CanExecute(null).ShouldBeFalse(); // примечание без текста — нечего сохранять
    }

    [Test]
    public void Card_FixedLabelSameAsWeb()
    {
        // Как кнопка в карточке веба (CollisionCard.tsx); статус в строке — «исправлена (ждёт)», как StatusBadge веба.
        CardViewModel.FixedLabel.ShouldBe("Исправлена (ожидает подтверждения)");
        CollisionItemViewModel.StatusLabel(CollisionStatus.FixedPending).ShouldBe("исправлена (ждёт)");
    }

    [Test]
    public async Task Card_ImageFailed_ShowsReasonAndRefresh()
    {
        var (card, api, _, _, temp) = Create(User(Roles.Designer, "ОВ"));
        using var _ = temp;
        api.ImageFail = new ToadFlowRequestException(404, "Элемента нет в выгрузке модели DON_KR.rvt");

        await card.LoadAsync();

        (card.Image, card.ImageText, card.CanRefreshImage).ShouldBe((null, "Элемента нет в выгрузке модели DON_KR.rvt", true));

        api.ImageFail = null;
        await card.RefreshImageCommand.ExecuteAsync(null);

        card.Image.ShouldBe(new byte[] { 1 });
        (card.ImageText, card.CanRefreshImage).ShouldBe(("", false));
    }
}
