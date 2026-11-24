using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ToadTools.Telemetry;

/// <summary>
///     Учёт одного запуска Revit: активное время по дням, запуски команд, наибольшая память. Итоги — нарастающие записи очереди
///     (<c>session</c>, <c>sessionDay</c>, <c>commandDay</c>). Состояние раз в минуту ложится на диск: если Revit упадёт, следующий
///     запуск найдёт файл и отправит сессию как аварийную.
/// </summary>
/// <remarks>
///     <see cref="Command" /> зовётся из потока Revit и только меняет память; диск — в <see cref="Tick" />, <see cref="Snapshot" />,
///     <see cref="Start" /> и <see cref="Stop" />, которые зовутся из фонового потока. Ни один метод не бросает.
/// </remarks>
/// <param name="toLocal">Время UTC → местное время машины: по нему день.</param>
/// <param name="processStartedUtc">Время старта живого процесса с таким id; null — процесса нет.</param>
public sealed class SessionTracker(
    string stateFolder,
    TelemetryOutbox outbox,
    Func<DateTime> utcNow,
    Func<DateTime, DateTime> toLocal,
    Func<int, DateTime?> processStartedUtc)
{
    /// <summary>Отметки дальше друг от друга — компьютер спал или процесс висел: время между ними активным не считается.</summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(5);

    /// <summary>Расхождение времени старта процесса, при котором это всё ещё тот же процесс.</summary>
    private static readonly TimeSpan SameProcess = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly object _gate = new();
    private SessionState? _state;
    private DateTime _lastTick;
    private bool _sessionDirty;
    private readonly HashSet<string> _dirtyDays = [];
    private readonly HashSet<string> _dirtyCommands = [];

    public Guid SessionId => _state?.Id ?? Guid.Empty;

    /// <summary>Аварийные сессии прошлых запусков → в очередь как <c>end: Abnormal</c>; затем начинает свою.</summary>
    public void Start(SessionStart start)
    {
        try
        {
            RecoverLeftovers();
            var now = utcNow();
            lock (_gate)
            {
                _state = new SessionState
                {
                    Id = Guid.NewGuid(), Machine = start.Machine, WindowsUser = start.WindowsUser, RevitYear = start.RevitYear,
                    RevitBuild = start.RevitBuild, ToadTools = start.ToadTools, ProcessId = start.ProcessId, ProcessStartedUtc = start.ProcessStartedUtc,
                    StartedAt = now, SeenAt = now
                };
                _lastTick = now;
                _sessionDirty = true;
                SaveState(_state);
            }
        }
        catch (Exception exception) when (Safe(exception))
        {
        }
    }

    /// <summary>Отметка раз в минуту: <paramref name="active" /> — Revit впереди и был ввод за 5 минут; память процесса в МБ.</summary>
    public void Tick(bool active, long memoryMb)
    {
        try
        {
            lock (_gate)
            {
                if (_state is not { } state) return;
                var now = utcNow();
                var gap = now - _lastTick;
                _lastTick = now;
                state.SeenAt = now;
                state.PeakMemoryMb = Math.Max(state.PeakMemoryMb, memoryMb);
                if (active && gap > TimeSpan.Zero && gap <= MaxGap)
                {
                    var seconds = (int)Math.Round(gap.TotalSeconds);
                    state.ActiveSeconds += seconds;
                    var day = Day(now);
                    state.Days[day] = (state.Days.TryGetValue(day, out var had) ? had : 0) + seconds;
                    _dirtyDays.Add(day);
                }

                _sessionDirty = true;
                SaveState(state);
            }
        }
        catch (Exception exception) when (Safe(exception))
        {
        }
    }

    /// <summary>Запуск команды — в счётчик дня. Только память: зовётся из потока Revit.</summary>
    public void Command(CommandRun run)
    {
        try
        {
            lock (_gate)
            {
                if (_state is not { } state) return;
                var day = Day(utcNow());
                var count = state.Commands.FirstOrDefault(c => c.Day == day && c.Source == run.Source && c.CommandId == run.CommandId);
                if (count is null)
                {
                    count = new CommandCount { Day = day, Source = run.Source, CommandId = run.CommandId };
                    state.Commands.Add(count);
                }

                count.Name = run.Name;
                count.Tab = run.Tab;
                count.Count++;
                if (run.Outcome == CommandOutcome.Failed) count.Failed++;
                if (run.Outcome == CommandOutcome.Cancelled) count.Cancelled++;
                count.DurationMs += Math.Max(0, run.DurationMs);
                _dirtyCommands.Add(count.Key);
            }
        }
        catch (Exception exception) when (Safe(exception))
        {
        }
    }

    /// <summary>Кладёт в очередь текущие итоги (session Running, sessionDay, commandDay), если они изменились.</summary>
    public void Snapshot()
    {
        try
        {
            lock (_gate)
            {
                if (_state is not { } state) return;
                Queue(state, "Running", _sessionDirty, _dirtyDays, _dirtyCommands);
                _sessionDirty = false;
                _dirtyDays.Clear();
                _dirtyCommands.Clear();
            }
        }
        catch (Exception exception) when (Safe(exception))
        {
        }
    }

    /// <summary>Обычный выход: итоги с <c>end: Normal</c>, файл состояния удаляется.</summary>
    public void Stop()
    {
        try
        {
            lock (_gate)
            {
                if (_state is not { } state) return;
                state.SeenAt = utcNow();
                Queue(state, "Normal", true, state.Days.Keys, state.Commands.Select(c => c.Key));
                File.Delete(StatePath(state.Id));
                _state = null;
            }
        }
        catch (Exception exception) when (Safe(exception))
        {
        }
    }

    private void RecoverLeftovers()
    {
        if (!Directory.Exists(stateFolder)) return;
        foreach (var file in Directory.GetFiles(stateFolder, "*.json"))
        {
            try
            {
                var state = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(file, Encoding.UTF8), Json);
                if (state is null || state.Id == Guid.Empty)
                {
                    File.Delete(file);
                    continue;
                }

                // Соседний Revit на этой же машине ещё работает — его сессию не трогаем.
                if (processStartedUtc(state.ProcessId) is { } started && (started - state.ProcessStartedUtc).Duration() <= SameProcess) continue;

                Queue(state, "Abnormal", true, state.Days.Keys, state.Commands.Select(c => c.Key));
                File.Delete(file);
            }
            catch (JsonException)
            {
                Delete(file);
            }
            catch (Exception exception) when (Safe(exception))
            {
            }
        }
    }

    private void Queue(SessionState state, string end, bool session, IEnumerable<string> days, IEnumerable<string> commands)
    {
        var id = state.Id.ToString();
        if (session)
            outbox.Put("session_" + id, new JsonObject
            {
                ["type"] = "session", ["id"] = id, ["machine"] = state.Machine, ["windowsUser"] = state.WindowsUser, ["revitYear"] = state.RevitYear,
                ["revitBuild"] = state.RevitBuild, ["toadTools"] = state.ToadTools, ["startedAt"] = Iso(state.StartedAt), ["seenAt"] = Iso(state.SeenAt),
                ["activeSeconds"] = state.ActiveSeconds, ["end"] = end, ["peakMemoryMb"] = state.PeakMemoryMb
            });

        foreach (var day in days.ToList())
        {
            if (!state.Days.TryGetValue(day, out var seconds)) continue;
            outbox.Put($"day_{id}_{day}", new JsonObject
            {
                ["type"] = "sessionDay", ["sessionId"] = id, ["machine"] = state.Machine, ["day"] = day, ["activeSeconds"] = seconds
            });
        }

        var keys = commands.ToHashSet();
        foreach (var count in state.Commands.Where(c => keys.Contains(c.Key)))
            outbox.Put($"cmd_{id}_{Hash(count.Key)}", new JsonObject
            {
                ["type"] = "commandDay", ["sessionId"] = id, ["machine"] = state.Machine, ["day"] = count.Day, ["commandId"] = count.CommandId,
                ["name"] = count.Name, ["source"] = count.Source, ["tab"] = count.Tab, ["count"] = count.Count, ["failed"] = count.Failed,
                ["cancelled"] = count.Cancelled, ["durationMs"] = count.DurationMs
            });
    }

    private void SaveState(SessionState state)
    {
        try
        {
            Directory.CreateDirectory(stateFolder);
            var path = StatePath(state.Id);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, Json), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        catch (Exception exception) when (Safe(exception))
        {
        }
    }

    private string StatePath(Guid id) => Path.Combine(stateFolder, id + ".json");

    private string Day(DateTime utc) => toLocal(utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture);

    /// <summary>Короткое устойчивое имя для ключа очереди: id команды бывает длинным и с запрещёнными в имени файла знаками.</summary>
    private static string Hash(string text)
    {
        var hash = 14695981039346656037UL;
        foreach (var c in text)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }

        return hash.ToString("x16");
    }

    private static void Delete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception exception) when (Safe(exception))
        {
        }
    }

    /// <summary>Сбой сборщика не должен дойти до Revit; не глотаются только сбои самой среды.</summary>
    private static bool Safe(Exception exception) => exception is not (OutOfMemoryException or StackOverflowException);
}
