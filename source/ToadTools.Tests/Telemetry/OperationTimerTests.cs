using System.Text.Json.Nodes;
using Shouldly;
using ToadTools.Telemetry;

namespace ToadTools.Tests.Telemetry;

public class ModelRefTests
{
    [Test]
    public void ModelRef_RevitServer()
    {
        var model = ModelRef.From(null, "RSN://srv01/Проект/АР.rvt");

        model.ShouldBe(new ModelRef("АР.rvt", "RSN://srv01/Проект/АР.rvt", "RevitServer", "srv01"));
    }

    [Test]
    [Arguments("BIM 360://Проект/АР.rvt")]
    [Arguments("Autodesk Docs://Проект/АР.rvt")]
    [Arguments("cld://US/{guid}/АР.rvt")]
    public void ModelRef_Cloud(string path)
    {
        var model = ModelRef.From(path, @"C:\Users\ivanov\AppData\Local\Autodesk\Revit\АР.rvt");

        model.Storage.ShouldBe("Cloud");
        model.ServerHost.ShouldBeNull();
        model.Path.ShouldBe(path);
        model.Name.ShouldBe("АР.rvt");
    }

    [Test]
    [Arguments(@"\\fs\p\a.rvt")]
    [Arguments(@"C:\a.rvt")]
    public void ModelRef_File(string path)
    {
        var model = ModelRef.From(null, path);

        model.Storage.ShouldBe("File");
        model.ServerHost.ShouldBeNull();
        model.Name.ShouldBe("a.rvt");
    }

    [Test]
    public void ModelRef_LocalCopy_UsesCentral()
    {
        var model = ModelRef.From("RSN://srv01/П/АР.rvt", @"C:\Users\ivanov\Documents\АР_ivanov.rvt");

        model.ShouldBe(new ModelRef("АР.rvt", "RSN://srv01/П/АР.rvt", "RevitServer", "srv01"));
    }

    [Test]
    public void ModelRef_EmptyCentral_UsesFile()
    {
        ModelRef.From("", @"C:\a.rvt").Path.ShouldBe(@"C:\a.rvt");
    }
}

public class OperationTimerTests
{
    private sealed class Rig : IDisposable
    {
        private readonly TempFolder _temp = new();

        public Rig()
        {
            Outbox = new TelemetryOutbox(_temp.File("outbox"), () => Now);
            Timer = new OperationTimer(Outbox, () => Now, () => Session, "WS-01", 2024, () => FreeMemory);
        }

        public DateTime Now { get; set; } = new(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc);
        public Guid Session { get; } = Guid.NewGuid();
        public long FreeMemory { get; set; } = 8000;
        public TelemetryOutbox Outbox { get; }
        public OperationTimer Timer { get; }

        public List<JsonObject> Items()
        {
            Timer.Flush();
            return Outbox.Take(1000).Select(e => e.Item).ToList();
        }

        public void Dispose() => _temp.Dispose();
    }

    [Test]
    public void BeginEnd_WritesOperation_WithDurationAndFreeMemory()
    {
        using var rig = new Rig();
        rig.Timer.Begin("a", OperationKind.Sync, "RSN://srv01/П/АР.rvt");
        rig.FreeMemory = 100;
        rig.Now = rig.Now.AddSeconds(42.5);

        rig.Timer.End("a", OperationOutcome.Succeeded);

        var item = rig.Items().ShouldHaveSingleItem();
        ((string)item["type"]!).ShouldBe("operation");
        ((string)item["kind"]!).ShouldBe("Sync");
        ((long)item["durationMs"]!).ShouldBe(42500);
        ((long)item["freeMemoryMb"]!).ShouldBe(8000);
        ((string)item["outcome"]!).ShouldBe("Succeeded");
        ((string)item["modelPath"]!).ShouldBe("RSN://srv01/П/АР.rvt");
        ((string)item["modelName"]!).ShouldBe("АР.rvt");
        ((string)item["storage"]!).ShouldBe("RevitServer");
        ((string)item["serverHost"]!).ShouldBe("srv01");
        ((string)item["sessionId"]!).ShouldBe(rig.Session.ToString());
        ((string)item["machine"]!).ShouldBe("WS-01");
        ((int)item["revitYear"]!).ShouldBe(2024);
        Guid.TryParse((string)item["id"]!, out _).ShouldBeTrue();
        DateTime.Parse((string)item["startedAt"]!).ToUniversalTime().ShouldBe(new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public void End_BeforeFlush_NotOnDisk()
    {
        using var rig = new Rig();
        rig.Timer.Begin("a", OperationKind.Save, @"C:\a.rvt");
        rig.Timer.End("a", OperationOutcome.Succeeded);

        rig.Outbox.Take(10).ShouldBeEmpty();
        rig.Items().Count.ShouldBe(1);
    }

    [Test]
    public void End_WithoutBegin_Ignored()
    {
        using var rig = new Rig();

        rig.Timer.End("a", OperationOutcome.Succeeded);

        rig.Items().ShouldBeEmpty();
    }

    [Test]
    public void Begin_Twice_SameToken_Restarts()
    {
        using var rig = new Rig();
        rig.Timer.Begin("a", OperationKind.Open, @"C:\a.rvt");
        rig.Now = rig.Now.AddSeconds(10);
        rig.Timer.Begin("a", OperationKind.Open, @"C:\a.rvt");
        rig.Now = rig.Now.AddSeconds(5);

        rig.Timer.End("a", OperationOutcome.Succeeded);

        ((long)rig.Items().ShouldHaveSingleItem()["durationMs"]!).ShouldBe(5000);
    }

    [Test]
    public void Begin_NeverEnded_DroppedAfter24h()
    {
        using var rig = new Rig();
        rig.Timer.Begin("lost", OperationKind.Open, @"C:\a.rvt");
        rig.Now = rig.Now.AddHours(25);
        rig.Timer.Begin("b", OperationKind.Open, @"C:\b.rvt");

        rig.Timer.End("lost", OperationOutcome.Succeeded);

        rig.Timer.Pending.ShouldBe(1);
        rig.Items().ShouldBeEmpty();
    }

    [Test]
    public void Nested_OpenWithLinks_EachRecorded()
    {
        using var rig = new Rig();
        rig.Timer.Begin("open", OperationKind.Open, "RSN://srv01/П/АР.rvt");
        rig.Timer.Begin("link1", OperationKind.Link, "RSN://srv01/П/КР.rvt");
        rig.Now = rig.Now.AddSeconds(3);
        rig.Timer.End("link1", OperationOutcome.Succeeded);
        rig.Timer.Begin("link2", OperationKind.Link, @"\\fs\П\ОВ.rvt");
        rig.Now = rig.Now.AddSeconds(4);
        rig.Timer.End("link2", OperationOutcome.Failed);
        rig.Now = rig.Now.AddSeconds(10);
        rig.Timer.End("open", OperationOutcome.Succeeded);

        var items = rig.Items();
        items.Count.ShouldBe(3);
        var links = items.Where(i => (string)i["kind"]! == "Link").ToList();
        links.Count.ShouldBe(2);
        links.ShouldAllBe(l => (string)l["hostModelPath"]! == "RSN://srv01/П/АР.rvt");
        ((long)items.Single(i => (string)i["kind"]! == "Open")["durationMs"]!).ShouldBe(17000);
    }

    [Test]
    public void End_ModelRefOverridesBeginPath()
    {
        using var rig = new Rig();
        rig.Timer.Begin("a", OperationKind.Open, @"C:\Users\ivanov\Documents\АР_ivanov.rvt");

        rig.Timer.End("a", OperationOutcome.Succeeded, ModelRef.From("RSN://srv01/П/АР.rvt", @"C:\Users\ivanov\Documents\АР_ivanov.rvt"), 123456);

        var item = rig.Items().Single();
        ((string)item["modelPath"]!).ShouldBe("RSN://srv01/П/АР.rvt");
        ((string)item["storage"]!).ShouldBe("RevitServer");
        ((long)item["fileSizeBytes"]!).ShouldBe(123456);
    }

    [Test]
    public void FreeMemory_Throws_NoThrow()
    {
        using var temp = new TempFolder();
        var timer = new OperationTimer(new TelemetryOutbox(temp.File("o"), () => DateTime.UtcNow), () => DateTime.UtcNow, () => Guid.Empty, "WS", 2024,
            () => throw new InvalidOperationException());

        timer.Begin("a", OperationKind.Open, @"C:\a.rvt");
        timer.End("a", OperationOutcome.Succeeded);
        timer.Flush();
    }
}
