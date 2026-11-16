using ToadTools.Desktop.Core.Revit;

namespace ToadTools.Desktop.Tests.Revit;

/// <summary>Фейковый процесс Revit: завершается по Exit().</summary>
public sealed class FakeRevitProcess(bool canWatch = true) : IRevitProcess
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

/// <summary>Фейковый список процессов Revit.</summary>
public sealed class FakeRevitSource : IRevitProcessSource
{
    private readonly List<FakeRevitProcess> _running = [];
    public int Calls { get; private set; }

    public FakeRevitProcess Start(bool canWatch = true)
    {
        var process = new FakeRevitProcess(canWatch);
        lock (_running) _running.Add(process);
        return process;
    }

    public IReadOnlyList<IRevitProcess> Current()
    {
        lock (_running)
        {
            Calls++;
            return _running.Where(process => !process.HasExited).ToArray();
        }
    }
}
