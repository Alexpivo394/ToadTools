using ParamChecker.Core.Checking;
using ParamChecker.Core.Profiles;
using Shouldly;

namespace ParamChecker.Tests;

public class ConditionEvaluatorTests
{
    private static Func<string, ParamValue> Element(params (string Name, ParamValue Value)[] values) =>
        name => values.FirstOrDefault(v => v.Name == name).Value ?? ParamValue.Missing;

    private static ParamValue Text(string? text) => new(true, text);

    private static ParamValue Number(double number, string text) => new(true, text, number);

    [Test]
    public void CategoriesOrParameters_AcceptsElementOfOtherCategoryMatchingConditions()
    {
        var filter = new RuleFilter
        {
            Mode = SelectionMode.CategoriesOrParameters,
            Conditions = [Condition.Simple("Система", ConditionOperator.Equals, "П1")]
        };

        ConditionEvaluator.Accepts(filter, categoryMatches: false, Element(("Система", Text("П1")))).ShouldBeTrue();
        ConditionEvaluator.Accepts(filter, categoryMatches: true, Element(("Система", Text("В1")))).ShouldBeTrue();
        ConditionEvaluator.Accepts(filter, categoryMatches: false, Element(("Система", Text("В1")))).ShouldBeFalse();
    }

    [Test]
    public void CategoriesAndParameters_NeedsBoth()
    {
        var filter = new RuleFilter { Conditions = [Condition.Simple("Система", ConditionOperator.Equals, "П1")] };

        ConditionEvaluator.Accepts(filter, true, Element(("Система", Text("П1")))).ShouldBeTrue();
        ConditionEvaluator.Accepts(filter, false, Element(("Система", Text("П1")))).ShouldBeFalse();
        ConditionEvaluator.Accepts(filter, true, Element(("Система", Text("В1")))).ShouldBeFalse();
    }

    [Test]
    public void NoConditions_MatchesEverything_EvenWithOrLogic()
    {
        var filter = new RuleFilter { Mode = SelectionMode.ParametersOnly, Logic = ConditionLogic.Or };

        ConditionEvaluator.Accepts(filter, false, Element()).ShouldBeTrue();
    }

    [Test]
    public void CategoriesOrParameters_WithoutConditions_IsCategoriesOnly()
    {
        var filter = new RuleFilter { Mode = SelectionMode.CategoriesOrParameters };

        ConditionEvaluator.Accepts(filter, true, Element()).ShouldBeTrue();
        ConditionEvaluator.Accepts(filter, false, Element()).ShouldBeFalse();
    }

    [Test]
    public void IncompleteConditionsAreIgnored()
    {
        var filter = new RuleFilter
        {
            Mode = SelectionMode.ParametersOnly,
            Conditions = [Condition.Simple("", ConditionOperator.Equals, "x"), new Condition { Group = [] }]
        };

        ConditionEvaluator.Accepts(filter, false, Element()).ShouldBeTrue();
    }

    [Test]
    public void NumericComparison_UsesNumberEvenWhenTextHasUnits()
    {
        var length = Element(("Длина", Number(1200, "1200 мм")));

        ConditionEvaluator.Matches(Condition.Simple("Длина", ConditionOperator.GreaterThan, "1000"), length).ShouldBeTrue();
        ConditionEvaluator.Matches(Condition.Simple("Длина", ConditionOperator.LessThanOrEquals, "1200"), length).ShouldBeTrue();
        ConditionEvaluator.Matches(Condition.Simple("Длина", ConditionOperator.Equals, "1200"), length).ShouldBeTrue();
        ConditionEvaluator.Matches(Condition.Simple("Длина", ConditionOperator.LessThan, "1 000,5"), length).ShouldBeFalse();
    }

    [Test]
    public void NumericComparison_ParsesTextParameterThatStartsWithNumber()
    {
        var element = Element(("Ду", Text("150 мм")));

        ConditionEvaluator.Matches(Condition.Simple("Ду", ConditionOperator.GreaterThanOrEquals, "100"), element).ShouldBeTrue();
    }

    [Test]
    public void NumericComparison_FalseWhenNotANumber()
    {
        var element = Element(("Марка", Text("П1")));

        ConditionEvaluator.Matches(Condition.Simple("Марка", ConditionOperator.GreaterThan, "0"), element).ShouldBeFalse();
        ConditionEvaluator.Matches(Condition.Simple("Марка", ConditionOperator.LessThan, "0"), element).ShouldBeFalse();
    }

    [Test]
    public void TextOperators_IgnoreCase()
    {
        var element = Element(("Наименование", Text("Воздуховод круглый")));

        ConditionEvaluator.Matches(Condition.Simple("Наименование", ConditionOperator.Equals, "воздуховод КРУГЛЫЙ"), element).ShouldBeTrue();
        ConditionEvaluator.Matches(Condition.Simple("Наименование", ConditionOperator.Contains, "КРУГ"), element).ShouldBeTrue();
        ConditionEvaluator.Matches(Condition.Simple("Наименование", ConditionOperator.StartsWith, "возд"), element).ShouldBeTrue();
        ConditionEvaluator.Matches(Condition.Simple("Наименование", ConditionOperator.EndsWith, "лый"), element).ShouldBeTrue();
        ConditionEvaluator.Matches(Condition.Simple("Наименование", ConditionOperator.NotContains, "труба"), element).ShouldBeTrue();
    }

    [Test]
    public void ExistsAndFilled_AreDifferent()
    {
        var element = Element(("Марка", Text("")));

        ConditionEvaluator.Matches(Condition.Simple("Марка", ConditionOperator.Exists), element).ShouldBeTrue();
        ConditionEvaluator.Matches(Condition.Simple("Марка", ConditionOperator.IsFilled), element).ShouldBeFalse();
        ConditionEvaluator.Matches(Condition.Simple("Марка", ConditionOperator.IsEmpty), element).ShouldBeTrue();
        ConditionEvaluator.Matches(Condition.Simple("Нет такого", ConditionOperator.NotExists), element).ShouldBeTrue();
        ConditionEvaluator.Matches(Condition.Simple("Нет такого", ConditionOperator.IsEmpty), element).ShouldBeTrue();
    }

    [Test]
    public void MissingParameter_NotEqualsAnything()
    {
        ConditionEvaluator.Matches(Condition.Simple("Марка", ConditionOperator.Equals, ""), Element()).ShouldBeFalse();
        ConditionEvaluator.Matches(Condition.Simple("Марка", ConditionOperator.NotEquals, "П1"), Element()).ShouldBeTrue();
    }

    [Test]
    public void Group_IsAlwaysAnd_TopLevelOr()
    {
        var filter = new RuleFilter
        {
            Mode = SelectionMode.ParametersOnly,
            Logic = ConditionLogic.Or,
            Conditions =
            [
                Condition.Simple("Наименование", ConditionOperator.Equals, "Воздуховод"),
                Condition.AllOf(Condition.Simple("Система", ConditionOperator.Equals, "П1"),
                    Condition.Simple("Марка", ConditionOperator.Equals, "ВД"))
            ]
        };

        ConditionEvaluator.Accepts(filter, false, Element(("Система", Text("П1")), ("Марка", Text("ВД")))).ShouldBeTrue();
        ConditionEvaluator.Accepts(filter, false, Element(("Система", Text("П1")), ("Марка", Text("ВК")))).ShouldBeFalse();
        ConditionEvaluator.Accepts(filter, false, Element(("Наименование", Text("Воздуховод")))).ShouldBeTrue();
    }

    [Test]
    [Arguments("1200", 1200d)]
    [Arguments("1 200,5", 1200.5d)]
    [Arguments("-3.5", -3.5d)]
    [Arguments(" 12 ", 12d)]
    public void ParsesNumbers(string text, double expected)
    {
        ValueParser.TryParseNumber(text, out var value).ShouldBeTrue();
        value.ShouldBe(expected);
    }

    [Test]
    [Arguments("П1")]
    [Arguments("1200 мм")]
    [Arguments("")]
    [Arguments("-")]
    public void RejectsNonNumbers(string text) => ValueParser.TryParseNumber(text, out _).ShouldBeFalse();

    [Test]
    public void PrefixOnly_TakesLeadingNumber()
    {
        ValueParser.TryParseNumber("1200 мм", out var value, prefixOnly: true).ShouldBeTrue();
        value.ShouldBe(1200);
    }
}
