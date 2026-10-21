using Shouldly;
using ToadExporter.Core;
using ToadExporter.ViewModels.Tree;
using ToadTools.UI.RevitServer;

namespace ToadExporter.Tests;

public class ModelTreeTests
{
    // ЖК/
    //   К1/  LES_K1_AR.rvt, LES_K1_KR.rvt
    //   LES_PARK.rvt
    // Школа.rvt
    private static FakeSource Sample() => new()
    {
        [""] = [Folder("ЖК", "ЖК"), Model("Школа.rvt", "")],
        ["ЖК"] = [Folder("К1", "ЖК/К1"), Model("LES_PARK.rvt", "ЖК")],
        ["ЖК/К1"] = [Model("LES_K1_AR.rvt", "ЖК/К1"), Model("LES_K1_KR.rvt", "ЖК/К1")]
    };

    [Test]
    public async Task Load_ShowsRootEntries()
    {
        var tree = await Loaded(Sample());

        tree.Roots.Select(n => n.Name).ShouldBe(["ЖК", "Школа.rvt"]);
        tree.Roots[0].IsFolder.ShouldBeTrue();
    }

    [Test]
    public async Task Expanding_LoadsChildren()
    {
        var tree = await Loaded(Sample());
        var folder = tree.Roots[0];

        await folder.EnsureLoadedAsync();

        folder.Children.Select(n => n.Name).ShouldBe(["К1", "LES_PARK.rvt"]);
    }

    [Test]
    public async Task CheckingFolder_LoadsAndChecksAllDescendants()
    {
        var tree = await Loaded(Sample());

        await tree.Roots[0].SetRvtAsync(true);

        tree.RvtCount.ShouldBe(3);
        tree.Roots[0].Rvt.ShouldBe(true);
        tree.Roots[0].Children[0].Children.ShouldAllBe(n => n.Rvt == true);
    }

    [Test]
    public async Task PartialChildren_MakeParentIndeterminate()
    {
        var tree = await Loaded(Sample());
        await tree.Roots[0].SetRvtAsync(true);
        var k1 = tree.Roots[0].Children[0];

        await k1.Children[0].SetRvtAsync(false);

        k1.Rvt.ShouldBeNull();
        tree.Roots[0].Rvt.ShouldBeNull();
        tree.RvtCount.ShouldBe(2);
    }

    [Test]
    public async Task AllChildrenChecked_MakeParentChecked()
    {
        var tree = await Loaded(Sample());
        await tree.Roots[0].EnsureLoadedAsync();
        var k1 = tree.Roots[0].Children[0];
        await k1.EnsureLoadedAsync();

        await k1.Children[0].SetNwcAsync(true);
        await k1.Children[1].SetNwcAsync(true);

        k1.Nwc.ShouldBe(true);
        tree.Roots[0].Nwc.ShouldBeNull();
        tree.NwcCount.ShouldBe(2);
    }

    [Test]
    public async Task NwcDisabled_ClearsAndCountsZero()
    {
        var tree = await Loaded(Sample());
        await tree.Roots[1].SetNwcAsync(true);

        tree.NwcEnabled = false;
        await tree.Roots[1].SetNwcAsync(true);

        tree.Roots[1].Nwc.ShouldBe(false);
        tree.NwcCount.ShouldBe(0);
    }

    [Test]
    public async Task SetAll_ChecksEveryModel()
    {
        var tree = await Loaded(Sample());

        await tree.SetAllAsync(true, null);

        tree.RvtCount.ShouldBe(4);
        tree.NwcCount.ShouldBe(0);
    }

    [Test]
    public async Task Search_HidesNonMatchingModels()
    {
        var tree = await Loaded(Sample());
        await tree.SetAllAsync(true, null); // отметка загружает всё дерево

        tree.SearchText = "kr";

        var k1 = tree.Roots[0].Children[0];
        k1.Children.Single(n => n.Name == "LES_K1_KR.rvt").IsVisible.ShouldBeTrue();
        k1.Children.Single(n => n.Name == "LES_K1_AR.rvt").IsVisible.ShouldBeFalse();
        tree.Roots[0].Children.Single(n => n.Name == "LES_PARK.rvt").IsVisible.ShouldBeFalse();
        tree.Roots[1].IsVisible.ShouldBeFalse();
        tree.Roots[0].IsVisible.ShouldBeTrue();
        k1.IsExpanded.ShouldBeTrue();
    }

    [Test]
    public async Task ClearingSearch_ShowsEverything()
    {
        var tree = await Loaded(Sample());
        await tree.SetAllAsync(true, null);
        tree.SearchText = "kr";

        tree.SearchText = "";

        tree.Roots[1].IsVisible.ShouldBeTrue();
        tree.Roots[0].Children[0].Children.ShouldAllBe(n => n.IsVisible);
    }

    [Test]
    public async Task CollectJobs_OnlyChecked()
    {
        var tree = await Loaded(Sample());
        await tree.Roots[0].EnsureLoadedAsync();
        await tree.Roots[0].Children[1].SetRvtAsync(true);
        await tree.Roots[1].SetNwcAsync(true);

        var jobs = tree.CollectJobs(isServer: true);

        jobs.ShouldBe([
            new ModelJob("LES_PARK.rvt", "path:ЖК/LES_PARK.rvt", true, true, false),
            new ModelJob("Школа.rvt", "path:Школа.rvt", true, false, true)
        ]);
    }

    [Test]
    public async Task SourceError_SetsError()
    {
        var tree = new ModelTreeViewModel();

        await tree.LoadAsync(new FakeSource());

        tree.Error.ShouldNotBeNull();
        tree.Roots.ShouldBeEmpty();
        tree.IsLoading.ShouldBeFalse();
    }

    [Test]
    public async Task UnloadedFolder_HasPlaceholderChild()
    {
        var tree = await Loaded(Sample());

        tree.Roots[0].Children.Count.ShouldBe(1);
        tree.Roots[0].Children[0].IsPlaceholder.ShouldBeTrue();
        tree.Roots[1].Children.ShouldBeEmpty();
    }

    [Test]
    public async Task ClickOnIndeterminateFolder_ChecksAll()
    {
        var tree = await Loaded(Sample());
        await tree.Roots[0].EnsureLoadedAsync();
        await tree.Roots[0].Children[1].SetRvtAsync(true);
        tree.Roots[0].Rvt.ShouldBeNull();

        // Чекбокс без третьего состояния после «частично» присылает false.
        tree.Roots[0].Rvt = false;

        tree.Roots[0].Rvt.ShouldBe(true);
        tree.RvtCount.ShouldBe(3);
    }

    [Test]
    public async Task FolderThatFailsToLoad_StaysUnchecked()
    {
        var source = Sample();
        source.Remove("ЖК/К1"); // папка К1 не загрузится
        var tree = await Loaded(source);

        await tree.Roots[0].SetRvtAsync(true);

        var k1 = tree.Roots[0].Children[0];
        k1.Rvt.ShouldBe(false);
        tree.Roots[0].Rvt.ShouldBeNull(); // отмечена только LES_PARK.rvt
        tree.RvtCount.ShouldBe(1);
        tree.Error.ShouldNotBeNull();
    }

    [Test]
    public async Task RootFolderThatFailsToLoad_StaysUnchecked()
    {
        var source = Sample();
        source.Remove("ЖК");
        var tree = await Loaded(source);

        await tree.Roots[0].SetRvtAsync(true);

        tree.Roots[0].Rvt.ShouldBe(false);
        tree.RvtCount.ShouldBe(0);
    }

    [Test]
    public async Task Depth_GrowsWithNesting()
    {
        var tree = await Loaded(Sample());
        await tree.Roots[0].SetRvtAsync(true);

        tree.Roots[0].Depth.ShouldBe(0);
        tree.Roots[0].Children[0].Depth.ShouldBe(1);
        tree.Roots[0].Children[0].Children[0].Depth.ShouldBe(2);
    }

    [Test]
    public async Task CheckedModels_ListsCheckedModelsWithFormats()
    {
        var tree = await Loaded(Sample());
        await tree.Roots[1].SetRvtAsync(true);
        await tree.Roots[0].SetNwcAsync(true);
        await tree.Roots[0].Children[1].SetRvtAsync(true);

        tree.CheckedModels().OrderBy(m => m.ModelPath, StringComparer.Ordinal).ShouldBe(
        [
            new CheckedModel("ЖК", "path:ЖК/LES_PARK.rvt", true, true),
            new CheckedModel("ЖК/К1", "path:ЖК/К1/LES_K1_AR.rvt", false, true),
            new CheckedModel("ЖК/К1", "path:ЖК/К1/LES_K1_KR.rvt", false, true),
            new CheckedModel("", "path:Школа.rvt", true, false)
        ]);
    }

    [Test]
    public async Task ApplyChecked_LoadsFoldersAndChecksSavedModels()
    {
        var tree = await Loaded(Sample());

        var missing = await tree.ApplyCheckedAsync([new CheckedModel("ЖК/К1", "path:ЖК/К1/LES_K1_AR.rvt", true, false)]);

        missing.ShouldBeEmpty();
        var zhk = tree.Roots[0];
        var k1 = zhk.Children[0];
        k1.Children[0].Rvt.ShouldBe(true);
        k1.Children[1].Rvt.ShouldBe(false);
        k1.Rvt.ShouldBeNull();
        zhk.Rvt.ShouldBeNull();
        zhk.IsExpanded.ShouldBeTrue();
        k1.IsExpanded.ShouldBeTrue();
        tree.RvtCount.ShouldBe(1);
        tree.NwcCount.ShouldBe(0);
    }

    [Test]
    public async Task ApplyChecked_ClearsPreviousChecks()
    {
        var tree = await Loaded(Sample());
        await tree.Roots[0].SetRvtAsync(true);

        await tree.ApplyCheckedAsync([new CheckedModel("", "path:Школа.rvt", false, true)]);

        tree.RvtCount.ShouldBe(0);
        tree.NwcCount.ShouldBe(1);
        tree.Roots[1].Nwc.ShouldBe(true);
    }

    [Test]
    public async Task ApplyChecked_ReportsModelsMissingFromSource()
    {
        var tree = await Loaded(Sample());
        var gone = new CheckedModel("ЖК/К2", "path:ЖК/К2/LES_K2_AR.rvt", true, false);

        var missing = await tree.ApplyCheckedAsync([gone, new CheckedModel("", "path:Школа.rvt", true, false)]);

        missing.ShouldBe([gone]);
        tree.RvtCount.ShouldBe(1);
    }

    [Test]
    public async Task ApplyChecked_IgnoresNwcWhenExporterMissing()
    {
        var tree = await Loaded(Sample());
        tree.NwcEnabled = false;

        await tree.ApplyCheckedAsync([new CheckedModel("", "path:Школа.rvt", true, true)]);

        tree.Roots[1].Rvt.ShouldBe(true);
        tree.Roots[1].Nwc.ShouldBe(false);
    }

    private static async Task<ModelTreeViewModel> Loaded(FakeSource source)
    {
        var tree = new ModelTreeViewModel();
        await tree.LoadAsync(source);
        return tree;
    }

    private static SourceEntry Folder(string name, string path) => new(name, path, "", true);

    private static SourceEntry Model(string name, string folder) =>
        new(name, folder, "path:" + RevitServerPaths.Combine(folder, name), false);

    private sealed class FakeSource : Dictionary<string, SourceEntry[]>, ITreeSource
    {
        public string RootPath => "";

        public Task<IReadOnlyList<SourceEntry>> GetChildrenAsync(string folderPath, CancellationToken ct) =>
            TryGetValue(folderPath, out var entries)
                ? Task.FromResult<IReadOnlyList<SourceEntry>>(entries)
                : throw new SourceException("нет папки " + folderPath);
    }
}
