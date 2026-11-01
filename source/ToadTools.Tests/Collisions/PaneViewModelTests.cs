using Collisions.Core;
using Collisions.ViewModels;
using Shouldly;
using ToadTools.Connection;

namespace ToadTools.Tests.Collisions;

/// <summary>Подставной Revit: открытый документ и переходы к коллизиям.</summary>
public sealed class FakeRevit : IRevitBridge
{
    public (string? CentralPath, string FileName)? Document { get; set; } = ("RSN://srv/DON/DON_OV.rvt", "DON_OV.rvt");
    public List<NavigateRequest> Shown { get; } = [];
    public NavigateResult Result { get; set; } = new(true, OtherState.Selected, null);

    public Task<(string? CentralPath, string FileName)?> ActiveDocumentAsync() => Task.FromResult(Document);

    public Task<NavigateResult> ShowAsync(NavigateRequest request)
    {
        Shown.Add(request);
        return Task.FromResult(Result);
    }
}

public class PaneViewModelTests
{
    private static ModelMatch Match(string key, string path = "RSN://srv/DON/DON_OV.rvt") =>
        new(key, Guid.NewGuid(), "DON_OV.rvt", path, "ОВ", [new ModelPart(Guid.NewGuid(), "Часть 1", new DateTimeOffset(2026, 10, 30, 14, 5, 0, TimeSpan.Zero))]);

    private sealed record Setup(PaneViewModel Model, FakeClashApi Api, FakeRevit Revit, ConnectionStore Store, ModelChoices Choices, PaneSession Session,
        TempFolder Temp) : IDisposable
    {
        public void Dispose() => Temp.Dispose();
    }

    private static Setup Create(bool connected = true, PaneSession? session = null)
    {
        var temp = new TempFolder();
        var store = new ConnectionStore(temp.File("toadflow.json"));
        if (connected) store.Save(new ToadFlowConnection("http://toadflow", "ivanov", "Иванов", "Designer", Guid.NewGuid(), "tf_x"));
        var api = new FakeClashApi();
        var revit = new FakeRevit();
        var choices = new ModelChoices(temp.File("models.json"));
        session ??= new PaneSession();
        var thumbnails = new ThumbnailCache(temp.File("images"), api.ImageAsync);
        var model = new PaneViewModel(api, revit, store, choices, thumbnails, session, () => false, new FakeDialogs());
        return new Setup(model, api, revit, store, choices, session, temp);
    }

    [Test]
    public async Task Pane_NotConnected_Screen()
    {
        using var s = Create(connected: false);

        await s.Model.ActivateAsync();

        s.Model.Screen.ShouldBe(PaneScreen.NotConnected);
        s.Model.Message.ShouldBe("Войдите в ToadFlow, чтобы видеть коллизии");
        s.Api.PageRequests.ShouldBeEmpty();
    }

    [Test]
    public async Task Pane_NoDocument_Screen()
    {
        using var s = Create();
        s.Revit.Document = null;

        await s.Model.ActivateAsync();

        (s.Model.Screen, s.Model.Message).ShouldBe((PaneScreen.NoDocument, "Откройте модель проекта"));
    }

    [Test]
    public async Task Pane_NotFound_ShowsPath()
    {
        using var s = Create();
        string? askedPath = null, askedName = null;
        s.Api.Resolve = (path, name) =>
        {
            (askedPath, askedName) = (path, name);
            return new ModelResolve("none", []);
        };

        await s.Model.ActivateAsync();

        (askedPath, askedName).ShouldBe(("RSN://srv/DON/DON_OV.rvt", "DON_OV.rvt"));
        s.Model.Screen.ShouldBe(PaneScreen.NotFound);
        s.Model.Message.ShouldBe("Модель не участвует в проверках коллизий ToadFlow");
        s.Model.SearchedPath.ShouldBe("RSN://srv/DON/DON_OV.rvt");
    }

    [Test]
    public async Task Pane_TwoMatches_Choose_RemembersAndLoads()
    {
        using var s = Create();
        var (first, second) = (Match("k1"), Match("k2", @"\\fs\DON\DON_OV.rvt"));
        s.Api.Resolve = (_, _) => new ModelResolve("name", [first, second]);
        s.Api.Rows = [FakeClashApi.Row(1)];

        await s.Model.ActivateAsync();
        s.Model.Screen.ShouldBe(PaneScreen.Choose);
        s.Model.Message.ShouldBe("Таких моделей в ToadFlow несколько — выберите свою");
        s.Model.Matches.ShouldBe([first, second]);

        await s.Model.ChooseModelCommand.ExecuteAsync(second);

        s.Model.Screen.ShouldBe(PaneScreen.Ready);
        s.Api.PageRequests.ShouldHaveSingleItem().Key.ShouldBe("k2");
        s.Choices.Get("RSN://srv/DON/DON_OV.rvt").ShouldBe("k2");

        await s.Model.ActivateAsync(); // снова открыта та же модель — выбор помнится
        s.Model.Screen.ShouldBe(PaneScreen.Ready);
        s.Api.PageRequests.Last().Key.ShouldBe("k2");
    }

    [Test]
    public async Task Pane_AuthRequired_SwitchesToNotConnected()
    {
        using var s = Create();
        s.Api.Fail = new ToadFlowAuthRequiredException();

        await s.Model.ActivateAsync();

        s.Model.Screen.ShouldBe(PaneScreen.NotConnected);
        s.Model.Message.ShouldBe("Подключение к ToadFlow устарело — войдите заново");
        s.Store.Load().ShouldNotBeNull();
    }

    [Test]
    public async Task Pane_Unavailable_ShowsError()
    {
        using var s = Create();
        s.Api.Fail = new ToadFlowUnavailableException("Нет связи с ToadFlow: сервер не ответил за 15 с");

        await s.Model.ActivateAsync();

        (s.Model.Screen, s.Model.Message).ShouldBe((PaneScreen.Error, "Нет связи с ToadFlow: сервер не ответил за 15 с"));
    }

    [Test]
    public async Task Pane_Ready_VisitOncePerSession()
    {
        var session = new PaneSession();
        using var s = Create(session: session);
        s.Api.Resolve = (_, _) => new ModelResolve("path", [Match("k1")]);

        await s.Model.ActivateAsync();
        await s.Model.RefreshCommand.ExecuteAsync(null);
        await s.Model.ActivateAsync();

        s.Api.Visits.ShouldBe(1);
        s.Model.Header.ShouldBe("DON_OV.rvt · ОВ");
        s.Model.Parts.ShouldBe("Часть 1 — прогон " + new DateTimeOffset(2026, 10, 30, 14, 5, 0, TimeSpan.Zero).ToLocalTime().ToString("dd.MM.yyyy HH:mm"));
    }

    [Test]
    public async Task Pane_Groups_MineThenOther()
    {
        using var s = Create();
        s.Api.Resolve = (_, _) => new ModelResolve("path", [Match("k1")]);
        s.Api.Rows = [FakeClashApi.Row(2), FakeClashApi.Row(5), FakeClashApi.Row(1, mine: false)];
        (s.Api.Mine, s.Api.Other, s.Api.Fresh) = (2, 1, 3);

        await s.Model.ActivateAsync();

        s.Model.Items.Select(i => (i.Number, i.Group)).ShouldBe([(2, "Мои и общие"), (5, "Мои и общие"), (1, "Взяла другая сторона")]);
        s.Model.Counters.ShouldBe("мне править 2, взяла другая сторона 1, новых с прошлого захода 3");
        var other = s.Model.Items[2];
        other.Own.ShouldBe("Воздуховоды · Воздуховод · 500x300 · 100001");
        other.Other.ShouldBe("DON_KR.rvt: Стены · Стена · 200 · 1");
        other.Taken.ShouldBe("взял(а) Петров");
        other.Status.ShouldBe("активная");
    }

    [Test]
    public async Task Pane_FilterChange_ReloadsFirstPage_RememberedInSession()
    {
        var session = new PaneSession();
        using var s = Create(session: session);
        s.Api.Resolve = (_, _) => new ModelResolve("path", [Match("k1")]);
        await s.Model.ActivateAsync();

        s.Model.Search = "ub-3";
        await s.Model.ApplyFiltersCommand.ExecuteAsync(null);

        s.Api.PageRequests.Last().Query.Search.ShouldBe("ub-3");
        session.Query.Search.ShouldBe("ub-3");
    }

    [Test]
    public async Task Pane_LoadMore_Appends()
    {
        using var s = Create();
        s.Api.Resolve = (_, _) => new ModelResolve("path", [Match("k1")]);
        s.Api.Rows = Enumerable.Range(1, 130).Select(i => FakeClashApi.Row(i)).ToList();

        await s.Model.ActivateAsync();
        s.Model.HasMore.ShouldBeTrue();
        await s.Model.LoadMoreCommand.ExecuteAsync(null);

        s.Model.Items.Count.ShouldBe(130);
        s.Model.HasMore.ShouldBeFalse();
    }

    [Test]
    public async Task Pane_ArrowDownOnLast_LoadsMore()
    {
        using var s = Create();
        s.Api.Resolve = (_, _) => new ModelResolve("path", [Match("k1")]);
        s.Api.Rows = Enumerable.Range(1, 130).Select(i => FakeClashApi.Row(i)).ToList();
        await s.Model.ActivateAsync();
        s.Model.SelectedItem = s.Model.Items[99];

        await s.Model.MoveAsync(1);

        s.Api.PageRequests.Last().Page.ShouldBe(2);
        s.Model.SelectedItem!.Number.ShouldBe(101);
        await s.Model.MoveAsync(-1);
        s.Model.SelectedItem!.Number.ShouldBe(100);
    }

    [Test]
    public async Task Pane_OpenItem_ShowsInRevitAndOpensCard()
    {
        using var s = Create();
        s.Api.Resolve = (_, _) => new ModelResolve("path", [Match("k1")]);
        s.Api.Rows = [FakeClashApi.Row(1)];
        s.Revit.Result = new NavigateResult(true, OtherState.NoLink, null);
        await s.Model.ActivateAsync();

        await s.Model.OpenAsync(s.Model.Items[0]);

        var request = s.Revit.Shown.ShouldHaveSingleItem();
        request.ShouldBe(new NavigateRequest("ub-1", "DON_KR.rvt", "", "ua-1", (1000, 2000, 3000)));
        s.Model.Card.ShouldNotBeNull().Number.ShouldBe(1);
        s.Model.Card.NavigateMessage.ShouldBe("Модель DON_KR.rvt не подгружена связью");
        s.Api.MeRequests.ShouldBe(1);
    }

    [Test]
    public async Task Pane_CardChanged_RefreshesList()
    {
        using var s = Create();
        s.Api.Resolve = (_, _) => new ModelResolve("path", [Match("k1")]);
        s.Api.Rows = [FakeClashApi.Row(1)];
        await s.Model.ActivateAsync();
        await s.Model.OpenAsync(s.Model.Items[0]);
        var before = s.Api.PageRequests.Count;

        await s.Model.Card!.TakeCommand.ExecuteAsync(null);

        s.Api.PageRequests.Count.ShouldBe(before + 1);
        s.Model.SelectedItem!.Number.ShouldBe(1);
    }

    [Test]
    public async Task Pane_SelectItem_RaisesNavigate()
    {
        using var s = Create();
        s.Api.Resolve = (_, _) => new ModelResolve("path", [Match("k1")]);
        s.Api.Rows = [FakeClashApi.Row(1)];
        await s.Model.ActivateAsync();
        ModelCollisionRow? requested = null;
        s.Model.NavigateRequested += (_, row) => requested = row;

        s.Model.SelectedItem = s.Model.Items[0];

        requested.ShouldNotBeNull().Collision.Number.ShouldBe(1);
    }
}
