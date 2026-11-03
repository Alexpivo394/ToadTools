using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace ToadTools.Connection;

/// <summary>
///     HTTP-клиент ToadFlow для плагинов: вход (пароль → краткоживущий JWT → личный токен этого компьютера), запросы с
///     заголовком <c>X-ToadFlow-Token</c> и выход. Все ожидания — с <c>ConfigureAwait(false)</c>: ParamChecker вызывает
///     клиента синхронно из потока Revit.
/// </summary>
public sealed class ToadFlowClient
{
    public const string TokenHeader = "X-ToadFlow-Token";

    /// <summary>Отказ без текста от сервера (403): роли пользователя это действие не положено.</summary>
    public const string NotEnoughRights = "Недостаточно прав в ToadFlow для этого действия";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>Одновременных запросов к серверу ToadFlow: миниатюры (до 4) не должны занимать все соединения.</summary>
    private const int ParallelRequests = 8;

    public static readonly JsonSerializerSettings Json = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Converters = { new TolerantEnumConverter() },
        NullValueHandling = NullValueHandling.Include
    };

    private static readonly Lazy<ToadFlowClient> SharedClient = new(() => new ToadFlowClient(ConnectionStore.Shared));

    private readonly ConnectionStore _store;
    private readonly HttpClient _http;

    public ToadFlowClient(ConnectionStore store, HttpMessageHandler? handler = null)
    {
#if NETFRAMEWORK
        // .NET Framework по умолчанию может не предложить TLS 1.2 — без него не подключиться к серверу за HTTPS.
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
#endif
        _store = store;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = Timeout;
    }

    /// <summary>Клиент общего подключения всех плагинов.</summary>
    public static ToadFlowClient Shared => SharedClient.Value;

    public ConnectionStore Store => _store;

    /// <summary>Адрес как ввёл человек → <c>http://host:port</c> без хвостового <c>/</c>.</summary>
    public static string NormalizeUrl(string url)
    {
        var root = url.Trim().TrimEnd('/');
        if (root.Length > 0 && !root.Contains("://")) root = "http://" + root;
        return root;
    }

    /// <summary>Входит паролем и выпускает личный токен «ToadTools — {компьютер}». Пароль и JWT не сохраняются.</summary>
    public async Task<ToadFlowConnection> SignInAsync(string url, string login, string password, CancellationToken ct)
    {
        var root = NormalizeUrl(url);
        if (root.Length == 0) throw new SignInException("Укажите адрес ToadFlow");

        JObject signedIn;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Address(root, "/api/auth/login")) { Content = Body(new { login = login.Trim(), password }) };
            using var response = await SendRawAsync(request, ct).ConfigureAwait(false);
            var text = await ReadAsync(response).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized) throw new SignInException("Неверный логин или пароль");
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new SignInException(ErrorOf(text) ?? "ToadFlow не пустил этого пользователя");
            if (!response.IsSuccessStatusCode || Parse(text) is not { } parsed || parsed["token"] is null || parsed["user"] is not JObject)
                throw new SignInException(response.StatusCode != HttpStatusCode.NotFound && ErrorOf(text) is { } error ? error : "По этому адресу нет ToadFlow");
            signedIn = parsed;
        }
        catch (ToadFlowUnavailableException exception)
        {
            throw new SignInException(exception.Message, exception);
        }
        catch (UriFormatException exception)
        {
            throw new SignInException("Неверный адрес ToadFlow", exception);
        }

        var jwt = (string)signedIn["token"]!;
        var user = (JObject)signedIn["user"]!;
        JObject created;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Address(root, "/api/auth/tokens"))
            {
                Content = Body(new { name = $"ToadTools — {Environment.MachineName}" })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            using var response = await SendRawAsync(request, ct).ConfigureAwait(false);
            var text = await ReadAsync(response).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || Parse(text) is not { } parsed || parsed["token"] is null)
                throw new SignInException(ErrorOf(text) ?? $"ToadFlow не выдал токен (ответ {(int)response.StatusCode})");
            created = parsed;
        }
        catch (ToadFlowUnavailableException exception)
        {
            throw new SignInException(exception.Message, exception);
        }

        var connection = new ToadFlowConnection(root, (string?)user["login"] ?? login.Trim(), (string?)user["displayName"] ?? login.Trim(),
            (string?)user["role"] ?? "", Guid.TryParse((string?)created["view"]?["id"], out var id) ? id : Guid.Empty, (string)created["token"]!);
        try
        {
            _store.Save(connection);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw new SignInException($"Не получилось сохранить подключение: {exception.Message}", exception);
        }

        return connection;
    }

    /// <summary>
    ///     Отзывает токен этого компьютера на сервере и стирает его у себя в любом случае. Токен, перенесённый из настроек ParamChecker
    ///     (<see cref="ToadFlowConnection.TokenId" /> пуст), человек создал сам в веб-интерфейсе и мог использовать ещё где-то: он только
    ///     стирается здесь, на сервере остаётся.
    /// </summary>
    public async Task<SignOutResult> SignOutAsync(CancellationToken ct)
    {
        var connection = _store.Load();
        if (connection is null) return SignOutResult.Revoked;
        if (connection.TokenId == Guid.Empty)
        {
            _store.Clear();
            return SignOutResult.KeptOnServer;
        }

        try
        {
            using var request = Request(connection, HttpMethod.Delete, "/api/auth/tokens/current");
            using var response = await SendRawAsync(request, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Unauthorized ? SignOutResult.Revoked : SignOutResult.ServerDown;
        }
        catch (Exception exception) when (exception is ToadFlowUnavailableException or UriFormatException)
        {
            return SignOutResult.ServerDown;
        }
        finally
        {
            _store.Clear();
        }
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        var text = await SendAsync(HttpMethod.Get, path, null, ct).ConfigureAwait(false);
        return Deserialize<T>(text) ?? throw new ToadFlowRequestException(200, "ToadFlow прислал пустой ответ");
    }

    public async Task<T?> PostAsync<T>(string path, object? body, CancellationToken ct)
    {
        var text = await SendAsync(HttpMethod.Post, path, body, ct).ConfigureAwait(false);
        return text.Trim().Length == 0 ? default : Deserialize<T>(text);
    }

    public async Task<byte[]> GetBytesAsync(string path, CancellationToken ct)
    {
        var connection = _store.Load() ?? throw new ToadFlowAuthRequiredException();
        using var request = Request(connection, HttpMethod.Get, path);
        using var response = await SendRawAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) Fail(response, await ReadAsync(response).ConfigureAwait(false));
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    public Task<UserInfo> MeAsync(CancellationToken ct) => GetAsync<UserInfo>("/api/auth/me", ct);

    /// <summary>Владелец токена не из хранилища (перенос из старых настроек ParamChecker); null — сервер не ответил или не принял токен.</summary>
    public async Task<UserInfo?> MeWithAsync(string url, string token, CancellationToken ct)
    {
        try
        {
            var probe = new ToadFlowConnection(NormalizeUrl(url), "", "", "", Guid.Empty, token.Trim());
            using var request = Request(probe, HttpMethod.Get, "/api/auth/me");
            using var response = await SendRawAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            return JsonConvert.DeserializeObject<UserInfo>(await ReadAsync(response).ConfigureAwait(false), Json);
        }
        catch (Exception exception) when (exception is ToadFlowUnavailableException or UriFormatException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Адрес страницы веб-интерфейса; null — подключения нет.</summary>
    public string? WebUrl(string relative) => _store.Load() is { } connection ? connection.Url + "/" + relative.TrimStart('/') : null;

    private async Task<string> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var connection = _store.Load() ?? throw new ToadFlowAuthRequiredException();
        using var request = Request(connection, method, path);
        if (body is not null) request.Content = Body(body);
        using var response = await SendRawAsync(request, ct).ConfigureAwait(false);
        var text = await ReadAsync(response).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) Fail(response, text);
        return text;
    }

    /// <summary>
    ///     401 — токен не принят, нужно войти заново. 403 — роли пользователя действие не положено: повторный вход не поможет,
    ///     поэтому это отказ в действии, с текстом сервера или «недостаточно прав».
    /// </summary>
    private static void Fail(HttpResponseMessage response, string text)
    {
        var status = (int)response.StatusCode;
        var error = ErrorOf(text);
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new ToadFlowAuthRequiredException();
        if (response.StatusCode == HttpStatusCode.Forbidden && error is null) throw new ToadFlowRequestException(status, NotEnoughRights);
        throw new ToadFlowRequestException(status, error ?? $"ToadFlow ответил {status}");
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, CancellationToken ct)
    {
#if NETFRAMEWORK
        AllowParallelRequests(request.RequestUri);
#endif
        try
        {
            return await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or WebException)
        {
            var reason = exception is TaskCanceledException ? $"сервер не ответил за {Timeout.TotalSeconds:0} с" : Root(exception).Message;
            throw new ToadFlowUnavailableException($"Нет связи с ToadFlow: {reason}", exception);
        }
    }

#if NETFRAMEWORK
    /// <summary>
    ///     .NET Framework в настольном приложении держит к одному серверу только 2 соединения: миниатюры занимали бы оба, а
    ///     карточка и список ждали бы за ними до тайм-аута. Лимит поднимается только для сервера ToadFlow, перед каждым
    ///     запросом: сменился адрес или Revit пересоздал точку подключения после простоя — лимит снова верный.
    /// </summary>
    private static void AllowParallelRequests(Uri? address)
    {
        if (address is null) return;
        var point = ServicePointManager.FindServicePoint(address);
        if (point.ConnectionLimit < ParallelRequests) point.ConnectionLimit = ParallelRequests;
    }

#endif
    private static HttpRequestMessage Request(ToadFlowConnection connection, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, Address(connection.Url, path));
        request.Headers.Add(TokenHeader, connection.Token);
        return request;
    }

    private static Uri Address(string root, string path) => new(root + "/" + path.TrimStart('/'));

    private static StringContent Body(object body) => new(JsonConvert.SerializeObject(body, Json), Encoding.UTF8, "application/json");

    private static async Task<string> ReadAsync(HttpResponseMessage response) =>
        response.Content is null ? "" : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    /// <summary>Ответ, который не разобрать (страница прокси вместо JSON, другая форма), — отказ сервера, а не сбой плагина.</summary>
    private static T? Deserialize<T>(string text)
    {
        try
        {
            return JsonConvert.DeserializeObject<T>(text, Json);
        }
        catch (JsonException exception)
        {
            throw new ToadFlowRequestException(200, "ToadFlow прислал непонятный ответ", exception);
        }
    }

    private static JObject? Parse(string text)
    {
        try
        {
            return text.TrimStart().StartsWith("{", StringComparison.Ordinal) ? JObject.Parse(text) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ErrorOf(string text) => Parse(text)?["error"]?.Type == JTokenType.String ? (string?)Parse(text)!["error"] : null;

    private static Exception Root(Exception exception) => exception.InnerException is { } inner ? Root(inner) : exception;
}
