using Shouldly;
using ToadTools.Connection;

namespace ToadTools.Tests.Notifications;

internal sealed class FakeNotificationSource : INotificationSource
{
    public bool Connected { get; set; } = true;
    public int Unread { get; set; }
    public List<NotificationDto> Items { get; } = [];
    public Exception? Failure { get; set; }
    public int CountCalls { get; private set; }

    public bool IsConnected => Connected;

    public Task<int> UnreadAsync(CancellationToken ct)
    {
        CountCalls++;
        if (Failure is { } failure) throw failure;
        return Task.FromResult(Unread);
    }

    public Task<List<NotificationDto>> LatestAsync(int take, CancellationToken ct) => Task.FromResult(Items.OrderByDescending(n => n.Id).Take(take).ToList());
}

public class NotificationPollerTests
{
    private static NotificationDto Item(long id, string kind = "request.message", bool read = false, string? link = "/families/requests/r1") =>
        new(id, kind, $"Заявка № {id}", "текст", link, 1, DateTimeOffset.Now, read);

    private static (NotificationPoller Poller, FakeNotificationSource Source, List<NotificationToastModel> Shown) Setup()
    {
        var source = new FakeNotificationSource();
        var shown = new List<NotificationToastModel>();
        return (new NotificationPoller(source, shown.Add), source, shown);
    }

    [Test]
    public async Task FirstTick_SummarisesUnread_ThenOnlyNewOnes()
    {
        var (poller, source, shown) = Setup();
        source.Items.AddRange([Item(1), Item(2)]);
        source.Unread = 2;

        await poller.TickAsync(CancellationToken.None);
        shown.Single().Title.ShouldBe("Непрочитанных уведомлений: 2");

        await poller.TickAsync(CancellationToken.None);
        shown.Count.ShouldBe(1);

        var link = $"/families/requests/{Guid.NewGuid()}";
        source.Items.Add(Item(3, "request.review", link: link));
        source.Unread = 3;
        await poller.TickAsync(CancellationToken.None);
        var last = shown.Last();
        (last.Title, last.Link, last.CanAccept).ShouldBe(("Заявка № 3", link, true));
    }

    [Test]
    public async Task SeveralNew_GluedIntoOne()
    {
        var (poller, source, shown) = Setup();
        await poller.TickAsync(CancellationToken.None);
        source.Items.AddRange([Item(5), Item(6), Item(7)]);
        source.Unread = 3;

        await poller.TickAsync(CancellationToken.None);

        var toast = shown.Single();
        toast.Title.ShouldBe("Новых уведомлений: 3");
        toast.Text.ShouldContain("Заявка № 7");
        toast.Ids.ShouldBe([7L, 6L, 5L]);
    }

    [Test]
    public async Task OldServer404_StopsPolling_OtherErrorsAreSilent()
    {
        var (poller, source, shown) = Setup();
        source.Failure = new TimeoutException();
        await poller.TickAsync(CancellationToken.None);
        poller.Disabled.ShouldBeFalse();

        source.Failure = new ToadFlowRequestException(404, "нет");
        await poller.TickAsync(CancellationToken.None);
        await poller.TickAsync(CancellationToken.None);

        poller.Disabled.ShouldBeTrue();
        source.CountCalls.ShouldBe(2);
        shown.ShouldBeEmpty();
    }

    [Test]
    public async Task NotConnected_NoRequests()
    {
        var (poller, source, _) = Setup();
        source.Connected = false;

        await poller.TickAsync(CancellationToken.None);

        source.CountCalls.ShouldBe(0);
    }

    [Test]
    public void RequestId_FromLink()
    {
        var id = Guid.NewGuid();
        NotificationToastModel.RequestIdOf($"/families/requests/{id}").ShouldBe(id);
        NotificationToastModel.RequestIdOf("/clash/my").ShouldBeNull();
    }
}
