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

    /// <param name="Host">У связи — открытие или обновление, внутри которого она грузится.</param>
    private sealed record Started(OperationKind Kind, string Path, string? HostModelPath, DateTime At, long? FreeMemoryMb, object? Host);

    private readonly object _gate = new();
    private readonly Dictionary<object, Started> _pending = new();

    /// <summary>Готовые записи связей, чей хост ещё идёт: в конце хоста им ставится его модель (у локальной копии — хранилища).</summary>
    private readonly Dictionary<object, List<JsonObject>> _nested = new();
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
    /// <param name="hostModelPath">
    ///     У связи — модель, в которую она загружается; null — модель, которая сейчас открывается или обновляется до последней, если
    ///     есть: Revit грузит связи внутри этих операций.
    /// </param>
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
                foreach (var stale in _pending.Where(p => now - p.Value.At > Forget).Select(p => p.Key).ToList())
                {
                    _pending.Remove(stale);
                    Release(stale, null);
                }

                object? host = null;
                if (kind == OperationKind.Link && hostModelPath is null)
                {
                    var running = _pending.Where(p => p.Value.Kind is OperationKind.Open or OperationKind.Reload).OrderByDescending(p => p.Value.At)
                        .FirstOrDefault();
                    if (running.Key is not null)
                    {
                        host = running.Key;
                        hostModelPath = running.Value.Path;
                    }
                }

                _pending[token] = new Started(kind, path, hostModelPath, now, free, host);
            }
        }
        catch (Exception exception) when (Safe(exception))
        {
        }
    }

    /// <summary>Забывает начатую операцию: оказалось, что её не считаем (семейство, документ связи).</summary>
    public void Discard(object token)
    {
        lock (_gate)
        {
            _pending.Remove(token);
            Release(token, null);
        }
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
                var item = new JsonObject
                {
                    ["type"] = "operation", ["id"] = Guid.NewGuid().ToString(), ["sessionId"] = sessionId().ToString(), ["machine"] = machine,
                    ["kind"] = started.Kind.ToString(), ["startedAt"] = Iso(started.At),
                    ["durationMs"] = Math.Max(0L, (long)Math.Round((now - started.At).TotalMilliseconds)), ["outcome"] = outcome.ToString(),
                    ["modelName"] = reference.Name, ["modelPath"] = reference.Path, ["storage"] = reference.Storage, ["serverHost"] = reference.ServerHost,
                    ["hostModelPath"] = started.HostModelPath, ["fileSizeBytes"] = fileSizeBytes, ["freeMemoryMb"] = started.FreeMemoryMb,
                    ["revitYear"] = revitYear
                };
                if (started.Host is not null && _pending.ContainsKey(started.Host))
                {
                    if (!_nested.TryGetValue(started.Host, out var held)) _nested[started.Host] = held = [];
                    held.Add(item);
                    return;
                }

                _done.Add(item);
                Release(token, reference.Path);
            }
        }
        catch (Exception exception) when (Safe(exception))
        {
        }
    }

    /// <summary>Связи, которые ждали конца хоста, — в готовые; хост-модель — его модель, если она известна (под замком).</summary>
    private void Release(object host, string? hostModelPath)
    {
        if (!_nested.TryGetValue(host, out var links)) return;
        _nested.Remove(host);
        foreach (var link in links)
        {
            if (hostModelPath is not null) link["hostModelPath"] = hostModelPath;
            _done.Add(link);
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
