using Shouldly;
using ToadTools.UI.Services;

namespace ToadTools.Tests;

public class PaneLayoutTests
{
    [Test]
    public async Task Get_NothingSaved_Null()
    {
        using var temp = new TempFolder();

        new PaneLayout(temp.File("layout.json")).Get("Collisions").ShouldBeNull();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Set_ReadByNextInstance()
    {
        using var temp = new TempFolder();
        new PaneLayout(temp.File("Panes\\layout.json")).Set("Collisions", 0.3);

        var again = new PaneLayout(temp.File("Panes\\layout.json"));

        again.Get("Collisions").ShouldBe(0.3);
        again.Get("Families").ShouldBeNull();
        await Task.CompletedTask;
    }

    [Test]
    [Arguments(0.0, 0.1)]
    [Arguments(-5.0, 0.1)]
    [Arguments(1.0, 0.9)]
    [Arguments(0.5, 0.5)]
    public async Task Set_OutOfRange_Clamped(double share, double expected)
    {
        using var temp = new TempFolder();
        var layout = new PaneLayout(temp.File("layout.json"));

        layout.Set("Families", share);

        new PaneLayout(temp.File("layout.json")).Get("Families").ShouldBe(expected);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Get_HandEditedOutOfRange_Clamped()
    {
        using var temp = new TempFolder();
        await File.WriteAllTextAsync(temp.File("layout.json"), "{ \"Collisions\": 7 }");

        new PaneLayout(temp.File("layout.json")).Get("Collisions").ShouldBe(PaneLayout.MaxShare);
    }

    [Test]
    public async Task Forget_BackToDefault()
    {
        using var temp = new TempFolder();
        new PaneLayout(temp.File("layout.json")).Set("Collisions", 0.3);

        new PaneLayout(temp.File("layout.json")).Forget("Collisions");

        new PaneLayout(temp.File("layout.json")).Get("Collisions").ShouldBeNull();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Load_Corrupted_Defaults()
    {
        using var temp = new TempFolder();
        await File.WriteAllTextAsync(temp.File("layout.json"), "{ не json");

        var layout = new PaneLayout(temp.File("layout.json"));

        layout.Get("Collisions").ShouldBeNull();
        Should.NotThrow(() => layout.Set("Collisions", 0.4));
        new PaneLayout(temp.File("layout.json")).Get("Collisions").ShouldBe(0.4);
    }

    [Test]
    public async Task Save_FolderLocked_NoThrow()
    {
        using var temp = new TempFolder();
        await File.WriteAllTextAsync(temp.File("Panes"), "x");

        Should.NotThrow(() => new PaneLayout(temp.File("Panes\\layout.json")).Set("Collisions", 0.4));
    }
}

public class PaneLayoutShareTests
{
    [Test]
    [Arguments(300.0, 100.0, 0.25)]
    [Arguments(0.0, 50.0, 1.0)]
    public async Task ShareOf_BottomPart(double top, double bottom, double expected)
    {
        PaneLayout.ShareOf(top, bottom).ShouldBe(expected);
        await Task.CompletedTask;
    }

    [Test]
    public async Task ShareOf_BothEmpty_NaN()
    {
        double.IsNaN(PaneLayout.ShareOf(0, 0)).ShouldBeTrue();
        await Task.CompletedTask;
    }
}
