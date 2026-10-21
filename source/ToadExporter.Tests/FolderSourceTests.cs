using Shouldly;
using ToadExporter.Core;
using ToadTools.UI.RevitServer;

namespace ToadExporter.Tests;

public class FolderSourceTests
{
    [Test]
    public async Task SkipsBackupFoldersAndFiles()
    {
        using var dir = new TempDir();
        dir.File("A.rvt");
        dir.File("A.0001.rvt");
        dir.File("notes.txt");
        dir.File(@"A_backup\x.rvt");

        var entries = await new FolderSource(dir.Path).GetChildrenAsync(dir.Path, CancellationToken.None);

        entries.ShouldBe([new SourceEntry("A.rvt", dir.Path, Path.Combine(dir.Path, "A.rvt"), false)]);
    }

    [Test]
    public async Task HidesFoldersWithoutModels()
    {
        using var dir = new TempDir();
        dir.File(@"Empty\readme.txt");
        dir.File(@"Deep\Sub\B.rvt");

        var entries = await new FolderSource(dir.Path).GetChildrenAsync(dir.Path, CancellationToken.None);

        entries.ShouldBe([new SourceEntry("Deep", Path.Combine(dir.Path, "Deep"), "", true)]);
    }

    [Test]
    public async Task FoldersFirstThenModelsByName()
    {
        using var dir = new TempDir();
        dir.File("b.rvt");
        dir.File("A.rvt");
        dir.File(@"Z\c.rvt");

        var entries = await new FolderSource(dir.Path).GetChildrenAsync(dir.Path, CancellationToken.None);

        entries.Select(e => e.Name).ShouldBe(["Z", "A.rvt", "b.rvt"]);
    }

    [Test]
    public void RootPath_IsFolder() => new FolderSource(@"D:\Models").RootPath.ShouldBe(@"D:\Models");

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateDirectory(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "te-" + Guid.NewGuid().ToString("N"))).FullName;

        public void File(string relative)
        {
            var full = System.IO.Path.Combine(Path, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            System.IO.File.WriteAllText(full, "");
        }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
