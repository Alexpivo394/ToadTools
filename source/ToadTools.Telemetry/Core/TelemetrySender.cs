using System.Text.Json.Nodes;
using ToadTools.Connection;

namespace ToadTools.Telemetry;

/// <summary>Чем кончилась отправка очереди.</summary>
public enum SendOutcome
{
    /// <summary>Хотя бы одна пачка ушла, очередь пуста или отправлено всё, что можно за раз.</summary>
    Sent,

    /// <summary>Отправлять нечего.</summary>
    Nothing,

    /// <summary>Нет подключения к ToadFlow: очередь ждёт входа.</summary>
    NotConnected,

    /// <summary>Сервер недоступен, модуль выключен (404) или сервер ответил ошибкой: очередь ждёт.</summary>
    Unavailable,

    /// <summary>Токен не принят (401): очередь ждёт нового входа.</summary>
    AuthRequired,

    /// <summary>Отправку прервали (выход Revit).</summary>
    Stopped
}

/// <summary>
///     Отправляет очередь аналитики пачками по порядку: <c>{ "version": 1, "items": [...] }</c>. Пачка удаляется из очереди только
///     после ответа 200; 400 — пачка откладывается в <c>rejected</c> и отправка идёт дальше; любой другой отказ — очередь ждёт
///     следующего раза. Ничего не показывает человеку и никогда не бросает.
/// </summary>
/// <param name="post">Отправка пачки: <c>ToadFlowClient.Shared.PostAsync&lt;object&gt;("/api/usage/events", body, ct)</c>.</param>
public sealed class TelemetrySender(TelemetryOutbox outbox, Func<bool> connected, Func<JsonObject, CancellationToken, Task> post)
{
    public const int BatchSize = 200;
    public const int FormatVersion = 1;

    /// <summary>Больше пачек за одну отправку не шлём: остальное уйдёт в следующий раз, а отправка не тянется бесконечно.</summary>
    public const int MaxBatches = 100;

    public async Task<SendOutcome> FlushAsync(CancellationToken ct)
    {
        try
        {
            if (!IsConnected()) return SendOutcome.NotConnected;
            var sent = false;
            for (var batch = 0; batch < MaxBatches; batch++)
            {
                if (ct.IsCancellationRequested) return SendOutcome.Stopped;
                var entries = outbox.Take(BatchSize);
                if (entries.Count == 0) break;

                var body = new JsonObject
                {
                    ["version"] = FormatVersion,
                    ["items"] = new JsonArray(entries.Select(e => (JsonNode)e.Item.DeepClone()).ToArray())
                };
                try
                {
                    await post(body, ct).ConfigureAwait(false);
                    outbox.Remove(entries);
                    sent = true;
                }
                catch (ToadFlowRequestException exception) when (exception.Status == 400)
                {
                    outbox.Reject(entries);
                }
            }

            return sent ? SendOutcome.Sent : SendOutcome.Nothing;
        }
        catch (ToadFlowAuthRequiredException)
        {
            return SendOutcome.AuthRequired;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return SendOutcome.Stopped;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
        {
            return SendOutcome.Unavailable;
        }
    }

    private bool IsConnected()
    {
        try
        {
            return connected();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }
}
