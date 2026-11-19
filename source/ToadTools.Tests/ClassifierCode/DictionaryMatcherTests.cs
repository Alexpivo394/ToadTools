using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Matching;
using ClassifierCode.Engine.Patterns;
using ClassifierCode.Engine.Profile;
using Shouldly;

namespace ToadTools.Tests.ClassifierCode;

public class DictionaryMatcherTests
{
    private static ResultDef T(string type) => new() { Type = type };

    private static ProfileRuntime Runtime() => new(new ClassifierProfile
    {
        Texts = { ["fullName"] = new TextSourceDef { Parts = ["type.family"], Lowercase = true } },
        Dictionaries =
        {
            ["types"] = new DictionaryDef
            {
                Mode = "earliest",
                Entries =
                [
                    new DictionaryEntryDef { Result = T("armature"), Patterns = ["клапан", "=кран*"] },
                    new DictionaryEntryDef { Result = T("equipment"), Patterns = ["вентилятор", "установк"] },
                    new DictionaryEntryDef { Result = T("pipeFittingSame"), Patterns = ["клапан"] },
                    new DictionaryEntryDef
                    {
                        Result = new ResultDef { Pipe = T("pipeGroup"), Air = T("ductGroup") },
                        Patterns = ["хомут"]
                    },
                    new DictionaryEntryDef
                    {
                        Result = T("coolant"), Patterns = ["гликол"],
                        When = [new ConditionDef { Not = true, Category = ["OST_PipeAccessory", "OST_DuctAccessory"] }]
                    }
                ]
            },
            ["firstDict"] = new DictionaryDef
            {
                Mode = "first",
                Entries =
                [
                    new DictionaryEntryDef { Result = T("b"), Patterns = ["бета"] },
                    new DictionaryEntryDef { Result = T("a"), Patterns = ["альфа"] }
                ]
            },
            ["ss"] = new DictionaryDef
            {
                Mode = "lastToken", Normalize = true, Separators = @"[-_.,\s()]+",
                Entries =
                [
                    new DictionaryEntryDef { Result = new ResultDef { Discipline = "ss10" }, Patterns = ["АУПС", "АПС"] },
                    new DictionaryEntryDef { Result = new ResultDef { Skip = "подсистема {match} заполняется вручную" }, Patterns = ["СКЗ", "АГПТ"] }
                ]
            },
            ["slow"] = new DictionaryDef { Entries = [new DictionaryEntryDef { Result = T("x"), Patterns = ["re:(a+)+$"] }] }
        }
    });

    private static MatchContext Ctx(NetworkDomain domain = NetworkDomain.Pipe, string category = "") =>
        new(new FakeFacts { Category = category }, "mode", domain);

    private static string? Type(string dictionary, string text, MatchContext? ctx = null)
    {
        var runtime = Runtime();
        return DictionaryMatcher.Match(runtime.Dictionary(dictionary), text, ctx ?? Ctx(), runtime)?.Result.Type;
    }

    [Test]
    public void Earliest_FirstPositionWins()
    {
        Type("types", "клапан обратный для вентилятора").ShouldBe("armature");
        Type("types", "осевая вентиляторная установка с клапаном").ShouldBe("equipment");
    }

    [Test]
    public void Earliest_TieByOrder()
    {
        Type("types", "клапан").ShouldBe("armature");
    }

    [Test]
    public void First_RespectsOrder()
    {
        Type("firstDict", "альфа и бета").ShouldBe("b");
    }

    [Test]
    public void LastToken_FromEnd()
    {
        var runtime = Runtime();
        var hit = DictionaryMatcher.Match(runtime.Dictionary("ss"), "ШФТ-СОБ-СКЗ-АУПС", Ctx(), runtime);
        hit!.Result.Discipline.ShouldBe("ss10");
        hit.Text.ShouldBe("АУПС");

        hit = DictionaryMatcher.Match(runtime.Dictionary("ss"), "aups_СКЗ", Ctx(), runtime);
        hit!.Result.Skip.ShouldBe("подсистема {match} заполняется вручную");
        hit.Text.ShouldBe("СКЗ");

        DictionaryMatcher.Match(runtime.Dictionary("ss"), "СКЗСОБ", Ctx(), runtime).ShouldBeNull();
    }

    [Test]
    public void ByDomain_AirAndPipe()
    {
        Type("types", "хомут", Ctx(NetworkDomain.Air)).ShouldBe("ductGroup");
        Type("types", "хомут", Ctx(NetworkDomain.Pipe)).ShouldBe("pipeGroup");
        Type("types", "хомут", Ctx(NetworkDomain.Other)).ShouldBe("pipeGroup");
    }

    [Test]
    public void EntryWhen_NotCategory()
    {
        Type("types", "гликоль", Ctx(category: "OST_PipeAccessory")).ShouldBeNull();
        Type("types", "гликоль", Ctx(category: "OST_MechanicalEquipment")).ShouldBe("coolant");
    }

    [Test]
    public void Timeout_Throws_NotSilentNoMatch()
    {
        // Тихое «не совпало» дало бы другой код (под нагрузкой — случайно), поэтому таймаут — ошибка элемента.
        var error = Should.Throw<PatternTimeoutException>(() => Type("slow", new string('a', 40) + "!"));
        error.Message.ShouldContain("re:(a+)+$");
    }
}
