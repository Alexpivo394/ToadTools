using ParamChecker.Core;
using ParamChecker.Core.Profiles;
using Shouldly;

namespace ParamChecker.Tests;

public class RunValidatorTests
{
    private static CheckProfile Valid() => new()
    {
        Name = "ОВ",
        Models = [new ModelSource { Path = "RSN://rs/Дом_ОВ.rvt" }],
        Rules = [new CheckRule { Title = "Трубы", Filter = new RuleFilter { Categories = ["OST_PipeCurves"] }, Parameters = ["Марка"] }]
    };

    [Test]
    public void ValidProfile_HasNoProblems() => RunValidator.Problems([Valid()]).ShouldBeEmpty();

    [Test]
    public void NoProfiles() => RunValidator.Problems([]).ShouldHaveSingleItem().ShouldContain("Отметьте");

    [Test]
    public void ReportsAllProblemsAtOnce()
    {
        var profile = Valid();
        profile.Models.Add(new ModelSource { Path = "rsn://rs/Дом_ОВ.rvt" });
        profile.Models.Add(new ModelSource());
        profile.Rules.Add(new CheckRule { Title = "Пустое" });
        profile.Rules.Add(new CheckRule { Filter = new RuleFilter { Mode = SelectionMode.ParametersOnly }, Parameters = ["Марка"] });

        var problems = RunValidator.Problems([profile]);

        problems.ShouldContain(p => p.Contains("Дом_ОВ") && p.Contains("несколько раз"));
        problems.ShouldContain(p => p.Contains("не указан путь"));
        problems.ShouldContain(p => p.Contains("«Пустое»") && p.Contains("параметры"));
        problems.ShouldContain(p => p.Contains("«Пустое»") && p.Contains("категории"));
        problems.ShouldContain(p => p.Contains("№3") && p.Contains("условий нет"));
    }

    [Test]
    public void ActiveModelCheck_DoesNotNeedModels()
    {
        var profile = Valid();
        profile.Models.Clear();

        RunValidator.Problems([profile], needsModels: false).ShouldBeEmpty();
        RunValidator.Problems([profile]).ShouldHaveSingleItem().ShouldContain("нет моделей");
    }
}
