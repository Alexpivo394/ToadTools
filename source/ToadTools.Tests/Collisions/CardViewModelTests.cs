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

    public PassChoice? AskPass(CollisionAssignee current) => Pass;
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
        var card = new CardViewModel(api, row, me, dialogs, new ThumbnailCache(temp.File("images"), api.ImageAsync));
        return (card, api, dialogs, row, temp);
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

        card.OwnParameters.ShouldBe([new KeyValuePair<string, string>("ID", "ub-7")]);
        card.OtherParameters.ShouldBe([new KeyValuePair<string, string>("ID", "ua-7")]);
        card.Feed.ShouldHaveSingleItem().ShouldEndWith("Петров: проверю");
        card.Image.ShouldBe(new byte[] { 1 });
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
}
