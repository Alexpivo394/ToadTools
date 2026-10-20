using Shouldly;
using ToadExporter.Core;

namespace ToadExporter.Tests;

public class RevitServerPathsTests
{
    [Test]
    public void ContentsUrl_Root() =>
        RevitServerPaths.ContentsUrl("rs01", "2022", "")
            .ShouldBe("http://rs01/RevitServerAdminRESTService2022/AdminRESTService.svc/%7C/contents");

    [Test]
    public void ContentsUrl_EncodesSegments() =>
        RevitServerPaths.ContentsUrl("rs01", "2022", "ЖК Лесной/К#1")
            .ShouldBe("http://rs01/RevitServerAdminRESTService2022/AdminRESTService.svc/" +
                      "%D0%96%D0%9A%20%D0%9B%D0%B5%D1%81%D0%BD%D0%BE%D0%B9%7C%D0%9A%231/contents");

    [Test]
    public void ModelPath_Root() =>
        RevitServerPaths.ModelPath("rs01", "", "AR.rvt").ShouldBe("RSN://rs01/AR.rvt");

    [Test]
    public void ModelPath_Nested() =>
        RevitServerPaths.ModelPath("rs01", "ЖК/К1", "AR.rvt").ShouldBe("RSN://rs01/ЖК/К1/AR.rvt");
}
