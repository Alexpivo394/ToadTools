using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using Autodesk.Revit.UI;
using ToadTools.Connection;
using ToadTools.UI.Services;

namespace ToadTools.Telemetry;

/// <summary>
///     Сборщик аналитики ToadFlow в Revit проектировщика: подписки на события, раз в минуту отметка сессии, раз в 10 минут
///     итоги и отправка. В потоке Revit — только подписки и запуск фоновой задачи; диск, железо и сеть — в фоне. Revit, который
///     запустил агент фермы, не собирается. Сбой — запись в журнал ToadTools, без окон.
/// </summary>
internal static class TelemetryHost
{
    /// <summary>Переменная, которую агент ToadFlow ставит запущенному им Revit (<c>PipeNames.InstanceIdVariable</c> в ToadFlow).</summary>
    private const string FarmVariable = "TOADFLOW_INSTANCE_ID";

    private static readonly TimeSpan TickPeriod = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FirstSend = TimeSpan.FromSeconds(30);
    private const int TicksPerSend = 10;
    private static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StopSendLimit = TimeSpan.FromSeconds(3);

    private static UIControlledApplication? _application;
    private static RevitEvents? _events;
    private static SessionTracker? _tracker;
    private static OperationTimer? _timer;
    private static TelemetryOutbox? _outbox;
    private static TelemetrySender? _sender;
    private static CancellationTokenSource? _stop;

    /// <summary>Сколько миллисекунд занял <see cref="Start" /> в потоке Revit.</summary>
    public static double StartMilliseconds { get; private set; }

    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToadTools", "telemetry");

    public static void Start(UIControlledApplication application)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(FarmVariable))) return;

            var revit = application.ControlledApplication;
            var year = int.TryParse(revit.VersionNumber, out var number) ? number : 0;
            var build = revit.VersionBuild;
            var outbox = new TelemetryOutbox(Path.Combine(Root, "outbox"), () => DateTime.UtcNow);
            var tracker = new SessionTracker(Path.Combine(Root, "sessions"), outbox, () => DateTime.UtcNow,
                utc => TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local), ProcessStartedUtc);
            var timer = new OperationTimer(outbox, () => DateTime.UtcNow, () => tracker.SessionId, Environment.MachineName, year, ActivityProbe.FreeMemoryMb);
            var events = new RevitEvents(timer);
            events.Subscribe(revit);

            _application = application;
            _events = events;
            _outbox = outbox;
            _tracker = tracker;
            _timer = timer;
            _sender = new TelemetrySender(outbox, () => ConnectionStore.Shared.Load() is not null,
                (body, ct) => ToadFlowClient.Shared.PostAsync<JsonObject>("/api/usage/events", body, ct));
            _stop = new CancellationTokenSource();
            var ct = _stop.Token;
            _ = Task.Run(() => RunAsync(year, build, ct), ct);
        }
        catch (Exception exception)
        {
            ErrorReport.Log("Аналитика ToadFlow: запуск", exception);
        }
        finally
        {
            StartMilliseconds = watch.Elapsed.TotalMilliseconds;
        }
    }

    /// <summary>Запуск команды — в счётчик дня (только память).</summary>
    public static void Command(CommandRun run)
    {
        try
        {
            _tracker?.Command(run);
        }
        catch (Exception exception)
        {
            ErrorReport.Log("Аналитика ToadFlow: команда", exception);
        }
    }

    /// <summary>Обычный выход Revit: итоги сессии и одна попытка отправки не дольше 3 секунд.</summary>
    public static void Stop()
    {
        try
        {
            _stop?.Cancel();
            if (_application is not null) _events?.Unsubscribe(_application.ControlledApplication);
            _timer?.Flush();
            _tracker?.Stop();
            if (_sender is { } sender)
            {
                using var limit = new CancellationTokenSource(StopSendLimit);
                Task.Run(() => sender.FlushAsync(limit.Token)).Wait(StopSendLimit);
            }
        }
        catch (Exception exception)
        {
            ErrorReport.Log("Аналитика ToadFlow: завершение", exception);
        }
        finally
        {
            _events = null;
            _tracker = null;
            _timer = null;
            _sender = null;
            _application = null;
        }
    }

    private static async Task RunAsync(int year, string build, CancellationToken ct)
    {
        try
        {
            var toadTools = Version();
            using (var process = Process.GetCurrentProcess())
                _tracker!.Start(new SessionStart(Environment.MachineName, Environment.UserName, year, build, toadTools, process.Id,
                    process.StartTime.ToUniversalTime()));
            _outbox!.Add(MachineInfo.Read(toadTools, () => DateTime.UtcNow));
            _outbox.Trim();

            await Task.Delay(FirstSend, ct).ConfigureAwait(false);
            await SendAsync(ct).ConfigureAwait(false);

            var ticks = 0;
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TickPeriod, ct).ConfigureAwait(false);
                _tracker?.Tick(ActivityProbe.IsActive(IntPtr.Zero, IdleLimit), ActivityProbe.ProcessMemoryMb());
                _timer?.Flush();
                if (++ticks % TicksPerSend == 0) await SendAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Revit закрывается.
        }
        catch (Exception exception)
        {
            ErrorReport.Log("Аналитика ToadFlow: фоновая работа", exception);
        }
    }

    private static async Task SendAsync(CancellationToken ct)
    {
        _timer?.Flush();
        _tracker?.Snapshot();
        _outbox?.Trim();
        if (_sender is { } sender) await sender.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Время старта живого процесса; null — процесса нет или он недоступен.</summary>
    private static DateTime? ProcessStartedUtc(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited ? null : process.StartTime.ToUniversalTime();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Версия ToadTools: три числа версии сборки.</summary>
    private static string Version()
    {
        var version = typeof(TelemetryHost).Assembly.GetName().Version ?? new Version(0, 0, 0);
        return $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }
}
