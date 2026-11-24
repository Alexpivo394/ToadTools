using System.IO;
using System.Text.Json.Nodes;
using Shouldly;
using ToadTools.Telemetry;

namespace ToadTools.Tests.Telemetry;

public class SessionTrackerTests
{
    private sealed class Rig : IDisposable
    {
        private readonly TempFolder _temp = new();

        public Rig()
        {
            Outbox = new TelemetryOutbox(_temp.File("outbox"), () => Now);
            Processes = new Dictionary<int, DateTime>();
        }

        // 20:00 по Москве
        public DateTime Now { get; set; } = new(2026, 11, 24, 17, 0, 0, DateTimeKind.Utc);
        public TelemetryOutbox Outbox { get; }
        public Dictionary<int, DateTime> Processes { get; }
        public string States => _temp.File("sessions");

        public SessionTracker Tracker() =>
            new(States, Outbox, () => Now, utc => utc.AddHours(3), pid => Processes.TryGetValue(pid, out var started) ? started : null);

        public SessionStart Start(int pid = 100) => new("WS-01", "ivanov", 2024, "24.3.0.24", "4.6.0", pid, Now.AddSeconds(-5));

        public List<JsonObject> Items(string type) => Outbox.Take(1000).Select(e => e.Item).Where(i => (string?)i["type"] == type).ToList();

        public void Dispose() => _temp.Dispose();
    }

    private static void Minutes(Rig rig, SessionTracker tracker, int count, bool active)
    {
        for (var i = 0; i < count; i++)
        {
            rig.Now = rig.Now.AddMinutes(1);
            tracker.Tick(active, 1000);
        }
    }

    [Test]
    public void Tick_ActiveMinutes_Counted()
    {
        using var rig = new Rig();
        var tracker = rig.Tracker();
        tracker.Start(rig.Start());

        Minutes(rig, tracker, 6, true);
        Minutes(rig, tracker, 4, false);
        tracker.Snapshot();

        var session = rig.Items("session").ShouldHaveSingleItem();
        ((int)session["activeSeconds"]!).ShouldBe(360);
        ((string)session["end"]!).ShouldBe("Running");
        ((string)session["id"]!).ShouldBe(tracker.SessionId.ToString());
        ((string)session["machine"]!).ShouldBe("WS-01");
        ((int)rig.Items("sessionDay").ShouldHaveSingleItem()["activeSeconds"]!).ShouldBe(360);
    }

    [Test]
    public void Tick_GapOver5Minutes_NotActive()
    {
        using var rig = new Rig();
        var tracker = rig.Tracker();
        tracker.Start(rig.Start());
        Minutes(rig, tracker, 1, true);

        rig.Now = rig.Now.AddHours(3);
        tracker.Tick(true, 1000);
        tracker.Snapshot();

        ((int)rig.Items("session").Single()["activeSeconds"]!).ShouldBe(60);
    }

    [Test]
    public void Midnight_SplitsDays()
    {
        using var rig = new Rig();
        rig.Now = new DateTime(2026, 11, 24, 20, 58, 0, DateTimeKind.Utc); // 23:58 по Москве
        var tracker = rig.Tracker();
        tracker.Start(rig.Start());
        Minutes(rig, tracker, 1, true); // 23:59
        tracker.Command(new CommandRun("Export", "Выгрузка", "ToadTools", "ToadTools", CommandOutcome.Succeeded, 100));
        Minutes(rig, tracker, 2, true); // 00:00, 00:01
        tracker.Command(new CommandRun("Export", "Выгрузка", "ToadTools", "ToadTools", CommandOutcome.Succeeded, 100));
        tracker.Snapshot();

        rig.Items("sessionDay").Select(d => ((string)d["day"]!, (int)d["activeSeconds"]!)).ShouldBe([("2026-11-24", 60), ("2026-11-25", 120)], ignoreOrder: true);
        rig.Items("commandDay").Select(d => ((string)d["day"]!, (int)d["count"]!)).ShouldBe([("2026-11-24", 1), ("2026-11-25", 1)], ignoreOrder: true);
    }

    [Test]
    public void Snapshot_Unchanged_WritesNothing()
    {
        using var rig = new Rig();
        var tracker = rig.Tracker();
        tracker.Start(rig.Start());
        Minutes(rig, tracker, 1, true);
        tracker.Snapshot();
        rig.Outbox.Remove(rig.Outbox.Take(100));

        rig.Now = rig.Now.AddSeconds(10);
        tracker.Snapshot();

        rig.Outbox.Take(100).ShouldBeEmpty();
    }

    [Test]
    public void Stop_WritesNormal_RemovesState()
    {
        using var rig = new Rig();
        var tracker = rig.Tracker();
        tracker.Start(rig.Start());
        Minutes(rig, tracker, 2, true);
        Directory.GetFiles(rig.States).Length.ShouldBe(1);

        tracker.Stop();

        var session = rig.Items("session").ShouldHaveSingleItem();
        ((string)session["end"]!).ShouldBe("Normal");
        ((int)session["activeSeconds"]!).ShouldBe(120);
        Directory.GetFiles(rig.States).ShouldBeEmpty();
    }

    [Test]
    public void Start_LeftoverState_DeadProcess_Abnormal()
    {
        using var rig = new Rig();
        var crashed = rig.Tracker();
        crashed.Start(rig.Start(pid: 100));
        Minutes(rig, crashed, 3, true);
        crashed.Command(new CommandRun("Export", "Выгрузка", "ToadTools", "ToadTools", CommandOutcome.Succeeded, 100));
        Minutes(rig, crashed, 1, false);
        var lastTick = rig.Now;
        rig.Outbox.Remove(rig.Outbox.Take(100));

        rig.Now = rig.Now.AddHours(2);
        var next = rig.Tracker();
        next.Start(rig.Start(pid: 200));

        var session = rig.Items("session").ShouldHaveSingleItem();
        ((string)session["id"]!).ShouldBe(crashed.SessionId.ToString());
        ((string)session["end"]!).ShouldBe("Abnormal");
        DateTime.Parse((string)session["seenAt"]!).ToUniversalTime().ShouldBe(lastTick);
        ((int)session["activeSeconds"]!).ShouldBe(180);
        rig.Items("sessionDay").ShouldHaveSingleItem();
        ((int)rig.Items("commandDay").ShouldHaveSingleItem()["count"]!).ShouldBe(1);
        Directory.GetFiles(rig.States).Select(Path.GetFileNameWithoutExtension).ShouldBe([next.SessionId.ToString()]);
    }

    [Test]
    public void Start_LeftoverState_LiveProcess_Untouched()
    {
        using var rig = new Rig();
        var neighbour = rig.Tracker();
        var start = rig.Start(pid: 100);
        neighbour.Start(start);
        Minutes(rig, neighbour, 1, true);
        rig.Outbox.Remove(rig.Outbox.Take(100));
        rig.Processes[100] = start.ProcessStartedUtc;

        var second = rig.Tracker();
        second.Start(rig.Start(pid: 200));

        rig.Items("session").ShouldBeEmpty();
        Directory.GetFiles(rig.States).Length.ShouldBe(2);
    }

    [Test]
    public void Start_LeftoverState_PidReused_Abnormal()
    {
        using var rig = new Rig();
        var crashed = rig.Tracker();
        var start = rig.Start(pid: 100);
        crashed.Start(start);
        Minutes(rig, crashed, 1, true);
        rig.Outbox.Remove(rig.Outbox.Take(100));
        rig.Processes[100] = start.ProcessStartedUtc.AddHours(5);

        rig.Tracker().Start(rig.Start(pid: 200));

        ((string)rig.Items("session").ShouldHaveSingleItem()["end"]!).ShouldBe("Abnormal");
    }

    [Test]
    public void Start_CorruptState_Deleted_NoThrow()
    {
        using var rig = new Rig();
        Directory.CreateDirectory(rig.States);
        File.WriteAllText(Path.Combine(rig.States, Guid.NewGuid() + ".json"), "{ мусор");

        var tracker = rig.Tracker();
        tracker.Start(rig.Start());

        Directory.GetFiles(rig.States).Select(Path.GetFileNameWithoutExtension).ShouldBe([tracker.SessionId.ToString()]);
        rig.Items("session").ShouldBeEmpty();
    }

    [Test]
    public void Command_Counts_FailedCancelledDuration()
    {
        using var rig = new Rig();
        var tracker = rig.Tracker();
        tracker.Start(rig.Start());

        tracker.Command(new CommandRun("Export", "Выгрузка", "ToadTools", "ToadTools", CommandOutcome.Succeeded, 100));
        tracker.Command(new CommandRun("Export", "Выгрузка", "ToadTools", "ToadTools", CommandOutcome.Failed, 200));
        tracker.Command(new CommandRun("Export", "Выгрузка", "ToadTools", "ToadTools", CommandOutcome.Cancelled, 300));
        tracker.Command(new CommandRun("ID_OBJECTS_WALL", "Стена", "Revit", "Архитектура", CommandOutcome.Succeeded, 0));
        tracker.Snapshot();

        var commands = rig.Items("commandDay");
        var export = commands.Single(c => (string)c["commandId"]! == "Export");
        ((int)export["count"]!, (int)export["failed"]!, (int)export["cancelled"]!, (long)export["durationMs"]!).ShouldBe((3, 1, 1, 600L));
        ((string)export["source"]!).ShouldBe("ToadTools");
        ((string)export["sessionId"]!).ShouldBe(tracker.SessionId.ToString());
        var wall = commands.Single(c => (string)c["commandId"]! == "ID_OBJECTS_WALL");
        ((string)wall["tab"]!).ShouldBe("Архитектура");
    }

    [Test]
    public void PeakMemory_IsMax()
    {
        using var rig = new Rig();
        var tracker = rig.Tracker();
        tracker.Start(rig.Start());

        tracker.Tick(true, 3000);
        tracker.Tick(true, 9000);
        tracker.Tick(true, 4000);
        tracker.Snapshot();

        ((long)rig.Items("session").Single()["peakMemoryMb"]!).ShouldBe(9000);
    }

    [Test]
    public void BeforeStart_AllMethods_NoThrow()
    {
        using var rig = new Rig();
        var tracker = rig.Tracker();

        tracker.Tick(true, 1);
        tracker.Command(new CommandRun("Export", "Выгрузка", "ToadTools", null, CommandOutcome.Succeeded, 1));
        tracker.Snapshot();
        tracker.Stop();

        rig.Outbox.Take(10).ShouldBeEmpty();
    }
}

public class SessionTrackerIdTests
{
    [Test]
    public void SessionId_KnownBeforeStart_KeptAfter()
    {
        using var temp = new TempFolder();
        var tracker = new SessionTracker(temp.File("s"), new TelemetryOutbox(temp.File("o"), () => DateTime.UtcNow), () => DateTime.UtcNow, t => t, _ => null);
        var before = tracker.SessionId;

        tracker.Start(new SessionStart("WS", "u", 2024, "b", "4.6.0", 1, DateTime.UtcNow));

        before.ShouldNotBe(Guid.Empty);
        tracker.SessionId.ShouldBe(before);
    }
}
