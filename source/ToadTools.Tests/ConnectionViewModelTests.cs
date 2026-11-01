using System.Net;
using Shouldly;
using ToadTools.Connection;
using ToadTools.Connection.ViewModels;

namespace ToadTools.Tests;

public class ConnectionViewModelTests
{
    private const string LoginJson = """
        { "token": "jwt", "user": { "id": "7b1d3c55-3f0e-4c4a-9a52-2f1f1a0c0001", "login": "ivanov", "displayName": "Иванов И. И.", "role": "Designer" } }
        """;

    private const string TokenJson = """{ "view": { "id": "7b1d3c55-3f0e-4c4a-9a52-2f1f1a0c0002" }, "token": "tf_new" }""";

    private static (ConnectionViewModel Model, ConnectionStore Store, TempFolder Temp) Create(Func<HttpRequestMessage, HttpResponseMessage> respond,
        ToadFlowConnection? connection = null)
    {
        var temp = new TempFolder();
        var store = new ConnectionStore(temp.File("toadflow.json"));
        if (connection is not null) store.Save(connection);
        var client = new ToadFlowClient(store, new FakeHttp((request, _) => respond(request)));
        return (new ConnectionViewModel(client, store), store, temp);
    }

    [Test]
    public async Task ViewModel_SignIn_Success_ShowsWho()
    {
        var (model, store, temp) = Create(request => FakeHttp.Ok(request.RequestUri!.AbsolutePath == "/api/auth/login" ? LoginJson : TokenJson));
        using var _ = temp;
        (model.Url, model.Login, model.Password) = ("toadflow:8080", "ivanov", "secret");

        await model.SignInCommand.ExecuteAsync(null);

        model.IsConnected.ShouldBeTrue();
        model.Status.ShouldBe("Вы вошли как Иванов И. И. (Проектировщик) на http://toadflow:8080");
        model.Error.ShouldBeNull();
        model.Password.ShouldBeEmpty();
        store.Load()!.Token.ShouldBe("tf_new");
    }

    [Test]
    public async Task ViewModel_SignIn_Error_ShowsMessage_KeepsPassword()
    {
        var (model, store, temp) = Create(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var _ = temp;
        (model.Url, model.Login, model.Password) = ("http://toadflow", "ivanov", "wrong");

        await model.SignInCommand.ExecuteAsync(null);

        model.IsConnected.ShouldBeFalse();
        model.Error.ShouldBe("Неверный логин или пароль");
        model.Password.ShouldBe("wrong");
        store.Load().ShouldBeNull();
    }

    [Test]
    public async Task ViewModel_SignOut_ServerDown_WarnsTokenRemains()
    {
        var (model, store, temp) = Create(_ => throw new HttpRequestException("нет сети"),
            new ToadFlowConnection("http://toadflow", "ivanov", "Иванов И. И.", "Designer", Guid.NewGuid(), "tf_x"));
        using var _ = temp;
        model.IsConnected.ShouldBeTrue();

        await model.SignOutCommand.ExecuteAsync(null);

        model.IsConnected.ShouldBeFalse();
        model.Status.ShouldBe("Вы вышли. Сервер недоступен — токен останется в списке токенов на сервере");
        store.Load().ShouldBeNull();
        (model.Url, model.Login).ShouldBe(("http://toadflow", "ivanov")); // войти снова — только пароль
    }

    [Test]
    public async Task ViewModel_SignOut_Revoked_SaysSignedOut()
    {
        var (model, _, temp) = Create(_ => new HttpResponseMessage(HttpStatusCode.NoContent),
            new ToadFlowConnection("http://toadflow", "ivanov", "Иванов И. И.", "Designer", Guid.NewGuid(), "tf_x"));
        using var _ = temp;

        await model.SignOutCommand.ExecuteAsync(null);

        model.Status.ShouldBe("Вы вышли");
    }

    [Test]
    public void ViewModel_EmptyFields_SignInDisabled()
    {
        var (model, _, temp) = Create(_ => FakeHttp.Ok("{}"));
        using var _ = temp;

        model.SignInCommand.CanExecute(null).ShouldBeFalse();
        (model.Url, model.Login) = ("http://toadflow", "ivanov");
        model.SignInCommand.CanExecute(null).ShouldBeFalse();
        model.Password = "p";
        model.SignInCommand.CanExecute(null).ShouldBeTrue();
        model.Login = " ";
        model.SignInCommand.CanExecute(null).ShouldBeFalse();
    }

    [Test]
    public async Task ViewModel_Check_TokenRevoked_AsksToSignInAgain()
    {
        var (model, store, temp) = Create(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            new ToadFlowConnection("http://toadflow", "ivanov", "Иванов И. И.", "Designer", Guid.NewGuid(), "tf_x"));
        using var _ = temp;

        await model.CheckAsync();

        model.Error.ShouldBe("Подключение к ToadFlow устарело — войдите заново");
        store.Load().ShouldNotBeNull(); // стирает только «Выйти»
    }
}
