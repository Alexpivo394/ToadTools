using Collisions.Core;
using Shouldly;

namespace ToadTools.Tests.Collisions;

public class PaneStartupTests
{
    [Test]
    public void Startup_RegisterThrows_LoggedAndToggleSaysWhy()
    {
        using var temp = new TempFolder();
        var log = temp.File(@"Clash\startup.log");
        var startup = new PaneStartup(log);

        var registered = startup.TryRegister(() => throw new InvalidOperationException("ресурс ToadTools.UI не найден"));

        registered.ShouldBeFalse();
        startup.ToggleMessage.ShouldBe("Панель не запустилась: ресурс ToadTools.UI не найден");
        File.ReadAllText(log).ShouldContain("InvalidOperationException");
    }

    [Test]
    public void Startup_RegisterOk_NoMessage()
    {
        using var temp = new TempFolder();
        var startup = new PaneStartup(temp.File("startup.log"));
        var called = false;

        startup.TryRegister(() => called = true).ShouldBeTrue();

        called.ShouldBeTrue();
        startup.ToggleMessage.ShouldBeNull();
    }
}
