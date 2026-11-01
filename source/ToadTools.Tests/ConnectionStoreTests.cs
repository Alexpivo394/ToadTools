using Shouldly;
using ToadTools.Connection;

namespace ToadTools.Tests;

public class ConnectionStoreTests
{
    private static readonly ToadFlowConnection Sample =
        new("http://toadflow:8080", "ivanov", "Иванов И. И.", "Designer", Guid.NewGuid(), "tf_secret-token-value");

    [Test]
    public async Task Store_SaveLoad_TokenEncryptedOnDisk()
    {
        using var temp = new TempFolder();
        var store = new ConnectionStore(temp.File("toadflow.json"));

        store.Save(Sample);

        var text = await File.ReadAllTextAsync(temp.File("toadflow.json"));
        text.ShouldNotContain("tf_secret-token-value");
        text.ShouldContain("tokenProtected");
        new ConnectionStore(temp.File("toadflow.json")).Load().ShouldBe(Sample);
    }

    [Test]
    public void Store_Clear_RemovesFile_RaisesChanged()
    {
        using var temp = new TempFolder();
        var store = new ConnectionStore(temp.File("toadflow.json"));
        var changed = 0;
        store.Changed += (_, _) => changed++;

        store.Save(Sample);
        store.Clear();

        File.Exists(temp.File("toadflow.json")).ShouldBeFalse();
        store.Load().ShouldBeNull();
        changed.ShouldBe(2);
    }

    [Test]
    public async Task Store_UnreadableToken_LoadsNothing()
    {
        using var temp = new TempFolder();
        await File.WriteAllTextAsync(temp.File("toadflow.json"), """{ "url": "http://x", "tokenProtected": "bm90LWRwYXBp" }""");

        new ConnectionStore(temp.File("toadflow.json")).Load().ShouldBeNull();
    }

    [Test]
    public async Task Migration_MovesParamCheckerToken_Once()
    {
        using var temp = new TempFolder();
        var store = new ConnectionStore(temp.File("toadflow.json"));
        var calls = 0;
        UserInfo? Me(string url, string token)
        {
            calls++;
            (url, token).ShouldBe(("http://toadflow:8080", "tf_old"));
            return new UserInfo(Guid.NewGuid(), "petrov", "Петров П. П.", "Designer", ["ОВК"]);
        }

        ParamCheckerMigration.TryMigrate(store, "toadflow:8080/", "tf_old", Me).ShouldBeTrue();
        ParamCheckerMigration.TryMigrate(store, "http://other", "tf_other", Me).ShouldBeFalse();

        calls.ShouldBe(1);
        var saved = store.Load().ShouldNotBeNull();
        (saved.Url, saved.Login, saved.DisplayName, saved.Role, saved.Token).ShouldBe(("http://toadflow:8080", "petrov", "Петров П. П.", "Designer", "tf_old"));
        await Task.CompletedTask;
    }

    [Test]
    public void Migration_ServerDown_KeepsTryingLater()
    {
        using var temp = new TempFolder();
        var store = new ConnectionStore(temp.File("toadflow.json"));

        ParamCheckerMigration.TryMigrate(store, "http://toadflow", "tf_old", (_, _) => null).ShouldBeFalse();
        ParamCheckerMigration.TryMigrate(store, "", "", (_, _) => throw new InvalidOperationException()).ShouldBeFalse();

        store.Load().ShouldBeNull();
    }
}
