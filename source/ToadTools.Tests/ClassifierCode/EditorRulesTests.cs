using ClassifierCode.Editor;
using ClassifierCode.Editor.ViewModels;
using ClassifierCode.Engine.Running;
using Shouldly;

namespace ToadTools.Tests.ClassifierCode;

public class RuleDescriberTests
{
    [Test]
    public void Describe_InheritHost()
    {
        var profile = Donskoy.Profile.Source;
        var rule = profile.Modes.First(m => m.Key == "fire").DisciplineRules[0];
        var text = string.Join(" ", RuleDescriber.Describe(rule, profile, isTypeStage: false));
        text.ShouldContain("Материалы изоляции труб");
        text.ShouldContain("изолируемого элемента");
        text.ShouldContain("дальше");
    }

    [Test]
    public void Describe_DictionaryWithAllow()
    {
        var profile = Donskoy.Profile.Source;
        var rule = profile.Modes.First(m => m.Key == "water").DisciplineRules.Last();
        var text = string.Join(" ", RuleDescriber.Describe(rule, profile, isTypeStage: false));
        text.ShouldContain("elementDiscipline");
        text.ShouldContain("только");
        text.ShouldContain("Комплекс работ по монтажу водопровода");
    }

    [Test]
    public void Describe_Exception()
    {
        var profile = Donskoy.Profile.Source;
        var text = string.Join(" ", RuleDescriber.Describe(profile.Exceptions[1], profile, isTypeStage: true));
        text.ShouldContain("не ");
        text.ShouldContain("ничего не дал");
        text.ShouldContain("пропустить");
    }
}

public class ExceptionsTabTests
{
    [Test]
    public void Exception_MoveUp_ChangesOrder()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        var tab = editor.Exceptions;
        tab.SelectedRule = tab.Rules[1];
        tab.MoveUpCommand.Execute(null);
        editor.Profile.Exceptions[0].Title.ShouldBe("Задание на отверстие");
        tab.SelectedRule!.Title.ShouldBe("Задание на отверстие");
        editor.IsDirty.ShouldBeTrue();
    }

    [Test]
    public void Condition_Parameter_BuildsInstanceOrTypeSource()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        var tab = editor.Exceptions;
        tab.AddCommand.Execute(null);
        var rule = tab.SelectedRule!;
        rule.Title = "По ГОСТ вручную";
        rule.ActionKind = "skip";
        rule.ActionValue = "заполняется вручную";
        rule.AddConditionCommand.Execute(null);
        var condition = rule.Conditions.Single();
        condition.Kind = ConditionKind.Parameter;
        condition.ParameterName = "ADSK_Марка";
        condition.Value = "ГОСТ*";

        editor.Profile.Texts["param:ADSK_Марка"].Parts.ShouldBe(["instanceOrType:ADSK_Марка"]);
        editor.Issues.ShouldBeEmpty();

        var facts = new FakeFacts { Category = "OST_PipeCurves", Type = { ["ADSK_Марка"] = "ГОСТ 3262" } };
        var result = new ClassificationEngine(editor.Compiled!, "water").Classify(facts);
        result.Status.ShouldBe(EngineStatus.Skipped);
        result.Reason.ShouldBe("заполняется вручную");
    }

    [Test]
    public void Condition_Category_AndNot()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        var tab = editor.Exceptions;
        tab.AddCommand.Execute(null);
        var rule = tab.SelectedRule!;
        rule.ActionKind = "position";
        rule.ActionValue = "999";
        rule.AddConditionCommand.Execute(null);
        var condition = rule.Conditions.Single();
        condition.Kind = ConditionKind.Category;
        condition.Value = "OST_Casework";
        condition.Not = true;
        editor.Profile.Exceptions.Last().When.Single().Category.ShouldBe(["OST_Casework"]);
        editor.Profile.Exceptions.Last().When.Single().Not.ShouldBeTrue();
        rule.Summary.ShouldContain("Шкафы");
    }

    [Test]
    public void ComplexRule_IsReadOnly()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        editor.Profile.Exceptions.Add(new() { Title = "Сложное", Steps = [new() { Dictionary = "iosTypes", Source = "fullName" }] });
        editor.Exceptions.Reload();
        editor.Exceptions.Rules.Last().IsSimple.ShouldBeFalse();
    }
}

public class LogicTabTests
{
    [Test]
    public void LogicJson_Invalid_NotApplied()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        var tab = editor.Logic;
        tab.SelectedMode = editor.Profile.Modes.First(m => m.Key == "water");
        var before = global::ClassifierCode.Engine.Profile.ProfileSerializer.Write(editor.Profile);

        tab.JsonText = "{oops";
        tab.ApplyCommand.Execute(null);
        tab.ApplyError.ShouldNotBeNullOrEmpty();

        tab.JsonText = tab.JsonText.Replace("{oops", "{\"typeRules\":[],\"disciplineRules\":[{\"steps\":[{\"dictionary\":\"нет\",\"source\":\"fullName\"}]}]}");
        tab.ApplyCommand.Execute(null);
        tab.ApplyError!.ShouldContain("нет");

        global::ClassifierCode.Engine.Profile.ProfileSerializer.Write(editor.Profile).ShouldBe(before);
        editor.IsDirty.ShouldBeFalse();
    }

    [Test]
    public void LogicJson_Valid_AppliedAndDirty()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        var tab = editor.Logic;
        tab.SelectedMode = editor.Profile.Modes.First(m => m.Key == "cooling");
        tab.JsonText.ShouldContain("\"disciplineRules\"");
        tab.JsonText = tab.JsonText.Replace("\"Изоляция — по изолируемому элементу\"", "\"Изоляция — от трубы\"");
        tab.ApplyCommand.Execute(null);
        tab.ApplyError.ShouldBeNull();
        editor.Profile.Modes.First(m => m.Key == "cooling").DisciplineRules[0].Title.ShouldBe("Изоляция — от трубы");
        editor.IsDirty.ShouldBeTrue();
        tab.DisciplineRulesDescription.ShouldContain(l => l.Contains("Изоляция — от трубы"));
    }
}
