using Shouldly;
using ToadExporter.Core;

namespace ToadExporter.Tests;

public class RunValidatorTests
{
    private static readonly string Output = Path.Combine(Path.GetTempPath(), "te-out-" + Guid.NewGuid().ToString("N"));

    private static ExportSettings Settings() => new() { OutputFolder = Output };

    [Test]
    public void ValidRun_NoErrors() =>
        RunValidator.Errors([new ModelJob("AR.rvt", @"D:\src\AR.rvt", false, true, true)], Settings()).ShouldBeEmpty();

    [Test]
    public void NoModels_IsError() =>
        RunValidator.Errors([], Settings()).ShouldContain(e => e.Contains("Не отмечено ни одной модели"));

    [Test]
    public void EmptyOutputFolder_IsError() =>
        RunValidator.Errors([new ModelJob("AR.rvt", @"D:\src\AR.rvt", false, true, false)], new ExportSettings())
            .ShouldContain(e => e.Contains("папку для результатов"));

    [Test]
    public void OutputEqualsSourceFolder_IsError()
    {
        var job = new ModelJob("AR.rvt", Path.Combine(Output.ToUpperInvariant(), "AR.rvt"), false, true, false);

        RunValidator.Errors([job], Settings())
            .ShouldContain(e => e.Contains("совпадает с папкой модели AR.rvt"));
    }

    [Test]
    public void OutputIsJunctionToSourceFolder_IsError()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "te-jn-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
            File.WriteAllText(Path.Combine(source, "AR.rvt"), "");
            var junction = Path.Combine(root, "out");
            var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "cmd.exe", $"/c mklink /J \"{junction}\" \"{source}\"") { CreateNoWindow = true, UseShellExecute = false })!;
            mklink.WaitForExit();
            Directory.Exists(junction).ShouldBeTrue();

            var settings = new ExportSettings { OutputFolder = junction };
            RunValidator.Errors([new ModelJob("AR.rvt", Path.Combine(source, "AR.rvt"), false, true, false)], settings)
                .ShouldContain(e => e.Contains("совпадает с папкой модели AR.rvt"));
        }
        finally
        {
            var link = Path.Combine(root, "out");
            if (Directory.Exists(link)) Directory.Delete(link); // сама ссылка, без содержимого папки
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void NwcWithoutViewName_IsError()
    {
        var settings = Settings();
        settings.NwcViewName = " ";

        RunValidator.Errors([new ModelJob("AR.rvt", @"D:\src\AR.rvt", false, false, true)], settings)
            .ShouldContain(e => e.Contains("3D-вида для NWC"));
    }

    [Test]
    [Arguments(0.0)]
    [Arguments(-1.0)]
    [Arguments(double.NaN)]
    public void NwcWithInvalidFacetingFactor_IsError(double factor)
    {
        var settings = Settings();
        settings.NwcFacetingFactor = factor;

        RunValidator.Errors([new ModelJob("AR.rvt", @"D:\src\AR.rvt", false, false, true)], settings)
            .ShouldContain(e => e.Contains("Коэффициент фасетизации"));
    }

    [Test]
    public void RvtOnly_IgnoresFacetingFactor()
    {
        var settings = Settings();
        settings.NwcFacetingFactor = 0;

        RunValidator.Errors([new ModelJob("AR.rvt", @"D:\src\AR.rvt", false, true, false)], settings).ShouldBeEmpty();
    }

    [Test]
    public void ServerJobs_NoFolderCheck() =>
        RunValidator.Errors([new ModelJob("AR.rvt", "RSN://rs01/AR.rvt", true, true, false)], Settings()).ShouldBeEmpty();

    [Test]
    public void Duplicates_ByFileNameIgnoreCase()
    {
        ModelJob[] jobs =
        [
            new("AR.rvt", @"D:\a\AR.rvt", false, true, false),
            new("ar.rvt", @"D:\b\ar.rvt", false, true, false),
            new("KR.rvt", @"D:\a\KR.rvt", false, true, false)
        ];

        var duplicates = RunValidator.Duplicates(jobs);

        duplicates.Count.ShouldBe(1);
        duplicates[0].Count().ShouldBe(2);
    }
}
