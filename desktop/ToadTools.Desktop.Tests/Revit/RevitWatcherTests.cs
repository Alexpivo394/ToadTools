using Shouldly;
using ToadTools.Desktop.Core.Revit;

namespace ToadTools.Desktop.Tests.Revit;

public class RevitWatcherTests
{
    private sealed class FakeProcess(bool canWatch = true) : IRevitProcess
    {
        private readonly TaskCompletionSource<bool> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task? Exited => canWatch ? _exited.Task : null;
        public bool HasExited { get; private set; }

        public void Exit()
        {
            HasExited = true;
            _exited.TrySetResult(true);
        }

        public void Dispose() { }
    }

    private sealed class FakeSource : IRevitProcessSource
    {
        public List<FakeProcess> Running { get; } = [];
        public int Calls { get; private set; }

        public IReadOnlyList<IRevitProcess> Current()
        {
            Calls++;
            lock (Running) return Running.Where(process => !process.HasExited).ToArray();
        }
    }

    [Test]
    public async Task NoRevit_CompletesImmediately()
    {
        var watcher = new RevitWatcher(new FakeSource(), TimeSpan.FromMilliseconds(50));

        watcher.AnyRunning().ShouldBeFalse();
        await watcher.WaitUntilNoneAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task TwoRevits_CompletesWhenBothExit()
    {
        var source = new FakeSource();
        FakeProcess a = new(), b = new();
        source.Running.AddRange([a, b]);
        var watcher = new RevitWatcher(source, TimeSpan.FromSeconds(30));

        var wait = watcher.WaitUntilNoneAsync(CancellationToken.None);
        a.Exit();
        await Task.Delay(50);
        wait.IsCompleted.ShouldBeFalse();
        b.Exit();

        await wait.WaitAsync(TimeSpan.FromSeconds(1));
        source.Calls.ShouldBe(2);
    }

    [Test]
    public async Task RevitStartedWhileWaiting_IsWaitedToo()
    {
        var source = new FakeSource();
        FakeProcess first = new(), second = new();
        source.Running.Add(first);
        var watcher = new RevitWatcher(source, TimeSpan.FromSeconds(30));

        var wait = watcher.WaitUntilNoneAsync(CancellationToken.None);
        source.Running.Add(second);
        first.Exit();
        await Task.Delay(100);
        wait.IsCompleted.ShouldBeFalse();
        second.Exit();

        await wait.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task ElevatedRevit_FallsBackToPolling()
    {
        var source = new FakeSource();
        var elevated = new FakeProcess(canWatch: false);
        source.Running.Add(elevated);
        var watcher = new RevitWatcher(source, TimeSpan.FromMilliseconds(50));

        var wait = watcher.WaitUntilNoneAsync(CancellationToken.None);
        await Task.Delay(120);
        wait.IsCompleted.ShouldBeFalse();
        elevated.Exit();

        await wait.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task Cancel_Throws()
    {
        var source = new FakeSource();
        source.Running.Add(new FakeProcess());
        using var cancel = new CancellationTokenSource();
        var watcher = new RevitWatcher(source, TimeSpan.FromSeconds(30));

        var wait = watcher.WaitUntilNoneAsync(cancel.Token);
        cancel.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => wait);
    }

    [Test]
    public void RealSource_DoesNotThrow() => new SystemRevitProcessSource().Current().ShouldNotBeNull();

    [Test]
    public void Detector_InstalledYears_InRange() =>
        RevitDetector.InstalledYears().ShouldAllBe(year => year >= 2020 && year <= 2030);
}
