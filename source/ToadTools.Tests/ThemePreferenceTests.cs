using ToadTools.UI.Services;

namespace ToadTools.Tests;

public class ThemePreferenceTests
{
    [Test]
    public async Task Parse_OldLightFile_LightAuto()
    {
        var (theme, density) = ThemePreference.Parse("""{"dark":false}""");
        await Assert.That(theme).IsEqualTo(ThemeChoice.Light);
        await Assert.That(density).IsEqualTo(DensityChoice.Auto);
    }

    [Test]
    public async Task Parse_OldDarkFile_DarkAuto()
    {
        var (theme, density) = ThemePreference.Parse("""{"dark":true}""");
        await Assert.That(theme).IsEqualTo(ThemeChoice.Dark);
        await Assert.That(density).IsEqualTo(DensityChoice.Auto);
    }

    [Test]
    public async Task Parse_Broken_DarkAuto()
    {
        var (theme, density) = ThemePreference.Parse("{ not json");
        await Assert.That(theme).IsEqualTo(ThemeChoice.Dark);
        await Assert.That(density).IsEqualTo(DensityChoice.Auto);
    }

    [Test]
    public async Task Parse_New_SystemCompact()
    {
        var (theme, density) = ThemePreference.Parse("""{"dark":true,"theme":"system","density":"compact"}""");
        await Assert.That(theme).IsEqualTo(ThemeChoice.System);
        await Assert.That(density).IsEqualTo(DensityChoice.Compact);
    }

    [Test]
    public async Task Parse_ThemeWinsOverOldDarkFlag()
    {
        var (theme, _) = ThemePreference.Parse("""{"dark":true,"theme":"light"}""");
        await Assert.That(theme).IsEqualTo(ThemeChoice.Light);
    }

    [Test]
    public async Task AutoCompact_899_True() => await Assert.That(ThemePreference.AutoCompact(899)).IsTrue();

    [Test]
    public async Task AutoCompact_900_False() => await Assert.That(ThemePreference.AutoCompact(900)).IsFalse();
}
