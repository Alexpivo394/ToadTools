namespace ToadTools.Telemetry;

/// <summary>Как закончился запуск команды.</summary>
public enum CommandOutcome
{
    Succeeded,
    Failed,
    Cancelled
}

/// <summary>Один запуск команды: ToadTools, штатной Revit или чужого плагина.</summary>
/// <param name="CommandId">Идентификатор команды: у ToadTools — имя класса без «Command», у Revit — id команды ленты.</param>
/// <param name="Name">Подпись для людей.</param>
/// <param name="Source"><c>ToadTools</c>, <c>Revit</c> или <c>Addin</c>.</param>
/// <param name="Tab">Вкладка ленты; null — неизвестна.</param>
public sealed record CommandRun(string CommandId, string Name, string Source, string? Tab, CommandOutcome Outcome, long DurationMs);

/// <summary>Что известно о запуске Revit при старте сборщика.</summary>
/// <param name="ProcessStartedUtc">Время старта процесса Revit: вместе с id процесса отличает живую сессию соседа от аварийной.</param>
public sealed record SessionStart(string Machine, string WindowsUser, int RevitYear, string RevitBuild, string ToadTools, int ProcessId,
    DateTime ProcessStartedUtc);

/// <summary>
///     Файл состояния сессии на диске (<c>sessions/{id}.json</c>): всё, чтобы после аварии восстановить итоговые записи, плюс
///     процесс, которому сессия принадлежит. Переписывается раз в минуту.
/// </summary>
public sealed class SessionState
{
    public Guid Id { get; set; }
    public string Machine { get; set; } = "";
    public string WindowsUser { get; set; } = "";
    public int RevitYear { get; set; }
    public string RevitBuild { get; set; } = "";
    public string ToadTools { get; set; } = "";
    public int ProcessId { get; set; }
    public DateTime ProcessStartedUtc { get; set; }
    public DateTime StartedAt { get; set; }

    /// <summary>Последняя отметка: у аварийной сессии — её конец.</summary>
    public DateTime SeenAt { get; set; }

    public int ActiveSeconds { get; set; }
    public long PeakMemoryMb { get; set; }

    /// <summary>Активные секунды по местным дням машины (<c>yyyy-MM-dd</c>).</summary>
    public Dictionary<string, int> Days { get; set; } = new();

    public List<CommandCount> Commands { get; set; } = [];
}

/// <summary>Запуски одной команды за местный день.</summary>
public sealed class CommandCount
{
    public string Day { get; set; } = "";
    public string Source { get; set; } = "";
    public string CommandId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Tab { get; set; }
    public int Count { get; set; }
    public int Failed { get; set; }
    public int Cancelled { get; set; }
    public long DurationMs { get; set; }

    public string Key => $"{Day}|{Source}|{CommandId}";
}
