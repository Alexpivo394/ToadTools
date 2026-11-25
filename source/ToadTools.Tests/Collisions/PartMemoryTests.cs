using Collisions.Core;
using Shouldly;

namespace ToadTools.Tests.Collisions;

public class PartMemoryTests
{
    private static readonly Guid Part = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    [Test]
    public void PartMemory_SetThenGet_SurvivesNewInstance()
    {
        using var temp = new TempFolder();

        new PartMemory(temp.File("collisions.json")).Set("k1", Part);

        new PartMemory(temp.File("collisions.json")).Get("k1").ShouldBe(Part);
        new PartMemory(temp.File("collisions.json")).Get("k2").ShouldBeNull();
    }

    [Test]
    public void PartMemory_SetNull_ForgetsOnlyThatModel()
    {
        using var temp = new TempFolder();
        var memory = new PartMemory(temp.File("collisions.json"));
        memory.Set("k1", Part);
        memory.Set("k2", Part);

        memory.Set("k1", null);

        (memory.Get("k1"), memory.Get("k2")).ShouldBe((null, Part));
    }

    [Test]
    public void PartMemory_BrokenFile_Empty()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.File("collisions.json"), "{ не json");
        var memory = new PartMemory(temp.File("collisions.json"));

        memory.Get("k1").ShouldBeNull();
        memory.Set("k1", Part);
        memory.Get("k1").ShouldBe(Part);
    }
}
