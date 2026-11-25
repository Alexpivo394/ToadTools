using System.IO.Compression;
using System.Text;
using Shouldly;
using ToadTools.Connection;

namespace ToadTools.Tests;

public class DiagnosticsArchiveTests
{
    private static Dictionary<string, string> Read(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        return archive.Entries.ToDictionary(entry => entry.FullName, entry =>
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            return reader.ReadToEnd();
        });
    }

    [Test]
    public async Task Archive_ExcludesConnectionFiles()
    {
        using var temp = new TempFolder();
        var appData = Directory.CreateDirectory(temp.File("AppData")).FullName;
        var logs = Directory.CreateDirectory(temp.File("Logs")).FullName;
        var connectionFile = Path.Combine(appData, ConnectionStore.FileName);
        new ConnectionStore(connectionFile).Save(new ToadFlowConnection("http://toadflow", "ivanov", "Иванов", "Designer", Guid.NewGuid(), "tf_secret"));
        var protectedToken = File.ReadAllText(connectionFile).Split('"').First(part => part.Length > 40);
        File.WriteAllText(Path.Combine(appData, "toadflow-old.json"), """{ "tokenProtected": "AQAAANCMnd8BFdERjHoAwE" }""");
        File.WriteAllText(Path.Combine(appData, "theme.json"), """{ "theme": "dark" }""");
        var zip = temp.File("diag.zip");

        DiagnosticsArchive.Create(appData, logs, "ToadTools 4.7.1", zip);

        var entries = Read(zip);
        entries.Keys.ShouldContain("Settings/theme.json");
        entries.Keys.ShouldNotContain(name => name.Contains("toadflow"));
        var all = string.Concat(entries.Values);
        all.ShouldNotContain(protectedToken);
        all.ShouldNotContain("tokenProtected");
        all.ShouldNotContain("tf_secret");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Archive_IncludesLogsAndVersions()
    {
        using var temp = new TempFolder();
        var appData = Directory.CreateDirectory(temp.File("AppData")).FullName;
        var logs = Directory.CreateDirectory(temp.File("Logs")).FullName;
        File.WriteAllText(Path.Combine(logs, "ToadTools-2026-11-25.log"), "строка журнала");
        File.WriteAllText(Path.Combine(logs, "Revit-errors.log"), "сбой");
        var zip = temp.File("diag.zip");
        File.WriteAllText(zip, "старый архив");

        // журнал открыт на запись, как у фонового писателя
        using (new FileStream(Path.Combine(logs, "ToadTools-2026-11-25.log"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            DiagnosticsArchive.Create(appData, logs, "ToadTools 4.7.1\nRevit 2024", zip);

        var entries = Read(zip);
        entries["Logs/ToadTools-2026-11-25.log"].ShouldBe("строка журнала");
        entries["Logs/Revit-errors.log"].ShouldBe("сбой");
        entries["versions.txt"].ShouldBe("ToadTools 4.7.1\nRevit 2024");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Archive_MissingFolders_OnlyVersions()
    {
        using var temp = new TempFolder();
        var zip = temp.File("diag.zip");

        DiagnosticsArchive.Create(temp.File("нет"), temp.File("нет-журналов"), "v", zip);

        Read(zip).Keys.ShouldBe(["versions.txt"]);
        await Task.CompletedTask;
    }
}
