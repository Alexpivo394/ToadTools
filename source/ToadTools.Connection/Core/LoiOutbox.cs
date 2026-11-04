using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ToadTools.Connection;

/// <summary>Чем закончилась отправка очереди: сколько ушло, сколько осталось и почему.</summary>
public sealed record SendResult(int Sent, int Pending, string? Error);

/// <summary>
///     Очередь итогов проверок для аналитики LOI: каждый итог — файл в папке очереди, отправка — по порядку. Итог ложится в
///     очередь, если отправка включена, даже без подключения (сети нет, перенос старых настроек ещё не прошёл) — уйдёт после
///     входа. Ни один ответ сервера не удаляет итоги: неверные откладываются в <c>rejected</c>, отказ по правам — в
///     <c>forbidden</c> (очередь общая для пользователя Windows, а вошедший сейчас — не обязательно тот, кто проверял).
/// </summary>
/// <param name="folder">Папка очереди.</param>
/// <param name="enabled">Включена ли отправка итогов в настройках.</param>
/// <param name="connected">Есть ли подключение к ToadFlow.</param>
/// <param name="post">Отправка одного итога на сервер.</param>
public sealed class LoiOutbox(string folder, Func<bool> enabled, Func<bool> connected, Func<JObject, CancellationToken, Task> post)
{
    public const string SignInAgain = "Подключение к ToadFlow устарело — войдите заново (ToadTools → Подключение к ToadFlow)";

    public const string NotEnoughRights =
        "Недостаточно прав: статистику LOI в ToadFlow отправляют только координаторы и администраторы. Итоги отложены в outbox\forbidden";

    public const string NotConnected = "ToadFlow не подключён — итоги ждут в очереди и уйдут после входа";

    /// <summary>Сколько итогов ждут отправки.</summary>
    public int PendingCount() => Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json").Length : 0;

    /// <summary>Кладёт итог в очередь, если отправка включена; подключение не нужно.</summary>
    /// <returns>true — итог в очереди.</returns>
    public bool Enqueue(Guid id, string body)
    {
        if (!enabled()) return false;
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{id:N}.json"), body, new UTF8Encoding(false));
        return true;
    }

    /// <summary>
    ///     Отправляет очередь по порядку, только при подключении. Сеть пропала или сервер недоступен — останавливается, остальное
    ///     уйдёт в следующий раз. Итог, который сервер отверг как неверный, — в <c>rejected</c>, чтобы не мешать остальным.
    ///     Отказ по правам без текста сервера — вся очередь в <c>forbidden</c>; отказ с текстом (другая причина) очередь не трогает.
    /// </summary>
    public SendResult Flush()
    {
        if (!enabled() || !Directory.Exists(folder)) return new SendResult(0, PendingCount(), null);
        if (!connected()) return new SendResult(0, PendingCount(), PendingCount() > 0 ? NotConnected : null);

        var sent = 0;
        string? error = null;
        foreach (var file in Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            try
            {
                var body = JObject.Parse(File.ReadAllText(file));
                post(body, CancellationToken.None).GetAwaiter().GetResult();
                File.Delete(file);
                sent++;
            }
            catch (ToadFlowAuthRequiredException)
            {
                error = SignInAgain;
                break;
            }
            catch (ToadFlowRequestException exception) when (exception.Status == 403 && exception.Message == ToadFlowClient.NotEnoughRights)
            {
                // Роли вошедшего не положено: очередь не уйдёт и позже. Итоги не удаляются — их мог накопить координатор,
                // а 403 без текста отдаёт и прокси.
                MoveAll("forbidden");
                error = NotEnoughRights;
                break;
            }
            catch (ToadFlowRequestException exception) when (exception.Status == 400)
            {
                Move(file, "rejected");
                error = $"ToadFlow отверг итог: {exception.Message}";
            }
            catch (ToadFlowRequestException exception)
            {
                error = exception.Message;
                break;
            }
            catch (ToadFlowUnavailableException exception)
            {
                error = exception.Message;
                break;
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                error = $"итог из очереди не прочитан: {exception.Message}";
                break;
            }
        }

        return new SendResult(sent, PendingCount(), error);
    }

    private void MoveAll(string subfolder)
    {
        foreach (var pending in Directory.GetFiles(folder, "*.json")) Move(pending, subfolder);
    }

    private void Move(string file, string subfolder)
    {
        var target = Directory.CreateDirectory(Path.Combine(folder, subfolder)).FullName;
        var path = Path.Combine(target, Path.GetFileName(file));
        if (File.Exists(path)) File.Delete(path);
        File.Move(file, path);
    }
}
