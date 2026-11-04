using System.IO;
using Alexpivo394.Revit.ParamCheck.Core;
using Alexpivo394.Revit.ParamCheck.Profiles;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using ToadTools.Connection;

namespace ParamChecker.Services;

/// <summary>
///     Итоги проверенных моделей — в аналитику ToadFlow через общее подключение ToadTools. Каждый итог сначала ложится файлом
///     в очередь (%AppData%\ToadTools\ParamChecker\outbox), потом очередь отправляется: если сети нет, подключения ещё нет или
///     Revit упадёт, итоги уйдут при следующей проверке. У каждого итога свой id, поэтому повторная отправка не задвоит проверку
///     на сервере. Правила очереди — <see cref="LoiOutbox" />.
/// </summary>
public sealed class ToadFlowSender
{
    private static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToadTools", "ParamChecker", "outbox");

    private static readonly JsonSerializerSettings Json = new() { ContractResolver = new CamelCasePropertyNamesContractResolver() };

    private readonly LoiOutbox _outbox;

    public ToadFlowSender(CheckerSettings settings)
    {
        _outbox = new LoiOutbox(Folder, () => settings.ToadFlowEnabled, () => ConnectionStore.Shared.Load() is not null,
            (body, ct) => ToadFlowClient.Shared.PostAsync<JToken>("/api/loi/checks", body, ct));
    }

    /// <summary>Сколько итогов ждут отправки.</summary>
    public static int PendingCount() => Directory.Exists(Folder) ? Directory.GetFiles(Folder, "*.json").Length : 0;

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

    /// <summary>
    ///     Кладёт итог проверенной модели в очередь, если отправка включена, — подключение не нужно; непроверенные модели в
    ///     аналитику не идут.
    /// </summary>
    public void Enqueue(ModelOutcome outcome, CheckProfile profile)
    {
        if (outcome.Status != ModelStatus.Checked) return;
        var id = Guid.NewGuid();
        _outbox.Enqueue(id, JsonConvert.SerializeObject(new { id, summary = CheckSummaries.From(outcome, ProfileSerializer.Fingerprint(profile)) }, Json));
    }

    /// <summary>Отправляет очередь, если есть подключение; без него говорит, что итоги ждут.</summary>
    public SendResult Flush() => _outbox.Flush();
}
