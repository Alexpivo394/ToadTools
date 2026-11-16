using System.ComponentModel;
using System.Diagnostics;

namespace ToadTools.Desktop.Core.Revit;

/// <summary>Запущенный процесс Revit.</summary>
public interface IRevitProcess : IDisposable
{
    /// <summary>Завершается вместе с процессом; null — подписаться нельзя (Revit запущен от администратора).</summary>
    Task? Exited { get; }

    bool HasExited { get; }
}

/// <summary>Процессы Revit текущего пользователя.</summary>
public interface IRevitProcessSource
{
    IReadOnlyList<IRevitProcess> Current();
}

/// <summary>Revit.exe в сессии текущего пользователя: на терминальном сервере чужой Revit наши файлы не держит.</summary>
public sealed class SystemRevitProcessSource : IRevitProcessSource
{
    public IReadOnlyList<IRevitProcess> Current()
    {
        var session = Process.GetCurrentProcess().SessionId;
        var result = new List<IRevitProcess>();
        foreach (var process in Process.GetProcessesByName("Revit"))
        {
            bool ours;
            try
            {
                ours = process.SessionId == session && !process.HasExited;
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                // HasExited недоступен для процесса от администратора: считаем запущенным, если сессия наша
                ours = SafeSession(process) == session;
            }

            if (ours) result.Add(new SystemRevitProcess(process));
            else process.Dispose();
        }

        return result;
    }

    private static int SafeSession(Process process)
    {
        try
        {
            return process.SessionId;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return -1;
        }
    }

    private sealed class SystemRevitProcess : IRevitProcess
    {
        private readonly Process _process;

        public SystemRevitProcess(Process process)
        {
            _process = process;
            try
            {
                var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                process.Exited += (_, _) => exited.TrySetResult(true);
                process.EnableRaisingEvents = true;
                if (process.HasExited) exited.TrySetResult(true);
                Exited = exited.Task;
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                Exited = null;
            }
        }

        public Task? Exited { get; }

        public bool HasExited
        {
            get
            {
                try
                {
                    _process.Refresh();
                    return _process.HasExited;
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
                {
                    // Нет доступа к процессу от администратора: жив, пока его видно в списке процессов
                    return Process.GetProcessesByName("Revit").All(process => Id(process) != _process.Id);
                }
            }
        }

        private static int Id(Process process)
        {
            using (process) return process.Id;
        }

        public void Dispose() => _process.Dispose();
    }
}

/// <summary>
///     Ожидание момента, когда не запущено ни одного Revit. Не опрашивает: ждёт событие завершения процесса от
///     Windows, после каждого завершения один раз смотрит список (вдруг за это время открыли новый Revit). Опрос раз в
///     fallbackPoll — только для процессов, на завершение которых подписаться нельзя.
/// </summary>
public sealed class RevitWatcher(IRevitProcessSource source, TimeSpan fallbackPoll)
{
    public RevitWatcher() : this(new SystemRevitProcessSource(), TimeSpan.FromSeconds(30))
    {
    }

    public bool AnyRunning()
    {
        var processes = source.Current();
        foreach (var process in processes) process.Dispose();
        return processes.Count > 0;
    }

    public async Task WaitUntilNoneAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var processes = source.Current();
            if (processes.Count == 0) return;

            try
            {
                var waits = processes.Select(process => process.Exited ?? PollAsync(process, cancellationToken));
                var all = Task.WhenAll(waits);
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
                {
                    if (await Task.WhenAny(all, cancelled.Task).ConfigureAwait(false) == cancelled.Task)
                        throw new OperationCanceledException(cancellationToken);
                }
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }
    }

    private async Task PollAsync(IRevitProcess process, CancellationToken cancellationToken)
    {
        while (!process.HasExited) await Task.Delay(fallbackPoll, cancellationToken).ConfigureAwait(false);
    }
}
