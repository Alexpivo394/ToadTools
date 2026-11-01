using Collisions.Core;
using Shouldly;

namespace ToadTools.Tests.Collisions;

public class ModelNamesTests
{
    [Test]
    public void ModelNames_LocalCopy_StripsUserSuffix()
    {
        ModelNames.CentralName("DON_OVK_SOB_STLB_R22_ivanov.rvt", "Ivanov").ShouldBe("DON_OVK_SOB_STLB_R22.rvt");
        ModelNames.CentralName("DON_OVK.rvt", "ivanov").ShouldBe("DON_OVK.rvt");
        ModelNames.CentralName("A_ivanov_B.rvt", "ivanov").ShouldBe("A_ivanov_B.rvt");
        ModelNames.CentralName("DON_OVK_IVANOV", "ivanov").ShouldBe("DON_OVK");
        ModelNames.CentralName("_ivanov.rvt", "ivanov").ShouldBe("_ivanov.rvt"); // от имени ничего бы не осталось
    }
}

public class ModelChoicesTests
{
    [Test]
    public void ModelChoice_Remembered_PerPath()
    {
        using var temp = new TempFolder();
        var choices = new ModelChoices(temp.File("models.json"));

        choices.Get(@"C:\Users\ivanov\DON_OVK_ivanov.rvt").ShouldBeNull();
        choices.Set(@"C:\Users\ivanov\DON_OVK_ivanov.rvt", "key-1");
        choices.Set(@"D:\Other\DON_OVK.rvt", "key-2");

        var reopened = new ModelChoices(temp.File("models.json"));
        reopened.Get(@"c:\users\IVANOV\DON_OVK_ivanov.rvt").ShouldBe("key-1");
        reopened.Get(@"D:\Other\DON_OVK.rvt").ShouldBe("key-2");
        reopened.Set(@"D:\Other\DON_OVK.rvt", "key-3");
        new ModelChoices(temp.File("models.json")).Get(@"D:\Other\DON_OVK.rvt").ShouldBe("key-3");
    }

    [Test]
    public async Task ModelChoice_BrokenFile_StartsEmpty()
    {
        using var temp = new TempFolder();
        await File.WriteAllTextAsync(temp.File("models.json"), "not json");

        new ModelChoices(temp.File("models.json")).Get("x").ShouldBeNull();
    }
}

public class CollisionBoxTests
{
    [Test]
    public void Box_Default3m()
    {
        CollisionBox.Around((1000, 2000, 3000), null).ShouldBe(new Box3(-500, 500, 1500, 2500, 3500, 4500));
    }

    [Test]
    public void Box_GrowsToElements()
    {
        // элементы — от -4000 по X до 1500 по Z: куб растёт симметрично вокруг точки до дальней грани
        var box = CollisionBox.Around((0, 0, 0), new Box3(-4000, -100, -100, 1000, 100, 2500));

        box.ShouldBe(new Box3(-4000, -1500, -2500, 4000, 1500, 2500));
    }

    [Test]
    public void Box_CappedAt20m()
    {
        var box = CollisionBox.Around((100, 0, 0), new Box3(-50_000, -10, -10, 90_000, 10, 10));

        box.ShouldBe(new Box3(-9_900, -1500, -1500, 10_100, 1500, 1500));
    }
}
