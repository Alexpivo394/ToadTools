using Collisions.Core;
using Shouldly;

namespace ToadTools.Tests.Collisions;

public class ClashLinkTests
{
    private static readonly Guid Id = Guid.Parse("3f2a8c1e-5b6d-4e7f-9a0b-1c2d3e4f5a6b");

    [Test]
    public void ClashLink_Url_Id()
    {
        var link = ClashLink.Parse($"ToadClash №17 — Корпус 1\nhttp://toadflow:8080/clash/checks/{Guid.NewGuid()}?collision={Id}");

        link.ShouldBe(new ClashLink(Id, null));
    }

    [Test]
    public void ClashLink_Text_Number()
    {
        ClashLink.Parse("  ToadClash № 123 — Корпус 1 ").ShouldBe(new ClashLink(null, 123));
        ClashLink.Parse("toadclash №5").ShouldBe(new ClashLink(null, 5));
    }

    [Test]
    public void ClashLink_Digits_Number()
    {
        ClashLink.Parse(" 4521\r\n").ShouldBe(new ClashLink(null, 4521));
    }

    [Test]
    public void ClashLink_Other_Null()
    {
        ClashLink.Parse("").ShouldBeNull();
        ClashLink.Parse("стена 200").ShouldBeNull();
        ClashLink.Parse("12 34").ShouldBeNull();
        ClashLink.Parse("99999999999").ShouldBeNull();
        ClashLink.Parse("http://toadflow/clash/checks/1?collision=не-guid").ShouldBeNull();
    }
}
