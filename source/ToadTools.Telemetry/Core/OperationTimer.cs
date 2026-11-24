using System.Globalization;
using System.Text.Json.Nodes;

namespace ToadTools.Telemetry;

public enum OperationKind
{
    Open,
    Link,
    Sync,
    Save,
    Reload
}

public enum OperationOutcome
{
    Succeeded,
    Failed,
    Cancelled
}

/// <summary>
///     Замер операций с моделью по парам событий Revit («начало» и «конец»). <see cref="Begin" /> и <see cref="End" /> зовутся из
///     потока Revit и работают только с памятью; готовые записи ложатся на диск в <see cref="Flush" /> из фонового потока.
///     Ни один метод не бросает.
/// </summary>
/// <param name="freeMemoryMb">Свободная оперативная память машины, МБ: снимается на начало операции.</param>
public sealed class OperationTimer(TelemetryOutbox outbox, Func<DateTime> utcNow, Func<Guid> sessionId, string machine, int revitYear, Func<long> freeMemoryMb)
{
    /// <summary>Начатая операция без конца дольше этого срока забывается: конец уже не придёт.</summary>
    public static readonly TimeSpan Forget = TimeSpan.FromHours(24);

    private sealed record Started(OperationKind Kind, string Path, string? HostModelPath, DateTime At, long? FreeMemoryMb);

    private readonly object _gate = new();
    private readonly Dictionary<object, Started> _pending = new();
    private readonly List<JsonObject> _done = [];
    private readonly Dictionary<int, string> _localFiles = new();

    /// <summary>Сколько операций начато и не закончено.</summary>
    public int Pending
    {
        get
        {
            lock (_gate) return _pending.Count;
        }
    }

    /// <param name="token">То, по чему пара событий узнаёт друг друга (путь модели или документ).</param>
    /// <param name="hostModelPath">У связи — модель, в которую она загружается; null — открываемая сейчас модель, если есть.</param>
    public void Begin(object token, OperationKind kind, string path, string? hostModelPath = null)
    {
        try
        {
            var now = utcNow();
            long? free = null;
            try
            {
                free = freeMemoryMb();
            }
            catch (Exception exception) when (Safe(exception))
            {
            }

            lock (_gate)
            {
                foreach (var stale in _pending.Where(p => now - p.Value.At > Forget).Select(p => p.Key).ToList()) _pending.Remove(stale);
                if (kind == OperationKind.Link && hostModelPath is null)
                    hostModelPath = _pending.Values.Where(p => p.Kind == OperationKind.Open).OrderByDescending(p => p.At).FirstOrDefault()?.Path;
                _pending[token] = new Started(kind, path, hostModelPath, now, free);
            }
        }
        catch (Exception exception) when (Safe(exception))
        {
        }
    }

    /// <summary>Забывает начатую операцию: оказалось, что её не считаем (семейство, документ связи).</summary>
    public void Discard(object token)
    {
        lock (_gate) _pending.Remove(token);
    }

    /// <param name="model">Уточнённая модель (после открытия известен путь хранилища); null — по пути из <see cref="Begin" />.</param>
    /// <param name="localFile">Файл модели на этой машине: его размер читается в <see cref="Flush" />, если он на локальном диске.</param>
    public void End(object token, OperationOutcome outcome, ModelRef? model = null, long? fileSizeBytes = null, string? localFile = null)
    {
        try
        {
            var now = utcNow();
            lock (_gate)
            {
                if (!_pending.TryGetValue(token, out var started)) return;
                _pending.Remove(token);
                var reference = model ?? ModelRef.From(null, started.Path);
                if (fileSizeBytes is null && !string.IsNullOrEmpty(localFile)) _localFiles[_done.Count] = localFile!;
                _done.Add(new JsonObject
                {
                    ["type"] = "operation", ["id"] = Guid.NewGuid().ToString(), ["sessionId"] = sessionId().ToString(), ["machine"] = machine,
                    ["kind"] = started.Kind.ToString(), ["startedAt"] = Iso(started.At),
                    ["durationMs"] = Math.Max(0L, (long)Math.Round((now - started.At).TotalMilliseconds)), ["outcome"] = outcome.ToString(),
                    ["modelName"] = reference.Name, ["modelPath"] = reference.Path, ["storage"] = reference.Storage, ["serverHost"] = reference.ServerHost,
                    ["hostModelPath"] = started.HostModelPath, ["fileSizeBytes"] = fileSizeBytes, ["freeMemoryMb"] = started.FreeMemoryMb,
                    ["revitYear"] = revitYear
                });
            }
        }
        catch (Exception exception) when (Safe(exception))
        {
        }
    }

    /// <summary>Готовые записи — в очередь на диске. Из фонового потока.</summary>
    public void Flush()
    {
        List<JsonObject> done;
        Dictionary<int, string> files;
        lock (_gate)
        {
            done = [.. _done];
            files = new Dictionary<int, string>(_localFiles);
            _done.Clear();
            _localFiles.Clear();
        }

        for (var i = 0; i < done.Count; i++)
        {
            if (files.TryGetValue(i, out var file)) done[i]["fileSizeBytes"] = LocalSize(file);
            outbox.Add(done[i]);
        }
    }

    /// <summary>Размер файла на локальном диске; сетевой путь не трогаем — обращение к сети может быть долгим.</summary>
    private static long? LocalSize(string path)
    {
        try
        {
            if (path.StartsWith(@"\\", StringComparison.Ordinal) || !System.IO.Path.IsPathRooted(path)) return null;
            var root = System.IO.Path.GetPathRoot(path);
            if (root is null || new System.IO.DriveInfo(root).DriveType != System.IO.DriveType.Fixed) return null;
            var info = new System.IO.FileInfo(path);
            return info.Exists ? info.Length : null;
        }
        catch (Exception exception) when (Safe(exception))
        {
            return null;
        }
    }

    private static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture);

    private static bool Safe(Exception exception) => exception is not (OutOfMemoryException or StackOverflowException);
}
