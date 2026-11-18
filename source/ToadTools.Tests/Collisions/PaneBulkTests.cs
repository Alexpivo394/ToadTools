using Collisions.Core;
using Collisions.ViewModels;
using Shouldly;
using ToadTools.Connection;

namespace ToadTools.Tests.Collisions;

/// <summary>Выделение нескольких коллизий в панели и массовые действия над ними.</summary>
public class PaneBulkTests
{
    private static readonly Guid PartA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid PartB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private sealed record Setup(PaneViewModel Model, FakeClashApi Api, FakeRevit Revit, FakeDialogs Dialogs, TempFolder Temp) : IDisposable
    {
        public void Dispose() => Temp.Dispose();
    }

    private static async Task<Setup> CreateAsync(params ModelCollisionRow[] rows)
    {
        var temp = new TempFolder();
        var store = new ConnectionStore(temp.File("toadflow.json"));
        store.Save(new ToadFlowConnection("http://toadflow", "ivanov", "Иванов", "Designer", Guid.NewGuid(), "tf_x"));
        var api = new FakeClashApi
        {
            Rows = [..rows],
            Resolve = (_, _) => new ModelResolve("path",
                [new ModelMatch("k1", Guid.NewGuid(), "DON_OV.rvt", "RSN://srv/DON/DON_OV.rvt", "ОВ", [new ModelPart(PartA, "Часть 1", null)])])
        };
        var revit = new FakeRevit();
        var dialogs = new FakeDialogs();
        var model = new PaneViewModel(api, revit, store, new ModelChoices(temp.File("models.json")), new ThumbnailCache(temp.File("images"), api.ImageAsync),
            new PaneSession(), () => false, dialogs);
        await model.ActivateAsync();
        return new Setup(model, api, revit, dialogs, temp);
    }

    [Test]
    public async Task MultiSelect_HidesCard_AndDoesNotNavigate()
    {
        using var s = await CreateAsync(FakeClashApi.Row(1), FakeClashApi.Row(2));
        await s.Model.OpenAsync(s.Model.Items[0]);
        s.Model.CardVisible.ShouldBeTrue();

        s.Model.SetSelection([s.Model.Items[0], s.Model.Items[1]]);
        // Ctrl+щелчок снял первую строку: список сам делает выбранной вторую — переход не нужен.
        s.Model.SelectedItem = s.Model.Items[1];

        s.Model.IsMultiSelect.ShouldBeTrue();
        s.Model.SelectionText.ShouldBe("Выделено 2");
        s.Model.CardVisible.ShouldBeFalse();
        s.Revit.Shown.Count.ShouldBe(1);
    }

    [Test]
    public async Task BackToOne_OpensItsCard()
    {
        using var s = await CreateAsync(FakeClashApi.Row(1), FakeClashApi.Row(2));
        await s.Model.OpenAsync(s.Model.Items[0]);
        s.Model.SetSelection([s.Model.Items[0], s.Model.Items[1]]);

        s.Model.SelectedItem = s.Model.Items[1];
        s.Model.SetSelection([s.Model.Items[1]]);

        await WaitUntil(() => s.Model.Card?.Number == 2);
        s.Model.CardVisible.ShouldBeTrue();
        s.Revit.Shown.Count.ShouldBe(2);
    }

    [Test]
    public async Task Fake_AsksNoteOnce_OneRequestPerPart_RefreshesList()
    {
        using var s = await CreateAsync(FakeClashApi.Row(1), FakeClashApi.Row(2), FakeClashApi.Row(3, partId: PartB));
        s.Dialogs.Note = " гильза ";
        s.Model.SetSelection(s.Model.Items.ToList());
        var pages = s.Api.PageRequests.Count;

        await s.Model.BulkFakeCommand.ExecuteAsync(null);

        s.Dialogs.Asked.ShouldBe([CardViewModel.FakeTitle]);
        s.Api.Bulks.Select(b => (b.Part, b.Action, b.Ids.Count, b.Note)).ShouldBe([(PartA, "Fake", 2, "гильза"), (PartB, "Fake", 1, "гильза")]);
        s.Model.Status.ShouldBe("Изменено 3 из 3");
        s.Api.PageRequests.Count.ShouldBe(pages + 1);
    }

    [Test]
    public async Task Fixed_Declined_NothingSent()
    {
        using var s = await CreateAsync(FakeClashApi.Row(1), FakeClashApi.Row(2));
        s.Dialogs.Confirmed = false;
        s.Model.SetSelection(s.Model.Items.ToList());

        await s.Model.BulkFixedCommand.ExecuteAsync(null);

        s.Dialogs.Confirms.ShouldHaveSingleItem().ShouldBe("Изменить 2 коллизии: «Исправлена (ожидает подтверждения)». Продолжить?");
        s.Api.Bulks.ShouldBeEmpty();
    }

    [Test]
    public async Task Pass_OtherSide_IsSideOfEachRow_NoteGoesToFeed()
    {
        // Открытая модель — сторона B у первой и сторона A у второй: «другая сторона» у них разная.
        using var s = await CreateAsync(FakeClashApi.Row(1, side: "B"), FakeClashApi.Row(2, side: "A"));
        s.Dialogs.Pass = new PassChoice(CollisionAssignee.SideB, "пусть правят");
        s.Model.SetSelection(s.Model.Items.ToList());

        await s.Model.BulkPassCommand.ExecuteAsync(null);

        s.Dialogs.Offered!.Select(o => (o.Assignee, o.Label)).ShouldBe([
            (CollisionAssignee.SideA, "своя сторона"), (CollisionAssignee.SideB, "другая сторона"), (CollisionAssignee.Both, "обе стороны")
        ]);
        var (one, two) = (s.Api.Rows[0].Collision.Id, s.Api.Rows[1].Collision.Id);
        s.Api.Bulks.Select(b => (b.Action, b.Assignee, string.Join(",", b.Ids), b.Note)).ShouldBe([
            ("Assignee", "SideA", one.ToString(), null),
            ("Assignee", "SideB", two.ToString(), null),
            ("Note", null, $"{one},{two}", "пусть правят")
        ]);
    }

    [Test]
    public async Task Refusals_AreListedInStatus()
    {
        using var s = await CreateAsync(FakeClashApi.Row(1), FakeClashApi.Row(2), FakeClashApi.Row(3));
        s.Api.BulkAnswer = _ => new BulkResult(1, [new BulkRefusal(2, "Нет прав")], 1);
        s.Model.SetSelection(s.Model.Items.ToList());

        await s.Model.BulkTakeCommand.ExecuteAsync(null);

        s.Api.Bulks.ShouldHaveSingleItem().Action.ShouldBe("Take");
        s.Model.Status.ShouldBe("Изменено 1 из 3. Уже были такими: 1. Не изменены: №2 — Нет прав");
    }

    [Test]
    public async Task Viewer_NoBulkActions()
    {
        using var s = await CreateAsync(FakeClashApi.Row(1), FakeClashApi.Row(2));
        s.Api.Me = new UserInfo(Guid.NewGuid(), "boss", "Начальник", "Viewer", []);

        s.Model.SetSelection(s.Model.Items.ToList());

        await WaitUntil(() => !s.Model.CanBulkEdit);
    }

    [Test]
    public async Task ClearSelection_BackToList()
    {
        using var s = await CreateAsync(FakeClashApi.Row(1), FakeClashApi.Row(2));
        s.Model.SetSelection(s.Model.Items.ToList());

        s.Model.ClearSelectionCommand.Execute(null);

        s.Model.SelectedItem.ShouldBeNull();
        s.Model.IsMultiSelect.ShouldBeFalse();
    }

    private static async Task WaitUntil(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) await Task.Delay(10);
        done().ShouldBeTrue();
    }
}
