using System.IO;
using System.Text.Json.Nodes;
using Shouldly;
using ToadTools.Connection;
using ToadTools.Telemetry;

namespace ToadTools.Tests.Telemetry;

public class TelemetrySenderTests
{
    private sealed class Rig : IDisposable
    {
        private readonly TempFolder _temp = new();
        private DateTime _now = new(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc);

        public Rig() => Outbox = new TelemetryOutbox(_temp.File("outbox"), () => _now = _now.AddMilliseconds(1));

        public TelemetryOutbox Outbox { get; }
        public bool Connected { get; set; } = true;
        public List<JsonObject> Posted { get; } = [];

        /// <summary>Ответ на n-ю пачку (с нуля): null — принята.</summary>
        public Func<int, Exception?> Answer { get; set; } = _ => null;

        public string Rejected => Path.Combine(_temp.File("outbox"), "rejected");

        public TelemetrySender Sender() => new(Outbox, () => Connected, (body, ct) =>
        {
            var index = Posted.Count;
            Posted.Add(body);
            ct.ThrowIfCancellationRequested();
            return Answer(index) is { } fail ? Task.FromException(fail) : Task.CompletedTask;
        });

        public void Fill(int count)
        {
            for (var i = 0; i < count; i++) Outbox.Add(new JsonObject { ["type"] = "operation", ["n"] = i });
        }

        public int Left => Outbox.Take(10_000).Count;

        public void Dispose() => _temp.Dispose();
    }

    [Test]
    public async Task Flush_450Items_ThreeBatches_QueueEmpty()
    {
        using var rig = new Rig();
        rig.Fill(450);

        (await rig.Sender().FlushAsync(CancellationToken.None)).ShouldBe(SendOutcome.Sent);

        rig.Posted.Select(b => b["items"]!.AsArray().Count).ShouldBe([200, 200, 50]);
        rig.Posted.ShouldAllBe(b => (int)b["version"]! == 1);
        rig.Posted.SelectMany(b => b["items"]!.AsArray()).Select(i => (int)i!["n"]!).ShouldBe(Enumerable.Range(0, 450));
        rig.Left.ShouldBe(0);
    }

    [Test]
    public async Task EmptyQueue_Nothing()
    {
        using var rig = new Rig();

        (await rig.Sender().FlushAsync(CancellationToken.None)).ShouldBe(SendOutcome.Nothing);
        rig.Posted.ShouldBeEmpty();
    }

    [Test]
    public async Task NotConnected_QueueKept()
    {
        using var rig = new Rig();
        rig.Fill(3);
        rig.Connected = false;

        (await rig.Sender().FlushAsync(CancellationToken.None)).ShouldBe(SendOutcome.NotConnected);

        rig.Posted.ShouldBeEmpty();
        rig.Left.ShouldBe(3);
    }

    [Test]
    public async Task Auth401_StopsAndKeeps()
    {
        using var rig = new Rig();
        rig.Fill(450);
        rig.Answer = _ => new ToadFlowAuthRequiredException();

        (await rig.Sender().FlushAsync(CancellationToken.None)).ShouldBe(SendOutcome.AuthRequired);

        rig.Posted.Count.ShouldBe(1);
        rig.Left.ShouldBe(450);
    }

    [Test]
    public async Task Unavailable_Keeps()
    {
        using var rig = new Rig();
        rig.Fill(3);
        rig.Answer = _ => new ToadFlowUnavailableException("нет сети");

        (await rig.Sender().FlushAsync(CancellationToken.None)).ShouldBe(SendOutcome.Unavailable);

        rig.Left.ShouldBe(3);
    }

    [Test]
    [Arguments(404)]
    [Arguments(500)]
    [Arguments(503)]
    [Arguments(403)]
    public async Task ServerRefused_Keeps(int status)
    {
        using var rig = new Rig();
        rig.Fill(3);
        rig.Answer = _ => new ToadFlowRequestException(status, "нет");

        (await rig.Sender().FlushAsync(CancellationToken.None)).ShouldBe(SendOutcome.Unavailable);

        rig.Posted.Count.ShouldBe(1);
        rig.Left.ShouldBe(3);
        Directory.Exists(rig.Rejected).ShouldBeFalse();
    }

    [Test]
    public async Task BadRequest400_BatchRejected_NextBatchSent()
    {
        using var rig = new Rig();
        rig.Fill(250);
        rig.Answer = index => index == 0 ? new ToadFlowRequestException(400, "неверная пачка") : null;

        (await rig.Sender().FlushAsync(CancellationToken.None)).ShouldBe(SendOutcome.Sent);

        rig.Posted.Count.ShouldBe(2);
        rig.Left.ShouldBe(0);
        Directory.GetFiles(rig.Rejected).Length.ShouldBe(200);
    }

    [Test]
    public async Task TokenRevokedBetweenBatches_FirstGone_RestKept()
    {
        using var rig = new Rig();
        rig.Fill(450);
        rig.Answer = index => index == 0 ? null : new ToadFlowAuthRequiredException();

        (await rig.Sender().FlushAsync(CancellationToken.None)).ShouldBe(SendOutcome.AuthRequired);

        rig.Left.ShouldBe(250);
        rig.Outbox.Take(1).Single().Item["n"]!.GetValue<int>().ShouldBe(200);

        rig.Answer = _ => null;
        rig.Posted.Clear();
        await rig.Sender().FlushAsync(CancellationToken.None);
        rig.Posted.SelectMany(b => b["items"]!.AsArray()).Select(i => (int)i!["n"]!).ShouldBe(Enumerable.Range(200, 250));
    }

    [Test]
    public async Task Cancelled_NoThrow_QueueKept()
    {
        using var rig = new Rig();
        rig.Fill(3);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        (await rig.Sender().FlushAsync(cancel.Token)).ShouldBe(SendOutcome.Stopped);

        rig.Left.ShouldBe(3);
    }

    [Test]
    public async Task PostThrowsUnexpected_NoThrow()
    {
        using var rig = new Rig();
        rig.Fill(3);
        var sender = new TelemetrySender(rig.Outbox, () => true, (_, _) => throw new InvalidOperationException("сбой"));

        (await sender.FlushAsync(CancellationToken.None)).ShouldBe(SendOutcome.Unavailable);

        rig.Left.ShouldBe(3);
    }

    [Test]
    public async Task ConnectedThrows_NoThrow()
    {
        using var rig = new Rig();
        rig.Fill(1);
        var sender = new TelemetrySender(rig.Outbox, () => throw new IOException(), (_, _) => Task.CompletedTask);

        (await sender.FlushAsync(CancellationToken.None)).ShouldBe(SendOutcome.NotConnected);
    }
}
