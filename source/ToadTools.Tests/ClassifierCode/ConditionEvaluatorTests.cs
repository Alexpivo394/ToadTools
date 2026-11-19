using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Matching;
using ClassifierCode.Engine.Profile;
using Shouldly;

namespace ToadTools.Tests.ClassifierCode;

public class ConditionEvaluatorTests
{
    private static ProfileRuntime Runtime() => new(new ClassifierProfile
    {
        Texts =
        {
            ["primaryName"] = new TextSourceDef { Parts = ["instance:ADSK_Наименование"], Lowercase = true },
            ["familyType"] = new TextSourceDef { Parts = ["familyType"] }
        },
        Dictionaries =
        {
            ["eom"] = new DictionaryDef { Entries = [new DictionaryEntryDef { Result = new ResultDef { Type = "lighting" }, Patterns = ["светильник"] }] }
        }
    });

    private static bool Eval(MatchContext ctx, params ConditionDef[] conditions)
    {
        var runtime = Runtime();
        return ConditionEvaluator.All(runtime.Conditions(conditions), ctx, runtime);
    }

    private static MatchContext Ctx(FakeFacts facts, string? type = null) =>
        new(facts, "electrical", NetworkDomain.Other) { TypeKey = type };

    [Test]
    public void Not_Category()
    {
        var ctx = Ctx(new FakeFacts { Category = "OST_GenericModel" });
        Eval(ctx, new ConditionDef { Not = true, Category = ["OST_ElectricalEquipment"] }).ShouldBeTrue();
        Eval(ctx, new ConditionDef { Category = ["OST_ElectricalEquipment"] }).ShouldBeFalse();
    }

    [Test]
    public void And_TwoConditions()
    {
        var ctx = Ctx(new FakeFacts { Category = "OST_GenericModel", FamilyName = "Пересечение_Стена", TypeName = "Круглое" });
        Eval(ctx,
            new ConditionDef { Category = ["OST_GenericModel"] },
            new ConditionDef { Source = "familyType", Matches = "пересечени" }).ShouldBeTrue();
        Eval(ctx,
            new ConditionDef { Category = ["OST_GenericModel"] },
            new ConditionDef { Source = "familyType", Matches = "отверсти" }).ShouldBeFalse();
    }

    [Test]
    public void Dictionary_GivesNothing()
    {
        var plain = Ctx(new FakeFacts { Instance = { ["ADSK_Наименование"] = "Заглушка" } });
        var light = Ctx(new FakeFacts { Instance = { ["ADSK_Наименование"] = "Светильник" } });
        var nothing = new ConditionDef { Source = "primaryName", Dictionary = "eom", GivesNothing = true };
        var gives = new ConditionDef { Source = "primaryName", Dictionary = "eom", Gives = ["lighting"] };

        Eval(plain, nothing).ShouldBeTrue();
        Eval(light, nothing).ShouldBeFalse();
        Eval(light, gives).ShouldBeTrue();
    }

    [Test]
    public void StepSource()
    {
        var ctx = Ctx(new FakeFacts()) with { StepSource = "маты ei60" };
        Eval(ctx, new ConditionDef { Source = "$source", Matches = @"re:\bei\s?\d" }).ShouldBeTrue();
    }

    [Test]
    public void Type_And_Mode()
    {
        var ctx = Ctx(new FakeFacts(), "insulation");
        Eval(ctx, new ConditionDef { Type = ["insulation"] }, new ConditionDef { Mode = ["electrical"] }).ShouldBeTrue();
        Eval(ctx, new ConditionDef { Mode = ["vent"] }).ShouldBeFalse();
    }
}
