using System.Text.RegularExpressions;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Patterns;
using ClassifierCode.Engine.Profile;
using Shouldly;

namespace ToadTools.Tests.ClassifierCode;

public class TextSourceReaderTests
{
    private static readonly Dictionary<string, TextSourceDef> Named = new()
    {
        ["primaryName"] = new TextSourceDef { Parts = ["instanceOrType:ADSK_Наименование"], Lowercase = true },
        ["fullName"] = new TextSourceDef
        {
            Parts = ["instance.name", "instance:ADSK_Наименование", "instance:ADSK_Марка", "type.family", "type.name", "type:ADSK_Наименование", "type:ADSK_Марка"],
            Lowercase = true
        },
        ["primaryOrFull"] = new TextSourceDef { Parts = ["text:primaryName"], Fallback = "fullName" }
    };

    private static Regex Pattern(string pattern) => PatternCompiler.Compile(pattern, new Dictionary<string, List<string>>());

    private static string Read(string name, IElementFacts facts) => TextSourceReader.Read(Named[name], facts, Named, Pattern);

    private static string Read(TextSourceDef source, IElementFacts facts) => TextSourceReader.Read(source, facts, Named, Pattern);

    [Test]
    public void FullName_MatchesOldOrder()
    {
        var facts = new FakeFacts
        {
            InstanceName = "Имя", FamilyName = "Сем", TypeName = "Тип",
            Instance = { ["ADSK_Наименование"] = "Наим", ["ADSK_Марка"] = "Марка" },
            Type = { ["ADSK_Наименование"] = "ТНаим", ["ADSK_Марка"] = "ТМарка" }
        };

        Read("fullName", facts).ShouldBe("имя | наим | марка | сем | тип | тнаим | тмарка");
    }

    [Test]
    public void FullName_EmptyPartsKept()
    {
        Read("fullName", new FakeFacts { InstanceName = "А" }).ShouldBe("а |  |  |  |  |  | ");
    }

    [Test]
    public void InstanceOrType_FallsBackToType()
    {
        Read("primaryName", new FakeFacts { Instance = { ["ADSK_Наименование"] = "  " }, Type = { ["ADSK_Наименование"] = "Кран" } })
            .ShouldBe("кран");
        Read("primaryName", new FakeFacts { Instance = { ["ADSK_Наименование"] = "Клапан" }, Type = { ["ADSK_Наименование"] = "Кран" } })
            .ShouldBe("клапан");
    }

    [Test]
    public void FamilyType_NoType_UsesName()
    {
        var source = new TextSourceDef { Parts = ["familyType"] };
        Read(source, new FakeFacts { HasType = false, InstanceName = "Отверстие" }).ShouldBe("Отверстие");
        Read(source, new FakeFacts { FamilyName = "Пересечение", TypeName = "Круглое" }).ShouldBe("Пересечение | Круглое");
    }

    [Test]
    public void FirstStringMatching_InstanceThenType()
    {
        var source = new TextSourceDef { Parts = [@"firstStringMatching:re:^\s*отоплени"] };
        Read(source, new FakeFacts { Instance = { ["A"] = "Прочее", ["B"] = "Отопление жилой части" }, Type = { ["C"] = "Отопление УК" } })
            .ShouldBe("Отопление жилой части");
        Read(source, new FakeFacts { Type = { ["C"] = "Отопление УК" } }).ShouldBe("Отопление УК");
        Read(source, new FakeFacts()).ShouldBe(string.Empty);
    }

    [Test]
    public void Fallback_WhenEmpty()
    {
        Read("primaryOrFull", new FakeFacts { FamilyName = "Смеситель" }).ShouldBe(" |  |  | смеситель |  |  | ");
        Read("primaryOrFull", new FakeFacts { FamilyName = "Смеситель", Instance = { ["ADSK_Наименование"] = "Унитаз" } }).ShouldBe("унитаз");
    }

    [Test]
    public void Normalize_LatinLookalikes()
    {
        var source = new TextSourceDef { Parts = ["systems"], Normalize = true };
        Read(source, new FakeFacts { SystemText = "B1 kнд" }).ShouldBe("В1 КНД");
    }
}
