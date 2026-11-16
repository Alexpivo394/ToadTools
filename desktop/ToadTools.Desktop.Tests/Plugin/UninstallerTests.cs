using Shouldly;
using ToadTools.Desktop.Core;
using ToadTools.Desktop.Core.Plugin;

namespace ToadTools.Desktop.Tests.Plugin;

public class UninstallerTests
{
    [Test]
    public void RemovesPluginFromAllYears_AndWorkingFolders()
    {
        using var temp = new TempFolder();
        var paths = new AppPaths(temp.Combine("local"), temp.Combine("roaming"), temp.Combine("Addins"));
        var zip = PluginArchiveTests.MakeZip(temp.Path, "p.zip", [..PluginArchiveTests.Year(2024), ..PluginArchiveTests.Year(2025)]);
        var staged = PluginArchive.Extract(zip, paths.StagingDir, new Version(4, 0, 3));
        new PluginDeployer(paths).Deploy(staged, [2024, 2025]);
        Directory.CreateDirectory(paths.DownloadsDir);
        File.WriteAllText(paths.StateFile, "{}");
        File.WriteAllText(Path.Combine(paths.AddinsYear(2025), "Other.addin"), "foreign");

        Uninstaller.Run(paths);

        new PluginDeployer(paths).IsDeployed(2024).ShouldBeFalse();
        new PluginDeployer(paths).IsDeployed(2025).ShouldBeFalse();
        File.Exists(Path.Combine(paths.AddinsYear(2025), "Other.addin")).ShouldBeTrue();
        Directory.Exists(paths.StagingDir).ShouldBeFalse();
        Directory.Exists(paths.DownloadsDir).ShouldBeFalse();
        File.Exists(paths.StateFile).ShouldBeFalse();
    }

    [Test]
    public void NothingInstalled_DoesNotThrow()
    {
        using var temp = new TempFolder();

        Uninstaller.Run(new AppPaths(temp.Combine("l"), temp.Combine("r"), temp.Combine("A")));
    }

    [Test]
    public void LegacyMachineWide_Found()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Combine("ProgramData", "2024"));
        File.WriteAllText(temp.Combine("ProgramData", "2024", "ToadTools.addin"), "x");

        LegacyInstall.FindMachineWide([temp.Combine("ProgramData"), temp.Combine("missing")]).ShouldBe([temp.Combine("ProgramData", "2024", "ToadTools.addin")]);
    }
}
