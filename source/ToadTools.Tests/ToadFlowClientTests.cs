using System.Net;
using Newtonsoft.Json.Linq;
using Shouldly;
using ToadTools.Connection;

namespace ToadTools.Tests;

public class ToadFlowClientTests
{
    private const string LoginJson = """
        { "token": "jwt-123", "user": { "id": "7b1d3c55-3f0e-4c4a-9a52-2f1f1a0c0001", "login": "ivanov", "displayName": "Иванов И. И.",
          "role": "Designer", "disciplines": ["ОВК"] } }
        """;

    private const string TokenJson = """
        { "view": { "id": "7b1d3c55-3f0e-4c4a-9a52-2f1f1a0c0002", "name": "ToadTools — PC", "prefix": "tf_abcdef" }, "token": "tf_abcdef-full" }
        """;

    private static ToadFlowConnection Connected(string url = "http://toadflow:8080") =>
        new(url, "ivanov", "Иванов И. И.", "Designer", Guid.NewGuid(), "tf_abc");

    private static (ToadFlowClient Client, ConnectionStore Store, FakeHttp Http, TempFolder Temp) Create(
        Func<HttpRequestMessage, string, HttpResponseMessage> respond, ToadFlowConnection? connection = null)
    {
        var temp = new TempFolder();
        var store = new ConnectionStore(temp.File("toadflow.json"));
        if (connection is not null) store.Save(connection);
        var http = new FakeHttp(respond);
        return (new ToadFlowClient(store, http), store, http, temp);
    }

    [Test]
    public async Task SignIn_LoginThenCreateToken_SavesConnection()
    {
        var (client, store, http, temp) = Create((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/auth/login" => FakeHttp.Ok(LoginJson),
            "/api/auth/tokens" => FakeHttp.Ok(TokenJson),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var _ = temp;

        var connection = await client.SignInAsync("http://toadflow:8080/", "ivanov", "secret-password", CancellationToken.None);

        http.Requests.Count.ShouldBe(2);
        var (login, loginBody) = http.Requests[0];
        (login.Method, login.RequestUri!.ToString()).ShouldBe((HttpMethod.Post, "http://toadflow:8080/api/auth/login"));
        var sent = JObject.Parse(loginBody);
        ((string?)sent["login"], (string?)sent["password"]).ShouldBe(("ivanov", "secret-password"));

        var (token, tokenBody) = http.Requests[1];
        (token.Method, token.RequestUri!.ToString()).ShouldBe((HttpMethod.Post, "http://toadflow:8080/api/auth/tokens"));
        token.Headers.Authorization!.ToString().ShouldBe("Bearer jwt-123");
        ((string?)JObject.Parse(tokenBody)["name"]).ShouldBe($"ToadTools — {Environment.MachineName}");

        connection.ShouldBe(new ToadFlowConnection("http://toadflow:8080", "ivanov", "Иванов И. И.", "Designer",
            Guid.Parse("7b1d3c55-3f0e-4c4a-9a52-2f1f1a0c0002"), "tf_abcdef-full"));
        store.Load().ShouldBe(connection);
        var file = await File.ReadAllTextAsync(temp.File("toadflow.json"));
        file.ShouldNotContain("secret-password");
        file.ShouldNotContain("jwt-123");
    }

    [Test]
    public async Task SignIn_WrongPassword_Message()
    {
        var (client, store, _, temp) = Create((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var _ = temp;

        var error = await Should.ThrowAsync<SignInException>(() => client.SignInAsync("http://toadflow", "ivanov", "bad", CancellationToken.None));

        error.Message.ShouldBe("Неверный логин или пароль");
        store.Load().ShouldBeNull();
    }

    [Test]
    public async Task SignIn_Pending_ServerText()
    {
        var (client, _, _, temp) = Create((_, _) => FakeHttp.Json(HttpStatusCode.Forbidden, """{ "error": "Заявка ждёт подтверждения координатора" }"""));
        using var _ = temp;

        var error = await Should.ThrowAsync<SignInException>(() => client.SignInAsync("http://toadflow", "ivanov", "p", CancellationToken.None));

        error.Message.ShouldBe("Заявка ждёт подтверждения координатора");
    }

    [Test]
    public async Task SignIn_NotToadFlow_Message()
    {
        foreach (var response in new Func<HttpResponseMessage>[]
                 {
                     () => new HttpResponseMessage(HttpStatusCode.NotFound),
                     () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>router</html>") }
                 })
        {
            var (client, _, _, temp) = Create((_, _) => response());
            using var _ = temp;

            var error = await Should.ThrowAsync<SignInException>(() => client.SignInAsync("http://router", "ivanov", "p", CancellationToken.None));

            error.Message.ShouldBe("По этому адресу нет ToadFlow");
        }
    }

    [Test]
    public async Task SignIn_NoConnection_Message()
    {
        var (client, _, _, temp) = Create((_, _) => throw new HttpRequestException("Этот хост неизвестен"));
        using var _ = temp;

        var error = await Should.ThrowAsync<SignInException>(() => client.SignInAsync("http://nowhere", "ivanov", "p", CancellationToken.None));

        error.Message.ShouldBe("Нет связи с ToadFlow: Этот хост неизвестен");
    }

    [Test]
    public async Task SignIn_AddsHttpScheme()
    {
        var (client, _, http, temp) = Create((request, _) => request.RequestUri!.AbsolutePath == "/api/auth/login" ? FakeHttp.Ok(LoginJson) : FakeHttp.Ok(TokenJson));
        using var _ = temp;

        var connection = await client.SignInAsync("  toadflow:8080/  ", "ivanov", "p", CancellationToken.None);

        http.Requests[0].Request.RequestUri!.ToString().ShouldBe("http://toadflow:8080/api/auth/login");
        connection.Url.ShouldBe("http://toadflow:8080");
    }

    [Test]
    public async Task Client_SendsTokenHeader()
    {
        var (client, _, http, temp) = Create((_, _) => FakeHttp.Ok("""{ "value": 5 }"""), Connected());
        using var _ = temp;

        var result = await client.GetAsync<JObject>("/api/clash/my/summary", CancellationToken.None);

        ((int)result["value"]!).ShouldBe(5);
        var request = http.Requests.ShouldHaveSingleItem().Request;
        request.RequestUri!.ToString().ShouldBe("http://toadflow:8080/api/clash/my/summary");
        request.Headers.GetValues("X-ToadFlow-Token").ShouldBe(["tf_abc"]);
        request.Headers.Authorization.ShouldBeNull();
    }

    [Test]
    public async Task Client_401_RaisesAuthRequired_AndKeepsConnection()
    {
        var connection = Connected();
        var (client, store, _, temp) = Create((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized), connection);
        using var _ = temp;

        await Should.ThrowAsync<ToadFlowAuthRequiredException>(() => client.GetAsync<JObject>("/api/auth/me", CancellationToken.None));
        await Should.ThrowAsync<ToadFlowAuthRequiredException>(() => client.PostAsync<JObject>("/api/loi/checks", new { }, CancellationToken.None));

        store.Load().ShouldBe(connection);
    }

    [Test]
    public async Task Client_403WithText_RaisesRequestError()
    {
        var (client, _, _, temp) = Create((_, _) => FakeHttp.Json(HttpStatusCode.Forbidden, """{ "error": "Коллизия не вашего раздела" }"""), Connected());
        using var _ = temp;

        var error = await Should.ThrowAsync<ToadFlowRequestException>(() => client.PostAsync<JObject>("/api/clash/collisions/x/take", null, CancellationToken.None));

        (error.Status, error.Message).ShouldBe((403, "Коллизия не вашего раздела"));
    }

    [Test]
    public async Task Client_403WithoutText_RaisesNotEnoughRights()
    {
        var connection = Connected();
        var (client, store, _, temp) = Create((_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden), connection);
        using var _ = temp;

        var error = await Should.ThrowAsync<ToadFlowRequestException>(() => client.PostAsync<JObject>("/api/loi/checks", new { }, CancellationToken.None));

        error.Status.ShouldBe(403);
        error.Message.ShouldStartWith("Недостаточно прав");
        store.Load().ShouldBe(connection);
    }

    [Test]
    public async Task Client_NotConnected_RaisesAuthRequired()
    {
        var (client, _, http, temp) = Create((_, _) => FakeHttp.Ok("{}"));
        using var _ = temp;

        await Should.ThrowAsync<ToadFlowAuthRequiredException>(() => client.GetAsync<JObject>("/api/auth/me", CancellationToken.None));
        http.Requests.ShouldBeEmpty();
        client.WebUrl("clash").ShouldBeNull();
    }

    [Test]
    public async Task Client_Timeout_RaisesUnavailable()
    {
        var (client, _, _, temp) = Create((_, _) => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"),
            Connected());
        using var _ = temp;

        var error = await Should.ThrowAsync<ToadFlowUnavailableException>(() => client.GetBytesAsync("/api/x", CancellationToken.None));

        error.Message.ShouldStartWith("Нет связи с ToadFlow");
    }

    [Test]
    public async Task Client_Post_SendsCamelCaseJson_ReadsEmptyAsNull()
    {
        var (client, _, http, temp) = Create((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent), Connected());
        using var _ = temp;

        var result = await client.PostAsync<JObject>("/api/clash/collisions/1/status", new { Status = "Fake", Note = "Гильза" }, CancellationToken.None);

        result.ShouldBeNull();
        var body = JObject.Parse(http.Requests.Single().Body);
        ((string?)body["status"], (string?)body["note"]).ShouldBe(("Fake", "Гильза"));
    }

    [Test]
    public void Client_WebUrl_JoinsAddress()
    {
        var (client, _, _, temp) = Create((_, _) => FakeHttp.Ok("{}"), Connected("http://toadflow:8080"));
        using var _ = temp;

        client.WebUrl("/clash/checks/1?collision=2").ShouldBe("http://toadflow:8080/clash/checks/1?collision=2");
    }

    [Test]
    public async Task Me_ReadsUser()
    {
        var (client, _, _, temp) = Create((_, _) => FakeHttp.Ok("""
            { "id": "7b1d3c55-3f0e-4c4a-9a52-2f1f1a0c0001", "login": "ivanov", "displayName": "Иванов И. И.", "role": "Operator", "disciplines": [] }
            """), Connected());
        using var _ = temp;

        var me = await client.MeAsync(CancellationToken.None);

        (me.Login, me.DisplayName, me.Role, me.Disciplines.Length).ShouldBe(("ivanov", "Иванов И. И.", "Operator", 0));
    }

    /// <summary>Перенос из ParamChecker: кто владелец старого токена — без сохранённого подключения.</summary>
    [Test]
    public async Task MeWith_TokenFromOldSettings_UserOrNull()
    {
        var (client, store, http, temp) = Create((request, _) => request.Headers.GetValues("X-ToadFlow-Token").Single() == "tf_old"
            ? FakeHttp.Ok("""{ "id": "7b1d3c55-3f0e-4c4a-9a52-2f1f1a0c0001", "login": "ivanov", "displayName": "Иванов", "role": "Designer", "disciplines": [] }""")
            : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var _ = temp;

        (await client.MeWithAsync("toadflow:8080", "tf_old", CancellationToken.None))!.Login.ShouldBe("ivanov");
        (await client.MeWithAsync("toadflow:8080", "tf_bad", CancellationToken.None)).ShouldBeNull();

        http.Requests[0].Request.RequestUri!.ToString().ShouldBe("http://toadflow:8080/api/auth/me");
        store.Load().ShouldBeNull();
    }

    [Test]
    public async Task SignOut_RevokesCurrentAndClears()
    {
        var (client, store, http, temp) = Create((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent), Connected());
        using var _ = temp;

        (await client.SignOutAsync(CancellationToken.None)).ShouldBeTrue();

        var request = http.Requests.ShouldHaveSingleItem().Request;
        (request.Method, request.RequestUri!.ToString()).ShouldBe((HttpMethod.Delete, "http://toadflow:8080/api/auth/tokens/current"));
        request.Headers.GetValues("X-ToadFlow-Token").ShouldBe(["tf_abc"]);
        store.Load().ShouldBeNull();
    }

    [Test]
    public async Task SignOut_ServerDown_ClearsLocally_ReturnsFalse()
    {
        var (client, store, _, temp) = Create((_, _) => throw new HttpRequestException("Сервер не отвечает"), Connected());
        using var _ = temp;

        (await client.SignOutAsync(CancellationToken.None)).ShouldBeFalse();

        store.Load().ShouldBeNull();
    }

    [Test]
    public async Task Client_NotJsonAnswer_RaisesRequestError()
    {
        var (client, _, _, temp) = Create((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>прокси</html>") }, Connected());
        using var _ = temp;

        (await Should.ThrowAsync<ToadFlowRequestException>(() => client.MeAsync(CancellationToken.None))).Message.ShouldBe("ToadFlow прислал непонятный ответ");
        (await Should.ThrowAsync<ToadFlowRequestException>(() => client.PostAsync<JToken>("/api/x", null, CancellationToken.None))).Message
            .ShouldBe("ToadFlow прислал непонятный ответ");
    }

    private enum Shade
    {
        Light,
        Dark,
        Unknown
    }

    private sealed record Painted(Shade Shade, Shade? Maybe);

    [Test]
    public void Json_UnknownEnumValue_ToUnknown()
    {
        var painted = Newtonsoft.Json.JsonConvert.DeserializeObject<Painted>("""{ "shade": "Ultraviolet", "maybe": "Infrared" }""", ToadFlowClient.Json)!;

        (painted.Shade, painted.Maybe).ShouldBe((Shade.Unknown, Shade.Unknown));
        Newtonsoft.Json.JsonConvert.DeserializeObject<Painted>("""{ "shade": "Dark", "maybe": null }""", ToadFlowClient.Json)
            .ShouldBe(new Painted(Shade.Dark, null));
    }

    [Test]
    public async Task SignIn_CannotSave_Message()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.File("blocker"), "файл вместо папки");
        var store = new ConnectionStore(Path.Combine(temp.File("blocker"), "toadflow.json"));
        var client = new ToadFlowClient(store,
            new FakeHttp((request, _) => FakeHttp.Ok(request.RequestUri!.AbsolutePath == "/api/auth/login" ? LoginJson : TokenJson)));

        var exception = await Should.ThrowAsync<SignInException>(() => client.SignInAsync("http://toadflow", "ivanov", "p", CancellationToken.None));

        exception.Message.ShouldStartWith("Не получилось сохранить подключение");
    }
}
