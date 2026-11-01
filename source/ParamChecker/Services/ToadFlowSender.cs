using System.IO;
using System.Text;
using Alexpivo394.Revit.ParamCheck.Core;
using Alexpivo394.Revit.ParamCheck.Profiles;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using ToadTools.Connection;

namespace ParamChecker.Services;

/// <summary>Чем закончилась отправка очереди: сколько ушло, сколько осталось и почему.</summary>
public sealed record SendResult(int Sent, int Pending, string? Error);

/// <summary>
///     Итоги проверенных моделей — в аналитику ToadFlow через общее подключение ToadTools. Каждый итог сначала ложится файлом
///     в очередь (%AppData%\ToadTools\ParamChecker\outbox), потом очередь отправляется: если сети нет или Revit упадёт, итоги
///     уйдут при следующей проверке. У каждого итога свой id, поэтому повторная отправка не задвоит проверку на сервере.
/// </summary>
public sealed class ToadFlowSender(CheckerSettings settings)
{
    public const string SignInAgain = "Подключение к ToadFlow устарело — войдите заново (ToadTools → Подключение к ToadFlow)";

    public const string NotEnoughRights = "Недостаточно прав: статистику LOI в ToadFlow отправляют только координаторы и администраторы";

    private static readonly string Outbox =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToadTools", "ParamChecker", "outbox");

    private static readonly JsonSerializerSettings Json = new() { ContractResolver = new CamelCasePropertyNamesContractResolver() };

    public bool Enabled => settings.ToadFlowEnabled && ConnectionStore.Shared.Load() is not null;

    /// <summary>Сколько итогов ждут отправки.</summary>
    public static int PendingCount() => Directory.Exists(Outbox) ? Directory.GetFiles(Outbox, "*.json").Length : 0;

    /// <summary>
    ///     Адрес и токен из настроек прежних версий — один раз в общее подключение. Сервер не ответил — старые поля остаются до
    ///     следующего раза; перенесено или общее подключение уже есть — стираются.
    /// </summary>
    /// <returns>true — настройки изменились, их нужно сохранить.</returns>
    public static bool MigrateLegacy(CheckerSettings settings)
    {
        if (settings.LegacyToadFlowUrl.Length == 0 && settings.LegacyToadFlowTokenProtected.Length == 0) return false;

        var token = settings.LegacyToadFlowToken();
        var moved = ParamCheckerMigration.TryMigrate(ConnectionStore.Shared, settings.LegacyToadFlowUrl, token,
            (url, value) => ToadFlowClient.Shared.MeWithAsync(url, value, CancellationToken.None).GetAwaiter().GetResult());
        if (!moved && token.Length > 0 && settings.LegacyToadFlowUrl.Trim().Length > 0 && ConnectionStore.Shared.Load() is null) return false;

        settings.LegacyToadFlowUrl = "";
        settings.LegacyToadFlowTokenProtected = "";
        return true;
    }

    /// <summary>Кладёт итог проверенной модели в очередь; непроверенные модели в аналитику не идут.</summary>
    public void Enqueue(ModelOutcome outcome, CheckProfile profile)
    {
        if (!Enabled || outcome.Status != ModelStatus.Checked) return;

        Directory.CreateDirectory(Outbox);
        var id = Guid.NewGuid();
        var body = JsonConvert.SerializeObject(new { id, summary = CheckSummaries.From(outcome, ProfileSerializer.Fingerprint(profile)) }, Json);
        File.WriteAllText(Path.Combine(Outbox, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{id:N}.json"), body, new UTF8Encoding(false));
    }

    /// <summary>
    ///     Отправляет очередь по порядку. Сеть пропала или сервер недоступен — останавливается, остальное уйдёт в следующий раз;
    ///     итог, который сервер отверг как неверный, откладывается в outbox\rejected, чтобы не мешать остальным. Отказ по правам
    ///     (проектировщик, наблюдатель) сбрасывает очередь: отправлять статистику им не положено.
    /// </summary>
    public SendResult Flush()
    {
        if (!Enabled || !Directory.Exists(Outbox)) return new SendResult(0, PendingCount(), null);

        var sent = 0;
        string? error = null;
        foreach (var file in Directory.GetFiles(Outbox, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            try
            {
                var body = JObject.Parse(File.ReadAllText(file));
                ToadFlowClient.Shared.PostAsync<JToken>("/api/loi/checks", body, CancellationToken.None).GetAwaiter().GetResult();
                File.Delete(file);
                sent++;
            }
            catch (ToadFlowAuthRequiredException)
            {
                error = SignInAgain;
                break;
            }
            catch (ToadFlowRequestException exception) when (exception.Status == 403)
            {
                // Роли не положено: очередь не уйдёт и позже, поэтому сбрасывается, а не копится.
                foreach (var pending in Directory.GetFiles(Outbox, "*.json")) File.Delete(pending);
                error = NotEnoughRights;
                break;
            }
            catch (ToadFlowRequestException exception) when (exception.Status == 400)
            {
                var rejected = Directory.CreateDirectory(Path.Combine(Outbox, "rejected")).FullName;
                File.Move(file, Path.Combine(rejected, Path.GetFileName(file)));
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
}
