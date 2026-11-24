using System.IO;
using System.Text.Json.Nodes;
using Shouldly;
using ToadTools.Telemetry;

namespace ToadTools.Tests.Telemetry;

public class TelemetryOutboxTests
{
    private sealed class Clock
    {
        public DateTime Now { get; set; } = new(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc);

        public DateTime Get() => Now;
    }

    private static JsonObject Item(string type, int n) => new() { ["type"] = type, ["n"] = n };

    [Test]
    public void Add_Take_OldestFirst()
    {
        using var temp = new TempFolder();
        var clock = new Clock();
        var outbox = new TelemetryOutbox(temp.File("outbox"), clock.Get);

        outbox.Add(Item("operation", 1));
        clock.Now = clock.Now.AddMinutes(1);
        outbox.Put("session-a", Item("session", 2));
        clock.Now = clock.Now.AddMinutes(1);
        outbox.Add(Item("operation", 3));

        outbox.Take(10).Select(e => (int)e.Item["n"]!).ShouldBe([1, 2, 3]);
        outbox.Take(2).Select(e => (int)e.Item["n"]!).ShouldBe([1, 2]);
    }

    [Test]
    public void Add_SameMoment_KeepsOrderOfAdding()
    {
        using var temp = new TempFolder();
        var outbox = new TelemetryOutbox(temp.File("outbox"), new Clock().Get);

        for (var i = 0; i < 20; i++) outbox.Add(Item("operation", i));

        outbox.Take(50).Select(e => (int)e.Item["n"]!).ShouldBe(Enumerable.Range(0, 20));
    }

    [Test]
    public void Put_SameKey_KeepsLatestOnly()
    {
        using var temp = new TempFolder();
        var clock = new Clock();
        var outbox = new TelemetryOutbox(temp.File("outbox"), clock.Get);

        outbox.Put("session-a", Item("session", 1));
        clock.Now = clock.Now.AddMinutes(1);
        outbox.Put("session-a", Item("session", 2));
        outbox.Put("session-b", Item("session", 3));

        outbox.Take(10).Select(e => (int)e.Item["n"]!).ShouldBe([2, 3]);
    }

    [Test]
    public void Remove_SkipsFileRewrittenAfterTake()
    {
        using var temp = new TempFolder();
        var clock = new Clock();
        var outbox = new TelemetryOutbox(temp.File("outbox"), clock.Get);
        outbox.Put("session-a", Item("session", 1));
        outbox.Add(Item("operation", 2));

        var taken = outbox.Take(10);
        clock.Now = clock.Now.AddMinutes(1);
        outbox.Put("session-a", Item("session", 3));
        outbox.Remove(taken);

        outbox.Take(10).Select(e => (int)e.Item["n"]!).ShouldBe([3]);
    }

    [Test]
    public void Trim_DropsOlderThan60Days()
    {
        using var temp = new TempFolder();
        var clock = new Clock();
        var outbox = new TelemetryOutbox(temp.File("outbox"), clock.Get);
        outbox.Add(Item("operation", 1));
        clock.Now = clock.Now.AddDays(30);
        outbox.Add(Item("operation", 2));
        outbox.Reject(outbox.Take(1));
        clock.Now = clock.Now.AddDays(31);

        outbox.Trim();

        outbox.Take(10).Select(e => (int)e.Item["n"]!).ShouldBe([2]);
        Directory.GetFiles(temp.File(Path.Combine("outbox", "rejected"))).ShouldBeEmpty();
    }

    [Test]
    public void Trim_OverLimit_DropsOldestFirst()
    {
        using var temp = new TempFolder();
        var clock = new Clock();
        var big = new string('x', 400);
        var outbox = new TelemetryOutbox(temp.File("outbox"), clock.Get, maxBytes: 1000);
        for (var i = 0; i < 5; i++)
        {
            outbox.Add(new JsonObject { ["type"] = "operation", ["n"] = i, ["pad"] = big });
            clock.Now = clock.Now.AddMinutes(1);
        }

        outbox.Trim();

        outbox.Take(10).Select(e => (int)e.Item["n"]!).ShouldBe([3, 4]);
    }

    [Test]
    public void Take_CorruptFile_MovedToRejected_OthersReturned()
    {
        using var temp = new TempFolder();
        var clock = new Clock();
        var outbox = new TelemetryOutbox(temp.File("outbox"), clock.Get);
        outbox.Add(Item("operation", 1));
        File.WriteAllText(Path.Combine(temp.File("outbox"), "00000000000000000001_0_broken.json"), "{ не json");
        outbox.Add(Item("operation", 2));

        outbox.Take(10).Select(e => (int)e.Item["n"]!).ShouldBe([1, 2]);
        Directory.GetFiles(Path.Combine(temp.File("outbox"), "rejected")).Length.ShouldBe(1);
    }

    [Test]
    public void Reject_MovesOutOfQueue()
    {
        using var temp = new TempFolder();
        var outbox = new TelemetryOutbox(temp.File("outbox"), new Clock().Get);
        outbox.Add(Item("operation", 1));
        outbox.Add(Item("operation", 2));

        outbox.Reject(outbox.Take(1));

        outbox.Take(10).Select(e => (int)e.Item["n"]!).ShouldBe([2]);
    }

    [Test]
    public void AllMethods_NoFolder_NoThrow()
    {
        using var temp = new TempFolder();
        // Папку очереди не создать: на её месте лежит файл.
        File.WriteAllText(temp.File("outbox"), "файл");
        var outbox = new TelemetryOutbox(temp.File("outbox"), new Clock().Get);

        outbox.Add(Item("operation", 1));
        outbox.Put("key", Item("session", 1));
        outbox.Take(10).ShouldBeEmpty();
        outbox.Remove([]);
        outbox.Reject([]);
        outbox.Trim();

        new TelemetryOutbox(temp.File("missing"), new Clock().Get).Take(10).ShouldBeEmpty();
    }
}
