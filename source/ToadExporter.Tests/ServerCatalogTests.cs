using System.Collections.Concurrent;
using Shouldly;
using ToadTools.UI.RevitServer;

namespace ToadExporter.Tests;

public class ServerCatalogTests
{
    /// <summary>Сервер из словаря «папка → содержимое»; считает запросы к каждой папке.</summary>
    private sealed class FakeSource(Dictionary<string, (string[] Folders, string[] Models)> tree) : ITreeSource
    {
        public ConcurrentDictionary<string, int> Requests { get; } = new();
        public HashSet<string> Broken { get; } = [];

        public string RootPath => "";

        public Task<IReadOnlyList<SourceEntry>> GetChildrenAsync(string folderPath, CancellationToken ct)
        {
            Requests.AddOrUpdate(folderPath, 1, (_, n) => n + 1);
            if (Broken.Contains(folderPath)) throw new SourceException($"сломана {folderPath}");

            var (folders, models) = tree[folderPath];
            IReadOnlyList<SourceEntry> entries = folders
                .Select(name => new SourceEntry(name, RevitServerPaths.Combine(folderPath, name), "", true))
                .Concat(models.Select(name => new SourceEntry(name, folderPath, RevitServerPaths.ModelPath("rs", folderPath, name), false)))
                .ToList();
            return Task.FromResult(entries);
        }
    }

    private static Dictionary<string, (string[], string[])> Tree() => new()
    {
        [""] = (["ЖК", "Школа"], ["Корень.rvt"]),
        ["ЖК"] = (["К1"], ["ЖК_АР.rvt"]),
        ["ЖК/К1"] = ([], ["К1_ОВ.rvt", "К1_ВК.rvt"]),
        ["Школа"] = ([], ["Школа_ЭОМ.rvt"])
    };

    [Test]
    public async Task Refresh_ReadsWholeServer_ThenServesFromMemory()
    {
        var source = new FakeSource(Tree());
        var catalog = new ServerCatalog(source, null);

        await catalog.RefreshAsync();
        await catalog.GetChildrenAsync("ЖК/К1", CancellationToken.None);

        catalog.IsFresh.ShouldBeTrue();
        catalog.AllModels().Count.ShouldBe(5);
        catalog.TryGetChildren("Школа")!.ShouldHaveSingleItem().Name.ShouldBe("Школа_ЭОМ.rvt");
        source.Requests.Values.ShouldAllBe(count => count == 1);
    }

    [Test]
    public async Task EnsureFresh_DoesNotCrawlTwice()
    {
        var source = new FakeSource(Tree());
        var catalog = new ServerCatalog(source, null);

        await catalog.EnsureFreshAsync();
        await catalog.EnsureFreshAsync();

        source.Requests[""].ShouldBe(1);
    }

    [Test]
    public async Task FailedFolder_KeepsPreviousContents()
    {
        var source = new FakeSource(Tree());
        var catalog = new ServerCatalog(source, null);
        await catalog.RefreshAsync();

        source.Broken.Add("ЖК/К1");
        await catalog.RefreshAsync();

        catalog.TryGetChildren("ЖК/К1")!.Count.ShouldBe(2);
        catalog.LastError.ShouldNotBeNull().ShouldContain("ЖК/К1");
    }

    [Test]
    public async Task DiskCache_ShowsTreeBeforeServerAnswers()
    {
        var file = Path.Combine(Path.GetTempPath(), $"catalog_{Guid.NewGuid():N}.json");
        try
        {
            await new ServerCatalog(new FakeSource(Tree()), file).RefreshAsync();

            var source = new FakeSource(Tree());
            var restored = new ServerCatalog(source, file);

            restored.IsFresh.ShouldBeFalse();
            restored.UpdatedAt.ShouldNotBeNull();
            restored.AllModels().Count.ShouldBe(5);
            restored.TryGetChildren("ЖК/К1")![0].ModelPath.ShouldBe("RSN://rs/ЖК/К1/К1_ОВ.rvt");
            source.Requests.ShouldBeEmpty();
        }
        finally
        {
            File.Delete(file);
        }
    }
}
