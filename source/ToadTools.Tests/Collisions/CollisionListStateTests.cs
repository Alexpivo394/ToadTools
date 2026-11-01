using Collisions.Core;
using Shouldly;

namespace ToadTools.Tests.Collisions;

public class CollisionListStateTests
{
    [Test]
    public async Task List_Load_ThenMore_AppendsAndStopsAtTotal()
    {
        var api = new FakeClashApi { Rows = Enumerable.Range(1, 250).Select(i => FakeClashApi.Row(i)).ToList(), Mine = 250 };
        var list = new CollisionListState(api, "ov");

        await list.LoadAsync();
        (list.Items.Count, list.Total, list.Mine, list.HasMore).ShouldBe((100, 250, 250, true));

        await list.MoreAsync();
        await list.MoreAsync();
        (list.Items.Count, list.HasMore).ShouldBe((250, false));
        list.Items.Select(i => i.Collision.Number).ShouldBe(Enumerable.Range(1, 250));

        await list.MoreAsync(); // больше нечего — запроса нет
        api.PageRequests.Select(r => r.Page).ShouldBe([1, 2, 3]);
        api.PageRequests.ShouldAllBe(r => r.Key == "ov");
    }

    [Test]
    public async Task List_Refresh_KeepsSelectionById()
    {
        var api = new FakeClashApi { Rows = Enumerable.Range(1, 150).Select(i => FakeClashApi.Row(i)).ToList() };
        var list = new CollisionListState(api, "ov");
        await list.LoadAsync();
        await list.MoreAsync();
        list.Selected = list.Items[120];
        var selectedId = list.Selected.Collision.Id;

        // на сервере коллизия сменила «кто правит», первая строка ушла
        api.Rows = api.Rows.Skip(1).Select(r => r.Collision.Id == selectedId ? r with { MineToFix = false } : r).ToList();
        await list.RefreshAsync();

        api.PageRequests.Select(r => r.Page).ShouldBe([1, 2, 1, 2]);
        list.Items.Count.ShouldBe(149);
        list.Selected.ShouldNotBeNull().Collision.Id.ShouldBe(selectedId);
        list.Selected.MineToFix.ShouldBeFalse();
    }

    [Test]
    public async Task List_FilterChange_ResetsToFirstPage()
    {
        var api = new FakeClashApi { Rows = Enumerable.Range(1, 150).Select(i => FakeClashApi.Row(i)).ToList() };
        var list = new CollisionListState(api, "ov");
        await list.LoadAsync();
        await list.MoreAsync();
        list.Selected = list.Items[3];

        await list.SetQueryAsync(list.Query with { Group = "mine", Pair = "КР × ОВ" });

        api.PageRequests.Last().ShouldBe(("ov", new CollisionQuery("open", "mine", "КР × ОВ"), 1));
        list.Items.Count.ShouldBe(100);
        list.Query.Group.ShouldBe("mine");
        list.Selected.ShouldNotBeNull().Collision.Number.ShouldBe(4); // осталась в выборке — выбрана и дальше
    }
}
