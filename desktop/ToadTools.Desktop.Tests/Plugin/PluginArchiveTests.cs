using System.IO.Compression;
using Shouldly;
using ToadTools.Desktop.Core.Plugin;

namespace ToadTools.Desktop.Tests.Plugin;

public class PluginArchiveTests
{
    /// <summary>Архив как у сборки: {год}/ToadTools.addin и {год}/ToadTools/**.</summary>
    public static string MakeZip(string folder, string name, params (string Path, string Text)[] entries)
    {
        var zip = Path.Combine(folder, name);
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var (path, text) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(path).Open());
            writer.Write(text);
        }

        return zip;
    }

    public static (string, string)[] Year(int year, string version = "4.0.3") =>
    [
        ($"{year}/ToadTools.addin", "<RevitAddIns/>"),
        ($"{year}/ToadTools/ToadTools.dll", $"dll {version}"),
        ($"{year}/ToadTools/Resources/icon.png", "png")
    ];

    [Test]
    public void Extract_TwoYears()
    {
        using var temp = new TempFolder();
        var zip = MakeZip(temp.Path, "p.zip", [..Year(2024), ..Year(2025)]);
        var staging = temp.Combine("staging");

        var staged = PluginArchive.Extract(zip, staging, new Version(4, 0, 3));

        staged.ShouldBe(Path.Combine(staging, "4.0.3"));
        PluginArchive.Years(staged).ShouldBe([2024, 2025]);
        File.ReadAllText(Path.Combine(staged, "2025", "ToadTools", "ToadTools.dll")).ShouldBe("dll 4.0.3");
        Directory.Exists(staged + ".tmp").ShouldBeFalse();
    }

    [Test]
    public void PathTraversal_Rejected_NothingCreated()
    {
        using var temp = new TempFolder();
        var zip = MakeZip(temp.Path, "p.zip", [..Year(2025), ("2025/../../evil.dll", "x")]);
        var staging = temp.Combine("staging");

        Should.Throw<InvalidPluginArchiveException>(() => PluginArchive.Extract(zip, staging, new Version(4, 0, 3)));

        File.Exists(temp.Combine("evil.dll")).ShouldBeFalse();
        Directory.Exists(Path.Combine(staging, "4.0.3")).ShouldBeFalse();
    }

    [Test]
    public void TopLevelNotYear_Rejected()
    {
        using var temp = new TempFolder();
        var zip = MakeZip(temp.Path, "p.zip", [..Year(2025), ("readme.txt", "x")]);

        Should.Throw<InvalidPluginArchiveException>(() => PluginArchive.Extract(zip, temp.Combine("staging"), new Version(4, 0, 3)));
    }

    [Test]
    public void YearWithoutAddin_Rejected()
    {
        using var temp = new TempFolder();
        var zip = MakeZip(temp.Path, "p.zip", ("2025/ToadTools/ToadTools.dll", "dll"));

        Should.Throw<InvalidPluginArchiveException>(() => PluginArchive.Extract(zip, temp.Combine("staging"), new Version(4, 0, 3)));
    }

    [Test]
    public void NotAZip_Rejected()
    {
        using var temp = new TempFolder();
        var zip = temp.Combine("p.zip");
        File.WriteAllText(zip, "not a zip");

        Should.Throw<InvalidPluginArchiveException>(() => PluginArchive.Extract(zip, temp.Combine("staging"), new Version(4, 0, 3)));
    }

    [Test]
    public void LeftoverTmp_Overwritten()
    {
        using var temp = new TempFolder();
        var zip = MakeZip(temp.Path, "p.zip", Year(2025));
        var staging = temp.Combine("staging");
        Directory.CreateDirectory(Path.Combine(staging, "4.0.3.tmp", "junk"));

        var staged = PluginArchive.Extract(zip, staging, new Version(4, 0, 3));

        Directory.Exists(Path.Combine(staged, "junk")).ShouldBeFalse();
        PluginArchive.Years(staged).ShouldBe([2025]);
    }

    [Test]
    public void NewVersion_RemovesOlderStaged()
    {
        using var temp = new TempFolder();
        var staging = temp.Combine("staging");
        PluginArchive.Extract(MakeZip(temp.Path, "a.zip", Year(2025)), staging, new Version(4, 0, 3));

        PluginArchive.Extract(MakeZip(temp.Path, "b.zip", Year(2025, "4.0.4")), staging, new Version(4, 0, 4));

        Directory.Exists(Path.Combine(staging, "4.0.3")).ShouldBeFalse();
        Directory.Exists(Path.Combine(staging, "4.0.4")).ShouldBeTrue();
    }

    [Test]
    public void BackslashEntries_AreAccepted()
    {
        using var temp = new TempFolder();
        var zip = MakeZip(temp.Path, "p.zip", (@"2025\ToadTools.addin", "a"), (@"2025\ToadTools\ToadTools.dll", "d"));

        var staged = PluginArchive.Extract(zip, temp.Combine("staging"), new Version(4, 0, 3));

        File.Exists(Path.Combine(staged, "2025", "ToadTools", "ToadTools.dll")).ShouldBeTrue();
    }
}
