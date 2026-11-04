using Newtonsoft.Json.Linq;
using Shouldly;
using ToadTools.Connection;

namespace ToadTools.Tests;

public class LoiOutboxTests
{
    private sealed class Server
    {
        public List<JObject> Posted { get; } = [];
        public Exception? Fail { get; set; }

        public Task Post(JObject body, CancellationToken ct)
        {
            if (Fail is { } fail) return Task.FromException(fail);
            Posted.Add(body);
            return Task.CompletedTask;
        }
    }

    private static string Body(Guid id) => new JObject { ["id"] = id.ToString(), ["summary"] = new JObject() }.ToString();

    [Test]
    public void Enqueue_NotConnectedYet_Queued()
    {
        using var temp = new TempFolder();
        var server = new Server();
        var connected = false;
        var outbox = new LoiOutbox(temp.File("outbox"), () => true, () => connected, server.Post);

        outbox.Enqueue(Guid.NewGuid(), Body(Guid.NewGuid())).ShouldBeTrue();
        var waiting = outbox.Flush();

        (waiting.Sent, waiting.Pending, waiting.Error).ShouldBe((0, 1, LoiOutbox.NotConnected));
        server.Posted.ShouldBeEmpty();

        connected = true;
        var sent = outbox.Flush();
        (sent.Sent, sent.Pending, sent.Error).ShouldBe((1, 0, null));
    }

    [Test]
    public void Enqueue_Disabled_NotQueued()
    {
        using var temp = new TempFolder();
        var outbox = new LoiOutbox(temp.File("outbox"), () => false, () => true, new Server().Post);

        outbox.Enqueue(Guid.NewGuid(), Body(Guid.NewGuid())).ShouldBeFalse();
        outbox.PendingCount().ShouldBe(0);
    }

    [Test]
    public void Flush_403NoBody_MovedToForbidden_NotDeleted()
    {
        using var temp = new TempFolder();
        var server = new Server { Fail = new ToadFlowRequestException(403, ToadFlowClient.NotEnoughRights) };
        var outbox = new LoiOutbox(temp.File("outbox"), () => true, () => true, server.Post);
        outbox.Enqueue(Guid.NewGuid(), Body(Guid.NewGuid()));
        outbox.Enqueue(Guid.NewGuid(), Body(Guid.NewGuid()));

        var result = outbox.Flush();

        (result.Sent, result.Pending, result.Error).ShouldBe((0, 0, LoiOutbox.NotEnoughRights));
        Directory.GetFiles(temp.File(@"outbox\forbidden"), "*.json").Length.ShouldBe(2);
    }

    [Test]
    public void Flush_403WithBody_Kept()
    {
        using var temp = new TempFolder();
        var server = new Server { Fail = new ToadFlowRequestException(403, "Профиль проверки отключён администратором") };
        var outbox = new LoiOutbox(temp.File("outbox"), () => true, () => true, server.Post);
        outbox.Enqueue(Guid.NewGuid(), Body(Guid.NewGuid()));
        outbox.Enqueue(Guid.NewGuid(), Body(Guid.NewGuid()));

        var result = outbox.Flush();

        (result.Sent, result.Pending, result.Error).ShouldBe((0, 2, "Профиль проверки отключён администратором"));
        Directory.Exists(temp.File(@"outbox\forbidden")).ShouldBeFalse();
    }

    [Test]
    public void Flush_400_RejectedOthersSent()
    {
        using var temp = new TempFolder();
        var server = new Server();
        var calls = 0;
        var outbox = new LoiOutbox(temp.File("outbox"), () => true, () => true, (body, ct) =>
            ++calls == 1 ? Task.FromException(new ToadFlowRequestException(400, "нет модели")) : server.Post(body, ct));
        outbox.Enqueue(Guid.NewGuid(), Body(Guid.NewGuid()));
        Thread.Sleep(5);
        outbox.Enqueue(Guid.NewGuid(), Body(Guid.NewGuid()));

        var result = outbox.Flush();

        (result.Sent, result.Pending, result.Error).ShouldBe((1, 0, "ToadFlow отверг итог: нет модели"));
        Directory.GetFiles(temp.File(@"outbox\rejected"), "*.json").Length.ShouldBe(1);
    }
}
