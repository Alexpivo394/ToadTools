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
    public void Box_2m_CenteredOnPoint()
    {
        CollisionBox.Around((10_000, -5_000, 3_000)).ShouldBe(new Box3(9_000, -6_000, 2_000, 11_000, -4_000, 4_000));
    }
}
