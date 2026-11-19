using System.Text.RegularExpressions;
using ClassifierCode.Engine.Patterns;
using Shouldly;

namespace ToadTools.Tests.ClassifierCode;

public class PatternCompilerTests
{
    private static readonly Dictionary<string, List<string>> NoSets = new();

    private static Regex P(string pattern, Dictionary<string, List<string>>? sets = null) =>
        PatternCompiler.Compile(pattern, sets ?? NoSets);

    [Test]
    public void Plain_WordStart()
    {
        P("кран").IsMatch("Кран шаровой").ShouldBeTrue();
        P("кран").IsMatch("экран").ShouldBeFalse();
        P("k-flex").IsMatch("K-FLEX ST").ShouldBeTrue();
    }

    [Test]
    public void LeadingStar_Anywhere()
    {
        P("*изоляц").IsMatch("Теплоизоляция труб").ShouldBeTrue();
        P("*шкаф пожарн").ToString().ShouldBe(@"шкаф\w*\s+пожарн");
    }

    [Test]
    public void TrailingStar_SameAsPlain()
    {
        P("трап*").IsMatch("Трапы канализационные").ShouldBeTrue();
        P("трап*").IsMatch("атрап").ShouldBeFalse();
    }

    [Test]
    public void Equals_WholeWord()
    {
        P("=пк").IsMatch("2 пк в шкафу").ShouldBeTrue();
        P("=пк").IsMatch("пкс").ShouldBeFalse();
    }

    [Test]
    public void Phrase()
    {
        P("шкаф пожарн").IsMatch("Шкафы пожарные навесные").ShouldBeTrue();
        P("шкаф пожарн").IsMatch("пожарный шкаф").ShouldBeFalse();
        P("=ниша пожарн*").IsMatch("ниша пожарная").ShouldBeTrue();
    }

    [Test]
    public void SystemCode()
    {
        P("#В1").IsMatch("В1.1").ShouldBeTrue();
        P("#В1").IsMatch("1В1").ShouldBeTrue();
        P("#В1").IsMatch("ПВ1").ShouldBeFalse();
        P("#В1").IsMatch("В11").ShouldBeFalse();
        P("#!В1").IsMatch("1В1").ShouldBeFalse();
        P("#!В1").IsMatch(" В1").ShouldBeTrue();
    }

    [Test]
    public void Raw()
    {
        P(@"re:^\s*кабел").IsMatch("  кабель ВВГ").ShouldBeTrue();
        P(@"re:^\s*кабел").IsMatch("лоток кабельный").ShouldBeFalse();
    }

    [Test]
    public void Set()
    {
        var sets = new Dictionary<string, List<string>> { ["fire"] = ["огнетуш", "=пк"] };
        P("@fire", sets).IsMatch("Огнетушитель ОП-5").ShouldBeTrue();
        P("@fire", sets).IsMatch("ПК-с 50").ShouldBeTrue();
        P("@fire", sets).IsMatch("пкс").ShouldBeFalse();
    }

    [Test]
    public void Invalid_Throws()
    {
        var error = Should.Throw<PatternException>(() => P("re:("));
        error.Message.ShouldContain("re:(");
    }

    [Test]
    public void UnknownSet_Throws()
    {
        var error = Should.Throw<PatternException>(() => P("@нет"));
        error.Message.ShouldContain("@нет");
    }

    [Test]
    public void Catastrophic_TimesOut()
    {
        var regex = P("re:(a+)+$");
        Should.Throw<RegexMatchTimeoutException>(() => regex.IsMatch(new string('a', 40) + "!"));
    }

    [Test]
    public void ToRegexText_ShowsTranslation()
    {
        PatternCompiler.ToRegexText("трап").ShouldBe(@"\bтрап");
        PatternCompiler.ToRegexText("трап*").ShouldBe(@"\bтрап");
        PatternCompiler.ToRegexText("*трап").ShouldBe("трап");
        PatternCompiler.ToRegexText("шкаф пожарн").ShouldBe(@"\bшкаф\w*\s+пожарн");
    }
}
