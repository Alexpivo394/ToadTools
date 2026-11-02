using System.Net;
using Collisions.Core;
using Newtonsoft.Json.Linq;
using Shouldly;
using ToadTools.Connection;

namespace ToadTools.Tests.Collisions;

public class ClashApiTests
{
    private static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name));

    private static (ClashApi Api, FakeHttp Http, TempFolder Temp) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var temp = new TempFolder();
        var store = new ConnectionStore(temp.File("toadflow.json"));
        store.Save(new ToadFlowConnection("http://toadflow:8080", "ivanov", "Иванов", "Designer", Guid.NewGuid(), "tf_x"));
        var http = new FakeHttp((request, _) => respond(request));
        return (new ClashApi(new ToadFlowClient(store, http)), http, temp);
    }

    [Test]
    public async Task Api_Collisions_BuildsQuery()
    {
        var (api, http, temp) = Create(_ => FakeHttp.Ok(Sample("model-collisions.json")));
        using var _ = temp;

        await api.CollisionsAsync("abc", new CollisionQuery("open", "mine", "КР × ОВ", "Этаж 1 / Этаж 2", "ua-1&x"), 2, CancellationToken.None);
        await api.CollisionsAsync("abc", new CollisionQuery(), 1, CancellationToken.None);

        http.Requests[0].Request.RequestUri!.PathAndQuery.ShouldBe(
            "/api/clash/models/abc/collisions?status=open&group=mine&pair=%D0%9A%D0%A0%20%C3%97%20%D0%9E%D0%92" +
            "&level=%D0%AD%D1%82%D0%B0%D0%B6%201%20%2F%20%D0%AD%D1%82%D0%B0%D0%B6%202&q=ua-1%26x&page=2&pageSize=100");
        http.Requests[1].Request.RequestUri!.PathAndQuery.ShouldBe("/api/clash/models/abc/collisions?status=open&group=all&page=1&pageSize=100");
    }

    [Test]
    public async Task Api_Dtos_DeserializeServerJson()
    {
        var (api, _, temp) = Create(request => FakeHttp.Ok(request.RequestUri!.AbsolutePath switch
        {
            "/api/clash/models/resolve" => Sample("model-resolve.json"),
            var path when path.EndsWith("/collisions") => Sample("model-collisions.json"),
            _ => Sample("collision-card.json")
        }));
        using var _ = temp;

        var resolve = await api.ResolveAsync("RSN://rs22/ЖК/DON_K1_OV.rvt", "DON_K1_OV.rvt", CancellationToken.None);
        var page = await api.CollisionsAsync("01a196c17e987078a2d487bee83122f0", new CollisionQuery(Status: "all"), 1, CancellationToken.None);
        var card = await api.CardAsync(page.Items[1].Collision.Id, CancellationToken.None);

        resolve.By.ShouldBe("name");
        var match = resolve.Matches.ShouldHaveSingleItem();
        (match.Key, match.Name, match.Discipline).ShouldBe(("01a196c17e987078a2d487bee83122f0", "DON_K1_OV.rvt", "ОВ"));
        match.Parts.ShouldHaveSingleItem().LastApprovedRunAt.ShouldNotBeNull();

        (page.Total, page.Mine, page.Other, page.Items.Count).ShouldBe((2, 1, 1, 2));
        var first = page.Items[0];
        (first.Side, first.MineToFix, first.Collision.Number, first.Collision.Status, first.Collision.Assignee)
            .ShouldBe(("B", true, 2, CollisionStatus.New, CollisionAssignee.Both));
        (first.Collision.ModelNameB, first.Collision.ElementIdB, first.Collision.LevelA).ShouldBe(("DON_K1_OV.rvt", 100002L, null));
        first.Collision.ImageVersion.ShouldNotBeNullOrWhiteSpace();
        page.Items[1].TakenBy.ShouldNotBeNull();
        page.Facets.Pairs.ShouldHaveSingleItem().Label.ShouldBe("КР × ОВ");
        page.Facets.Statuses.Select(s => s.Value).ShouldBe(["open", "fake", "fixed", "all"]);

        card.Collision.Assignee.ShouldBe(CollisionAssignee.SideA);
        card.Feed.ShouldNotBeEmpty();
    }

    [Test]
    public async Task Api_Actions_PostWebRoutes()
    {
        var (api, http, temp) = Create(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var _ = temp;
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");

        await api.TakeAsync(id, CancellationToken.None);
        await api.PassAsync(id, "SideB", "пусть ОВ", CancellationToken.None);
        await api.SetStatusAsync(id, "Fake", "Гильза", CancellationToken.None);
        await api.NoteAsync(id, "Проверю", CancellationToken.None);
        await api.VisitAsync(CancellationToken.None);

        http.Requests.Select(r => (r.Request.Method.Method, r.Request.RequestUri!.AbsolutePath)).ShouldBe([
            ("POST", $"/api/clash/collisions/{id}/take"),
            ("POST", $"/api/clash/collisions/{id}/assignee"),
            ("POST", $"/api/clash/collisions/{id}/status"),
            ("POST", $"/api/clash/collisions/{id}/notes"),
            ("POST", "/api/clash/my/visit")
        ]);
        JObject Body(int i) => JObject.Parse(http.Requests[i].Body);
        ((string?)Body(1)["assignee"], (string?)Body(1)["note"]).ShouldBe(("SideB", "пусть ОВ"));
        ((string?)Body(2)["status"], (string?)Body(2)["note"]).ShouldBe(("Fake", "Гильза"));
        ((string?)Body(3)["text"]).ShouldBe("Проверю");
    }

    [Test]
    public async Task Api_Image()
    {
        var (api, http, temp) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        using var _ = temp;
        var id = Guid.NewGuid();

        var image = await api.ImageAsync(id, "v1", CancellationToken.None);

        http.Requests[0].Request.RequestUri!.PathAndQuery.ShouldBe($"/api/clash/collisions/{id}/image?v=v1");
        image.ShouldBe([1, 2, 3]);
    }

    [Test]
    public async Task Api_UnknownStatusAndAssignee_ReadAsUnknown()
    {
        var card = JObject.Parse(Sample("collision-card.json"));
        card["collision"]!["status"] = "Archived";
        card["collision"]!["assignee"] = "Nobody";
        var (api, _, temp) = Create(_ => FakeHttp.Ok(card.ToString()));
        using var _ = temp;

        var read = await api.CardAsync(Guid.NewGuid(), CancellationToken.None);

        (read.Collision.Status, read.Collision.Assignee).ShouldBe((CollisionStatus.Unknown, CollisionAssignee.Unknown));
    }

    [Test]
    public void Api_ViewerUrl()
    {
        var (api, _, temp) = Create(_ => FakeHttp.Ok("{}"));
        using var _ = temp;
        var (part, collision) = (Guid.NewGuid(), Guid.NewGuid());

        api.ViewerUrl(part, collision).ShouldBe($"http://toadflow:8080/clash/checks/{part}?collision={collision}");
    }
}
