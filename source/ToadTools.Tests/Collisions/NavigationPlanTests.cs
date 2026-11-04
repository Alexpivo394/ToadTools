using Collisions.Core;
using Shouldly;

namespace ToadTools.Tests.Collisions;

public class NavigationPlanTests
{
    private static readonly Box3 Cube = new(1000, 2000, 3000, 4000, 5000, 6000);

    [Test]
    public void SameBox_NoTransaction()
    {
        var plan = NavigationPlanner.Plan(new ViewState(true, Cube with { MinX = 1000.6, MaxZ = 5999.2 }), Cube);

        plan.ShouldBe(new NavigationPlan(false, false));
        plan.NeedsTransaction.ShouldBeFalse();
    }

    [Test]
    public void OtherBox_OneTransaction()
    {
        NavigationPlanner.Plan(new ViewState(true, Cube with { MaxY = 5002 }), Cube).ShouldBe(new NavigationPlan(false, true));
        NavigationPlanner.Plan(new ViewState(true, null), Cube).NeedsTransaction.ShouldBeTrue();
    }

    [Test]
    public void NoView_CreatesViewAndBox()
    {
        NavigationPlanner.Plan(new ViewState(false, null), Cube).ShouldBe(new NavigationPlan(true, true));
    }
}
