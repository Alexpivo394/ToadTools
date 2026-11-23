using System.Windows.Threading;
using ToadTools.Connection;
using ToadTools.Connection.Notifications;

namespace ToadTools;

/// <summary>
///     Раз в 2 минуты спрашивает ToadFlow о новых уведомлениях и показывает всплывашку в Revit. Опрос и сеть — вне потока Revit;
///     окно — через диспетчер Revit, запомненный при запуске (<see cref="UiDispatcher" />). Выключено флажком в окне
///     подключения — молчит.
/// </summary>
public sealed class NotificationWatcher : IDisposable
{
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(20);

    private readonly CancellationTokenSource _stop = new();

    /// <summary>Диспетчер потока интерфейса Revit; задаётся при запуске приложения.</summary>
    public static Dispatcher? UiDispatcher { get; set; }

    /// <summary>Запускает опрос в фоне и сразу возвращается.</summary>
    public void Start()
    {
        var ct = _stop.Token;
        _ = Task.Run(() => RunAsync(ct), ct);
    }

    /// <summary>Останавливает опрос: Revit закрывается.</summary>
    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    private static async Task RunAsync(CancellationToken ct)
    {
        var source = new ToadFlowNotificationSource(ToadFlowClient.Shared, ConnectionStore.Shared);
        var poller = new NotificationPoller(source, model => UiDispatcher?.BeginInvoke(new Action(() => NotificationToast.Show(model, source))));
        try
        {
            await Task.Delay(FirstDelay, ct).ConfigureAwait(false);
            while (!ct.IsCancellationRequested && !poller.Disabled)
            {
                if (NotificationSettings.Enabled) await poller.TickAsync(ct).ConfigureAwait(false);
                await Task.Delay(Period, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Revit закрывается.
        }
        catch (Exception)
        {
            // Уведомления — удобство: сбой опроса не должен задеть Revit; до перезапуска всплывашек не будет.
        }
    }
}
