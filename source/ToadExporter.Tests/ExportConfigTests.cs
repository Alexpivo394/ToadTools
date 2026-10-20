using System.IO;
using Shouldly;
using ToadExporter.Core;

namespace ToadExporter.Tests;

public class ExportConfigTests
{
    private static readonly CheckedModel[] Models =
    [
        new("ЖК/К1", "RSN://srv/ЖК/К1/LES_K1_AR.rvt", true, false),
        new("ЖК", "RSN://srv/ЖК/LES_PARK.rvt", true, true)
    ];

    [Test]
    public async Task SaveThenLoad_RestoresSettingsAndModels()
    {
        using var file = new TempFile();
        var saved = new ExportSettings
        {
            SourceKind = SourceKind.Folder, SourceFolder = @"D:\Модели", OutputFolder = @"D:\Выгрузка",
            NwcViewName = "NW", Compact = false, NwcFacetingFactor = 2.5
        };
        ExportConfig.Save(file.Path, saved, Models);

        var loaded = new ExportSettings();
        var models = ExportConfig.Load(file.Path, loaded);

        loaded.SourceKind.ShouldBe(SourceKind.Folder);
        loaded.SourceFolder.ShouldBe(@"D:\Модели");
        loaded.OutputFolder.ShouldBe(@"D:\Выгрузка");
        loaded.NwcViewName.ShouldBe("NW");
        loaded.Compact.ShouldBeFalse();
        loaded.NwcFacetingFactor.ShouldBe(2.5);
        models.ShouldBe(Models);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Load_KeepsWindowState()
    {
        using var file = new TempFile();
        ExportConfig.Save(file.Path, new ExportSettings { DarkTheme = false, RvtExpanded = false }, Models);

        var target = new ExportSettings { DarkTheme = true, RvtExpanded = true };
        ExportConfig.Load(file.Path, target);

        target.DarkTheme.ShouldBeTrue();
        target.RvtExpanded.ShouldBeTrue();
        File.ReadAllText(file.Path).ShouldNotContain("DarkTheme");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Load_MissingKeysTakeDefaults()
    {
        using var file = new TempFile();
        File.WriteAllText(file.Path, """{ "Settings": { "OutputFolder": "D:\\out" }, "Models": [] }""");

        var target = new ExportSettings { Compact = false, NwcViewName = "Старый" };
        ExportConfig.Load(file.Path, target);

        target.OutputFolder.ShouldBe(@"D:\out");
        target.Compact.ShouldBeTrue();
        target.NwcViewName.ShouldBe("Navisworks");
        await Task.CompletedTask;
    }

    [Test]
    [Arguments("не json")]
    [Arguments("""{ "Something": 1 }""")]
    [Arguments("""{ "Settings": { "NwcFacetingFactor": "много" }, "Models": [] }""")]
    public async Task Load_RejectsForeignFile(string content)
    {
        using var file = new TempFile();
        File.WriteAllText(file.Path, content);
        var target = new ExportSettings { OutputFolder = "keep" };

        Should.Throw<InvalidDataException>(() => ExportConfig.Load(file.Path, target));
        target.OutputFolder.ShouldBe("keep");
        await Task.CompletedTask;
    }

    private sealed class TempFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"toadcfg-{Guid.NewGuid():N}.json");

        public void Dispose() => File.Delete(Path);
    }
}
