using Shouldly;
using ToadTools.Desktop.Core;
using ToadTools.Desktop.Core.Plugin;

namespace ToadTools.Desktop.Tests.Plugin;

public class PluginDeployerTests
{
    private sealed class Bench : IDisposable
    {
        private readonly TempFolder _temp = new();
        public AppPaths Paths { get; }
        public PluginDeployer Deployer { get; }

        public Bench()
        {
            Paths = new AppPaths(_temp.Combine("local"), _temp.Combine("roaming"), _temp.Combine("Addins"));
            Deployer = new PluginDeployer(Paths);
        }

        public string Stage(string version, params int[] years)
        {
            var zip = PluginArchiveTests.MakeZip(_temp.Path, $"p-{version}.zip",
                years.SelectMany(year => PluginArchiveTests.Year(year, version)).ToArray());
            return PluginArchive.Extract(zip, Paths.StagingDir, Version.Parse(version));
        }

        public string Addins(int year, params string[] parts) => Path.Combine([Paths.AddinsYear(year), ..parts]);

        public void Dispose() => _temp.Dispose();
    }

    [Test]
    public void FreshInstall_PutsFiles()
    {
        using var bench = new Bench();

        var result = bench.Deployer.Deploy(bench.Stage("4.0.3", 2025), [2025]);

        result.Deployed.ShouldBe([2025]);
        result.Locked.ShouldBeEmpty();
        File.ReadAllText(bench.Addins(2025, "ToadTools", "ToadTools.dll")).ShouldBe("dll 4.0.3");
        File.Exists(bench.Addins(2025, "ToadTools.addin")).ShouldBeTrue();
    }

    [Test]
    public void Update_ReplacesFiles_NoLeftovers()
    {
        using var bench = new Bench();
        bench.Deployer.Deploy(bench.Stage("4.0.2", 2025), [2025]);
        File.WriteAllText(bench.Addins(2025, "ToadTools", "removed-in-new.dll"), "old");

        bench.Deployer.Deploy(bench.Stage("4.0.3", 2025), [2025]).Deployed.ShouldBe([2025]);

        File.ReadAllText(bench.Addins(2025, "ToadTools", "ToadTools.dll")).ShouldBe("dll 4.0.3");
        File.Exists(bench.Addins(2025, "ToadTools", "removed-in-new.dll")).ShouldBeFalse();
        Directory.Exists(bench.Addins(2025, "ToadTools.old")).ShouldBeFalse();
        Directory.Exists(bench.Addins(2025, "ToadTools.new")).ShouldBeFalse();
        File.Exists(bench.Addins(2025, "ToadTools.addin.tmp")).ShouldBeFalse();
    }

    [Test]
    public void FileHeldByRevit_Locked_OldVersionIntact()
    {
        using var bench = new Bench();
        bench.Deployer.Deploy(bench.Stage("4.0.2", 2025), [2025]);

        DeployResult result;
        using (new FileStream(bench.Addins(2025, "ToadTools", "ToadTools.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            result = bench.Deployer.Deploy(bench.Stage("4.0.3", 2025), [2025]);
        }

        result.Locked.ShouldBe([2025]);
        result.Deployed.ShouldBeEmpty();
        File.ReadAllText(bench.Addins(2025, "ToadTools", "ToadTools.dll")).ShouldBe("dll 4.0.2");
        Directory.Exists(bench.Addins(2025, "ToadTools.new")).ShouldBeFalse();
        Directory.Exists(bench.Addins(2025, "ToadTools.old")).ShouldBeFalse();
    }

    [Test]
    public void OneYearLocked_OtherYearStillDeployed()
    {
        using var bench = new Bench();
        bench.Deployer.Deploy(bench.Stage("4.0.2", 2024, 2025), [2024, 2025]);

        DeployResult result;
        using (File.Open(bench.Addins(2024, "ToadTools", "ToadTools.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            result = bench.Deployer.Deploy(bench.Stage("4.0.3", 2024, 2025), [2024, 2025]);
        }

        result.Deployed.ShouldBe([2025]);
        result.Locked.ShouldBe([2024]);
    }

    [Test]
    public void YearNotInStaging_Skipped()
    {
        using var bench = new Bench();

        var result = bench.Deployer.Deploy(bench.Stage("4.0.3", 2025), [2024, 2025]);

        result.Deployed.ShouldBe([2025]);
        Directory.Exists(bench.Addins(2024)).ShouldBeFalse();
    }

    [Test]
    public void Recover_OldWithoutCurrent_RestoresOld()
    {
        using var bench = new Bench();
        Directory.CreateDirectory(bench.Addins(2025, "ToadTools.old"));
        File.WriteAllText(bench.Addins(2025, "ToadTools.old", "ToadTools.dll"), "dll 4.0.2");
        Directory.CreateDirectory(bench.Addins(2025, "ToadTools.new"));

        bench.Deployer.Recover();

        File.ReadAllText(bench.Addins(2025, "ToadTools", "ToadTools.dll")).ShouldBe("dll 4.0.2");
        Directory.Exists(bench.Addins(2025, "ToadTools.old")).ShouldBeFalse();
        Directory.Exists(bench.Addins(2025, "ToadTools.new")).ShouldBeFalse();
    }

    [Test]
    public void Recover_OldAndCurrent_DeletesOld()
    {
        using var bench = new Bench();
        Directory.CreateDirectory(bench.Addins(2025, "ToadTools.old"));
        Directory.CreateDirectory(bench.Addins(2025, "ToadTools"));
        File.WriteAllText(bench.Addins(2025, "ToadTools", "ToadTools.dll"), "dll 4.0.3");
        File.WriteAllText(bench.Addins(2025, "ToadTools.addin.tmp"), "x");

        bench.Deployer.Recover();

        Directory.Exists(bench.Addins(2025, "ToadTools.old")).ShouldBeFalse();
        File.Exists(bench.Addins(2025, "ToadTools.addin.tmp")).ShouldBeFalse();
        File.ReadAllText(bench.Addins(2025, "ToadTools", "ToadTools.dll")).ShouldBe("dll 4.0.3");
    }

    [Test]
    public void Recover_NoAddinsFolder_DoesNothing()
    {
        using var bench = new Bench();

        bench.Deployer.Recover();
    }

    [Test]
    public void Remove_DeletesOnlyOurFiles()
    {
        using var bench = new Bench();
        bench.Deployer.Deploy(bench.Stage("4.0.3", 2025), [2025]);
        File.WriteAllText(bench.Addins(2025, "Other.addin"), "foreign");

        bench.Deployer.Remove(2025);

        Directory.Exists(bench.Addins(2025, "ToadTools")).ShouldBeFalse();
        File.Exists(bench.Addins(2025, "ToadTools.addin")).ShouldBeFalse();
        File.Exists(bench.Addins(2025, "Other.addin")).ShouldBeTrue();
        bench.Deployer.IsDeployed(2025).ShouldBeFalse();
    }

    [Test]
    public void DeployedVersion_ReadsDllFileVersion()
    {
        using var bench = new Bench();
        Directory.CreateDirectory(bench.Addins(2025, "ToadTools"));
        File.Copy(typeof(Shouldly.ShouldBeTestExtensions).Assembly.Location, bench.Addins(2025, "ToadTools", "ToadTools.dll"));

        bench.Deployer.DeployedVersion(2025).ShouldNotBeNull();
        bench.Deployer.DeployedVersion(2024).ShouldBeNull();
    }
}
