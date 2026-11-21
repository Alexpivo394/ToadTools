using System.Windows.Threading;
using Microsoft.Extensions.Hosting;
using ToadTools.Connection;
using ToadTools.Connection.Notifications;

namespace ToadTools;

/// <summary>
///     Раз в 2 минуты спрашивает ToadFlow о новых уведомлениях и показывает всплывашку в Revit. Сеть — вне потока Revit; окно — через
///     диспетчер Revit, запомненный при запуске (<see cref="UiDispatcher" />). Выключено флажком в окне подключения — молчит.
/// </summary>
public sealed class NotificationWatcher : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(20);

    /// <summary>Диспетчер потока интерфейса Revit; задаётся при запуске приложения.</summary>
    public static Dispatcher? UiDispatcher { get; set; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var source = new ToadFlowNotificationSource(ToadFlowClient.Shared, ConnectionStore.Shared);
        var poller = new NotificationPoller(source, model => UiDispatcher?.BeginInvoke(new Action(() => NotificationToast.Show(model, source))));
        try
        {
            await Task.Delay(FirstDelay, ct);
            while (!ct.IsCancellationRequested && !poller.Disabled)
            {
                if (NotificationSettings.Enabled) await Task.Run(() => poller.TickAsync(ct), ct);
                await Task.Delay(Period, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Revit закрывается.
        }
    }
}
