using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace ToadTools.Telemetry;

/// <summary>Запись очереди: файл, когда записан (время сборщика, UTC) и сама запись пачки.</summary>
public sealed record OutboxEntry(string Path, DateTime WrittenUtc, JsonObject Item);

/// <summary>
///     Очередь записей аналитики на диске: запись — файл. Копится всегда, независимо от входа в ToadFlow; уходит, когда
///     отправка получится. Время записи — время изменения файла, его ставит сама очередь по переданным часам. Ни один метод не
///     бросает: сбой диска значит, что запись пропала или полежит до следующего раза, но Revit работает как обычно.
/// </summary>
/// <remarks>
///     Файлы пишутся через временный файл и переименование: снятый посреди записи процесс не оставит половину записи. Очередь
///     общая для всех Revit на машине под этим пользователем Windows: имена разовых записей уникальны, ключи итогов содержат id
///     сессии.
/// </remarks>
public sealed class TelemetryOutbox
{
    public const int MaxAgeDays = 60;
    public const long MaxBytes = 50L * 1024 * 1024;

    private const string Rejected = "rejected";
    private const string PutPrefix = "p_";

    private static readonly UTF8Encoding Utf8 = new(false);
    private static int _sequence;

    private readonly string _folder;
    private readonly Func<DateTime> _utcNow;
    private readonly long _maxBytes;
    private readonly object _gate = new();

    public TelemetryOutbox(string folder, Func<DateTime> utcNow) : this(folder, utcNow, MaxBytes)
    {
    }

    internal TelemetryOutbox(string folder, Func<DateTime> utcNow, long maxBytes)
    {
        _folder = folder;
        _utcNow = utcNow;
        _maxBytes = maxBytes;
    }

    /// <summary>Разовая запись (operation, machine): новый файл.</summary>
    public void Add(JsonObject item)
    {
        var now = _utcNow();
        var sequence = Interlocked.Increment(ref _sequence) & 0xFFFFFF;
        Write($"{now.Ticks:D19}_{sequence:D8}_{Guid.NewGuid():N}.json", item, now);
    }

    /// <summary>Нарастающий итог (session, sessionDay, commandDay): файл с именем по ключу, прежний заменяется.</summary>
    public void Put(string key, JsonObject item) => Write(PutPrefix + SafeName(key) + ".json", item, _utcNow());

    /// <summary>До <paramref name="max" /> записей, старые первыми. Нечитаемый файл уходит в <c>rejected</c>.</summary>
    public IReadOnlyList<OutboxEntry> Take(int max)
    {
        var entries = new List<OutboxEntry>();
        try
        {
            if (!Directory.Exists(_folder)) return entries;
            foreach (var file in Ordered(Files(_folder)))
            {
                if (entries.Count >= max) break;
                try
                {
                    var written = File.GetLastWriteTimeUtc(file);
                    if (JsonNode.Parse(File.ReadAllText(file, Utf8)) is JsonObject item) entries.Add(new OutboxEntry(file, written, item));
                    else Move(file, Rejected);
                }
                catch (System.Text.Json.JsonException)
                {
                    Move(file, Rejected);
                }
                catch (IOException)
                {
                    // Файл пишется прямо сейчас или его забрал другой Revit — в следующий раз.
                }
            }
        }
        catch (Exception exception) when (IsDisk(exception))
        {
        }

        return entries;
    }

    /// <summary>Удаляет отправленные; файл, переписанный после чтения (новый итог), остаётся.</summary>
    public void Remove(IReadOnlyList<OutboxEntry> sent)
    {
        foreach (var entry in sent)
        {
            try
            {
                lock (_gate)
                {
                    if (File.Exists(entry.Path) && File.GetLastWriteTimeUtc(entry.Path) == entry.WrittenUtc) File.Delete(entry.Path);
                }
            }
            catch (Exception exception) when (IsDisk(exception))
            {
            }
        }
    }

    /// <summary>Откладывает записи в подпапку <c>rejected</c>: сервер их не принял, но выбрасывать сразу незачем.</summary>
    public void Reject(IReadOnlyList<OutboxEntry> entries)
    {
        foreach (var entry in entries) Move(entry.Path, Rejected);
    }

    /// <summary>Удаляет записи старше <see cref="MaxAgeDays" /> дней и самые старые сверх предела объёма, включая отложенные.</summary>
    public void Trim()
    {
        try
        {
            if (!Directory.Exists(_folder)) return;
            var rejected = Path.Combine(_folder, Rejected);
            var files = Files(_folder).Concat(Directory.Exists(rejected) ? Files(rejected) : [])
                .Select(path => new FileInfo(path)).OrderBy(f => f.LastWriteTimeUtc).ThenBy(f => f.Name, StringComparer.Ordinal).ToList();
            var cutoff = _utcNow().AddDays(-MaxAgeDays);
            var total = files.Sum(f => f.Length);
            foreach (var file in files)
            {
                if (file.LastWriteTimeUtc >= cutoff && total <= _maxBytes) break;
                total -= file.Length;
                Delete(file.FullName);
            }
        }
        catch (Exception exception) when (IsDisk(exception))
        {
        }
    }

    private void Write(string name, JsonObject item, DateTime now)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_folder);
                var path = Path.Combine(_folder, name);
                var temp = path + ".tmp";
                File.WriteAllText(temp, item.ToJsonString(), Utf8);
                File.SetLastWriteTimeUtc(temp, now);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
                // File.Replace на некоторых системах переносит время изменения прежнего файла — ставим своё.
                File.SetLastWriteTimeUtc(path, now);
            }
        }
        catch (Exception exception) when (IsDisk(exception))
        {
        }
    }

    private static IEnumerable<string> Files(string folder) => Directory.GetFiles(folder, "*.json");

    private static IEnumerable<string> Ordered(IEnumerable<string> files) =>
        files.Select(path => (Path: path, Written: File.GetLastWriteTimeUtc(path)))
            .OrderBy(f => f.Written).ThenBy(f => Path.GetFileName(f.Path), StringComparer.Ordinal).Select(f => f.Path);

    private void Move(string file, string subfolder)
    {
        try
        {
            lock (_gate)
            {
                if (!File.Exists(file)) return;
                var target = Directory.CreateDirectory(Path.Combine(_folder, subfolder)).FullName;
                var path = Path.Combine(target, Path.GetFileName(file));
                if (File.Exists(path)) File.Delete(path);
                File.Move(file, path);
            }
        }
        catch (Exception exception) when (IsDisk(exception))
        {
        }
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (IsDisk(exception))
        {
        }
    }

    private static string SafeName(string key)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(key.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private static bool IsDisk(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException;
}
