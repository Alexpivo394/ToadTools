using Shouldly;
using ToadTools.UI.Services;

namespace ToadTools.Tests;

[NotInParallel]
public class ToadLogTests
{
    private static string Today(string folder) => Path.Combine(folder, ToadLog.FileFor(DateTime.Now));

    private static TempFolder Isolate()
    {
        var temp = new TempFolder();
        ToadLog.Folder = temp.Path;
        ToadLog.SettingsPath = temp.File("logging.json");
        return temp;
    }

    [Test]
    public async Task TestSession_JournalNotInUserFolder()
    {
        // Тесты, которые сами не подменяют папку (панель коллизий пишет ToadLog.Error), не должны писать в журнал пользователя.
        var user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToadTools");
        var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToadTools");

        TestSessionLog.Folder.ShouldNotStartWith(user, Case.Insensitive);
        TestSessionLog.SettingsPath.ShouldNotStartWith(settings, Case.Insensitive);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Debug_VerboseOff_NotWritten()
    {
        using var temp = Isolate();
        ToadLog.Verbose = false;

        ToadLog.Debug("Area", "hidden");
        ToadLog.Flush();

        File.Exists(Today(temp.Path)).ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Debug_VerboseOn_Written()
    {
        using var temp = Isolate();
        ToadLog.Verbose = true;

        ToadLog.Debug("Area", "shown");
        ToadLog.Flush();

        var text = await File.ReadAllTextAsync(Today(temp.Path));
        text.ShouldContain("[DEBUG] Area: shown");
        File.ReadAllText(temp.File("logging.json")).ShouldContain("true");
    }

    [Test]
    public async Task Info_AlwaysWritten()
    {
        using var temp = Isolate();
        ToadLog.Verbose = false;

        ToadLog.Info("Area", "hello");
        ToadLog.Error("Area", new InvalidOperationException("boom"));
        ToadLog.Flush();

        var text = await File.ReadAllTextAsync(Today(temp.Path));
        text.ShouldContain("[INFO] Area: hello");
        text.ShouldContain("[ERROR] Area: System.InvalidOperationException: boom");
    }

    [Test]
    public async Task Prune_DeletesOlderThan7Days_KeepsOthersAndErrorsLog()
    {
        using var temp = new TempFolder();
        var now = new DateTime(2026, 11, 25, 12, 0, 0);
        foreach (var name in new[] { ToadLog.FileFor(now.AddDays(-8)), ToadLog.FileFor(now.AddDays(-7)), ToadLog.FileFor(now.AddDays(-1)), "Revit-errors.log", "other.txt" })
            await File.WriteAllTextAsync(temp.File(name), "x");

        ToadLog.Prune(temp.Path, now);

        File.Exists(temp.File(ToadLog.FileFor(now.AddDays(-8)))).ShouldBeFalse();
        File.Exists(temp.File(ToadLog.FileFor(now.AddDays(-7)))).ShouldBeTrue();
        File.Exists(temp.File(ToadLog.FileFor(now.AddDays(-1)))).ShouldBeTrue();
        File.Exists(temp.File("Revit-errors.log")).ShouldBeTrue();
        File.Exists(temp.File("other.txt")).ShouldBeTrue();
    }

    [Test]
    public async Task Clear_RemovesLogs_KeepsErrorsLog()
    {
        using var temp = Isolate();
        await File.WriteAllTextAsync(temp.File("Revit-errors.log"), "x");
        ToadLog.Info("Area", "line");
        ToadLog.Flush();

        ToadLog.Clear();

        File.Exists(Today(temp.Path)).ShouldBeFalse();
        File.Exists(temp.File("Revit-errors.log")).ShouldBeTrue();
    }

    [Test]
    public async Task Write_FolderLocked_NoThrow()
    {
        using var temp = new TempFolder();
        // Вместо папки — файл: создать каталог журнала нельзя.
        var blocker = temp.File("Logs");
        await File.WriteAllTextAsync(blocker, "x");
        ToadLog.Folder = blocker;
        ToadLog.SettingsPath = temp.File("logging.json");
        ToadLog.Verbose = true;

        Should.NotThrow(() =>
        {
            ToadLog.Info("Area", "lost");
            ToadLog.Debug("Area", "lost");
            ToadLog.Error("Area", new Exception("x"));
            ToadLog.Flush();
            ToadLog.Clear();
        });
    }
}
