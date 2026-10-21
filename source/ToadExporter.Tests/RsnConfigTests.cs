using Shouldly;
using ToadExporter.Core;
using ToadTools.UI.RevitServer;

namespace ToadExporter.Tests;

public class RsnConfigTests
{
    [Test]
    public void ReadServers_SkipsBlankAndDuplicates()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "rs01\r\n\r\n RS01 \r\nrs02\r\n");
            RsnConfig.ReadServers(path).ShouldBe(["rs01", "rs02"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ReadServers_MissingFile_Empty() =>
        RsnConfig.ReadServers(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ini")).ShouldBeEmpty();

    [Test]
    public void DefaultPath_UsesRevitYear() =>
        RsnConfig.DefaultPath("2022").ShouldEndWith(@"Autodesk\Revit Server 2022\Config\RSN.ini");
}
