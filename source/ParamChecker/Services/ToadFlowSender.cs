using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Alexpivo394.Revit.ParamCheck.Core;
using Alexpivo394.Revit.ParamCheck.Profiles;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace ParamChecker.Services;

/// <summary>Чем закончилась отправка очереди: сколько ушло, сколько осталось и почему.</summary>
public sealed record SendResult(int Sent, int Pending, string? Error);

/// <summary>
///     Итоги проверенных моделей — в аналитику ToadFlow. Каждый итог сначала ложится файлом в очередь
///     (%AppData%\ToadTools\ParamChecker\outbox), потом очередь отправляется: если сети нет или Revit упадёт, итоги уйдут
///     при следующей проверке. У каждого итога свой id, поэтому повторная отправка не задвоит проверку на сервере.
/// </summary>
public sealed class ToadFlowSender(CheckerSettings settings)
{
    private static readonly string Outbox =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToadTools", "ParamChecker", "outbox");

    private static readonly JsonSerializerSettings Json = new() { ContractResolver = new CamelCasePropertyNamesContractResolver() };

    private static readonly HttpClient Http = CreateClient();

    public bool Enabled => settings.ToadFlowEnabled && settings.ToadFlowUrl.Trim().Length > 0 && settings.ToadFlowToken.Length > 0;

    /// <summary>Сколько итогов ждут отправки.</summary>
    public static int PendingCount() => Directory.Exists(Outbox) ? Directory.GetFiles(Outbox, "*.json").Length : 0;

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
    ///     итог, который сервер отверг как неверный, откладывается в outbox\rejected, чтобы не мешать остальным.
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
                using var request = Request(HttpMethod.Post, "/api/loi/checks");
                request.Content = new StringContent(File.ReadAllText(file), Encoding.UTF8, "application/json");
                using var response = Http.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode)
                {
                    File.Delete(file);
                    sent++;
                    continue;
                }

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    error = "ToadFlow не принял токен — создайте новый в «Настройки → Токены доступа» и укажите его в настройках ParamChecker";
                    break;
                }

                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    var rejected = Directory.CreateDirectory(Path.Combine(Outbox, "rejected")).FullName;
                    File.Move(file, Path.Combine(rejected, Path.GetFileName(file)));
                    error = $"ToadFlow отверг итог: {ErrorText(response)}";
                    continue;
                }

                error = $"ToadFlow ответил {(int)response.StatusCode}";
                break;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
            {
                error = $"нет связи с ToadFlow: {Root(exception).Message}";
                break;
            }
        }

        return new SendResult(sent, PendingCount(), error);
    }

    /// <summary>Проверка адреса и токена из настроек: кто вошёл или почему не получилось.</summary>
    public static (bool Ok, string Message) Test(string url, string token)
    {
        if (url.Trim().Length == 0 || token.Trim().Length == 0) return (false, "Укажите адрес ToadFlow и токен.");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Address(url, "/api/auth/me"));
            request.Headers.Add("X-ToadFlow-Token", token.Trim());
            using var response = Http.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return (false, "Адрес верный, но токен не принят.");
            if (!response.IsSuccessStatusCode) return (false, $"ToadFlow ответил {(int)response.StatusCode} — проверьте адрес.");

            var user = JObject.Parse(response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult());
            return (true, $"Подключено: {(string?)user["displayName"]}. Ручные проверки будут попадать в аналитику ToadFlow.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException or JsonException)
        {
            return (false, $"Нет связи с ToadFlow: {Root(exception).Message}");
        }
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, Address(settings.ToadFlowUrl, path));
        request.Headers.Add("X-ToadFlow-Token", settings.ToadFlowToken);
        return request;
    }

    private static Uri Address(string url, string path)
    {
        var root = url.Trim().TrimEnd('/');
        if (!root.Contains("://")) root = "http://" + root;
        return new Uri(root + path);
    }

    private static string ErrorText(HttpResponseMessage response)
    {
        try
        {
            return (string?)JObject.Parse(response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult())["error"] ?? "неверные данные";
        }
        catch (JsonException)
        {
            return "неверные данные";
        }
    }

    private static Exception Root(Exception exception) => exception.InnerException is { } inner ? Root(inner) : exception;

    private static HttpClient CreateClient()
    {
#if NETFRAMEWORK
        // .NET Framework по умолчанию может не предложить TLS 1.2 — без него не подключиться к серверу за HTTPS.
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
#endif
        return new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }
}
