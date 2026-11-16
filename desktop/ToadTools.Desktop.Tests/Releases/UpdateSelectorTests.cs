using Shouldly;
using ToadTools.Desktop.Core.Releases;

namespace ToadTools.Desktop.Tests.Releases;

public class UpdateSelectorTests
{
    private static ReleaseAsset Asset(string name) => new(name, new Uri("https://example.org/" + name), 10);

    private static ReleaseInfo Release(string tag, params string[] assets) => new(tag, "", assets.Select(Asset).ToArray());

    private static readonly ReleaseInfo Full = Release("4.0.3",
        "ToadTools-4.0.3.zip", "ToadTools-4.0.3.zip.sig", "ToadTools-App-1.1.0.msi", "ToadTools-App-1.1.0.msi.sig");

    [Test]
    public void NewerPlugin_IsChosen()
    {
        var choice = UpdateSelector.Choose(Full, new Version(4, 0, 2), new Version(1, 1, 0));

        choice.Plugin.ShouldBe(new Version(4, 0, 3));
        choice.PluginZip!.Name.ShouldBe("ToadTools-4.0.3.zip");
        choice.PluginSig!.Name.ShouldBe("ToadTools-4.0.3.zip.sig");
    }

    [Test]
    public void SamePlugin_IsNotChosen() =>
        UpdateSelector.Choose(Full, new Version(4, 0, 3), new Version(1, 1, 0)).Plugin.ShouldBeNull();

    [Test]
    public void NoDeployedPlugin_IsChosen() =>
        UpdateSelector.Choose(Full, null, new Version(1, 1, 0)).Plugin.ShouldBe(new Version(4, 0, 3));

    [Test]
    public void PluginWithoutSignature_IsNotChosen() =>
        UpdateSelector.Choose(Release("4.0.3", "ToadTools-4.0.3.zip"), null, new Version(1, 0, 0)).Plugin.ShouldBeNull();

    [Test]
    public void NewerApp_IsChosen()
    {
        var choice = UpdateSelector.Choose(Full, new Version(4, 0, 3), new Version(1, 0, 0));

        choice.App.ShouldBe(new Version(1, 1, 0));
        choice.AppMsi!.Name.ShouldBe("ToadTools-App-1.1.0.msi");
        choice.AppSig!.Name.ShouldBe("ToadTools-App-1.1.0.msi.sig");
    }

    [Test]
    public void SameApp_IsNotChosen() =>
        UpdateSelector.Choose(Full, new Version(4, 0, 3), new Version(1, 1, 0)).App.ShouldBeNull();

    [Test]
    public void AppWithoutSignature_IsNotChosen() =>
        UpdateSelector.Choose(Release("4.0.3", "ToadTools-App-1.1.0.msi"), null, new Version(1, 0, 0)).App.ShouldBeNull();

    [Test]
    public void TagWithV_IsParsed()
    {
        var release = Release("v4.0.3", "ToadTools-4.0.3.zip", "ToadTools-4.0.3.zip.sig");

        UpdateSelector.Choose(release, new Version(4, 0, 2), new Version(1, 0, 0)).Plugin.ShouldBe(new Version(4, 0, 3));
    }

    [Test]
    public void AssemblyVersionWithFourParts_EqualsThreePartTag() =>
        UpdateSelector.Choose(Full, new Version(4, 0, 3, 0), new Version(1, 1, 0, 0)).ShouldSatisfyAllConditions(
            choice => choice.Plugin.ShouldBeNull(),
            choice => choice.App.ShouldBeNull());
}
