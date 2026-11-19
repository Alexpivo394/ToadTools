using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Profile;
using ClassifierCode.Engine.Running;
using Shouldly;

namespace ToadTools.Tests.ClassifierCode;

public class ProfileCompilerTests
{
    private static ClassifierProfile Minimal() => new()
    {
        Outputs = [new OutputField { Field = "code", Parameter = "Код" }],
        Texts = { ["name"] = new TextSourceDef { Parts = ["instance:Имя"] } },
        Types = [new KeyTitle { Key = "kip", Title = "КИП" }],
        Disciplines = [new KeyTitle { Key = "water", Title = "Вода" }],
        Positions = [new Position { Code = "01", Description = "КИП" }],
        Dictionaries = { ["d"] = new DictionaryDef { Entries = [new DictionaryEntryDef { Result = new ResultDef { Type = "kip" }, Patterns = ["манометр"] }] } },
        RuleSets = { ["t"] = [new RuleDef { Steps = [new StepDef { Dictionary = "d", Source = "name" }] }] },
        Tables = { ["m"] = new TableDef { Rows = [new TableRow { Discipline = "water", Type = "kip", Position = "01" }] } },
        Modes = [new ModeDef { Key = "m", Title = "М", TypeRules = "t", Table = "m", DefaultDiscipline = "water", FileTokens = ["ВК"] }]
    };

    private static IReadOnlyList<ProfileIssue> Issues(ClassifierProfile profile) => ProfileCompiler.Compile(profile).Issues;

    [Test]
    public void Compile_Minimal_NoIssues()
    {
        var (compiled, issues) = ProfileCompiler.Compile(Minimal());
        issues.ShouldBeEmpty();
        compiled.ShouldNotBeNull();
    }

    [Test]
    public void Compile_DuplicateKeys()
    {
        var profile = Minimal();
        profile.Types.Add(new KeyTitle { Key = "kip", Title = "Ещё" });
        profile.Positions.Add(new Position { Code = "01" });
        var issues = Issues(profile);
        issues.ShouldContain(i => i.Path.StartsWith("Типы") && i.Message.Contains("kip"));
        issues.ShouldContain(i => i.Path.StartsWith("Классификатор") && i.Message.Contains("01"));
        ProfileCompiler.Compile(profile).Profile.ShouldBeNull();
    }

    [Test]
    public void Compile_UnknownReference_PathInMessage()
    {
        var profile = Minimal();
        profile.Tables["m"].Rows.Add(new TableRow { Discipline = "water", Type = "нет", Position = "01" });
        profile.RuleSets["t"][0].Steps.Add(new StepDef { Dictionary = "нетСловаря", Source = "name" });
        profile.Modes[0].DefaultDiscipline = "нетРаздела";
        var issues = Issues(profile);
        issues.ShouldContain(i => i.Path == "Таблицы › m › строка 2" && i.Message.Contains("нет"));
        issues.ShouldContain(i => i.Message.Contains("нетСловаря"));
        issues.ShouldContain(i => i.Path.StartsWith("Режимы › m") && i.Message.Contains("нетРаздела"));
    }

    [Test]
    public void Compile_BrokenPattern_Path()
    {
        var profile = Minimal();
        profile.Dictionaries["d"].Entries[0].Patterns.Add("re:(");
        Issues(profile).ShouldContain(i => i.Path == "Словари › d › запись 1" && i.Message.Contains("re:("));
    }

    [Test]
    public void Compile_UnknownSourceAndPart()
    {
        var profile = Minimal();
        profile.Texts["bad"] = new TextSourceDef { Parts = ["nope"] };
        profile.RuleSets["t"][0].Steps[0].Source = "нетИсточника";
        var issues = Issues(profile);
        issues.ShouldContain(i => i.Path.StartsWith("Тексты › bad") && i.Message.Contains("nope"));
        issues.ShouldContain(i => i.Message.Contains("нетИсточника"));
    }

    [Test]
    public void Compile_NoCodeOutput()
    {
        var profile = Minimal();
        profile.Outputs.Clear();
        Issues(profile).ShouldContain(i => i.Path.StartsWith("Параметры"));
    }

    [Test]
    public void DetectMode_LastTokenWins()
    {
        var profile = Minimal();
        profile.Modes.Add(new ModeDef { Key = "fire", Title = "ПТ", TypeRules = "t", Table = "m", FileTokens = ["ПТ", "РТ"] });
        var compiled = ProfileCompiler.Compile(profile).Profile!;

        ModeDetector.DetectMode(compiled, "ВК-ШФТ-СОБ-1-ПТ").ShouldBe("fire");
        ModeDetector.DetectMode(compiled, "ПТ_ВК").ShouldBe("m");
        ModeDetector.DetectMode(compiled, "DON_PT_SOB").ShouldBe("fire");
        ModeDetector.DetectMode(compiled, "Проект1").ShouldBeNull();
    }

    [Test]
    public void MatchesProfile_ByFileNamePattern()
    {
        var profile = Minimal();
        profile.FileNamePatterns = ["ШФТ*"];
        ModeDetector.MatchesProfile(profile, "ШФТ-СОБ-1-ПТ").ShouldBeTrue();
        ModeDetector.MatchesProfile(profile, "DON_OVK").ShouldBeFalse();
    }
}
