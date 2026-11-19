using ClassifierCode.Engine.Facts;
using ClassifierCode.Models;
using Shouldly;

namespace ToadTools.Tests.ClassifierCode;

public class CheckTabTests
{
    private static FakeFacts Accessory(long id, string name, string system = "Т1") => new()
    {
        Id = id, Category = "OST_PipeAccessory", Domain = NetworkDomain.Pipe, SystemText = system, Instance = { ["ADSK_Наименование"] = name }
    };

    [Test]
    public void Inspect_ShowsTraceAndFacts()
    {
        using var temp = new TempFolder();
        var (editor, host, _) = EditorSetup.Open(temp);
        host.Elements[1] = Accessory(1, "Манометр");
        host.Selection.Add(1);
        var check = editor.Check;
        check.SelectedMode = editor.Profile.Modes.First(m => m.Key == "heating");
        check.InspectSelectionCommand.Execute(null);

        var item = check.Inspected.Single();
        item.ResultText.ShouldContain("04.05.01.07.06");
        item.Trace.ShouldContain(t => t.Stage == "Тип" && t.Text.Contains("манометр"));
        item.Facts.ShouldContain(f => f.Name == "Категория" && f.Value.Contains("Арматура трубопроводов"));
        item.Facts.ShouldContain(f => f.Name == "primaryName" && f.Value == "манометр");
    }

    [Test]
    public void DryRun_OnlyChangesFilter_AndGroups()
    {
        using var temp = new TempFolder();
        var (editor, host, _) = EditorSetup.Open(temp);
        host.Elements[1] = Accessory(1, "Манометр");
        host.Elements[2] = Accessory(2, "Клапан");
        host.Elements[3] = Accessory(3, "Нечто", system: "");
        host.Elements[4] = new FakeFacts { Id = 4, Category = "OST_GenericModel", Instance = { ["ADSK_Наименование"] = "Нечто" } };
        host.Current[1] = "04.05.01.07.06";
        var check = editor.Check;
        check.SelectedMode = editor.Profile.Modes.First(m => m.Key == "heating");
        check.Scope = RunScope.Model;
        check.DryRunCommand.Execute(null);

        check.Rows.Count.ShouldBe(4);
        check.OnlyChanges = true;
        check.VisibleRows.Select(r => r.Id).ShouldBe([2, 3], ignoreOrder: true);
        check.OnlyChanges = false;
        check.OnlyUnclassified = true;
        check.VisibleRows.Select(r => r.Id).ShouldBe([4]);
        check.Groups.ShouldContain(g => g.Code == "04.05.01.07.05" && g.Count == 2);

        check.SelectInModelCommand.Execute(null);
        host.Selected.ShouldBe([4]);
    }

    [Test]
    public void SelectRow_InspectsIt()
    {
        using var temp = new TempFolder();
        var (editor, host, _) = EditorSetup.Open(temp);
        host.Elements[7] = Accessory(7, "Клапан");
        editor.Check.SelectedMode = editor.Profile.Modes.First(m => m.Key == "heating");
        editor.Check.DryRunCommand.Execute(null);
        editor.Check.SelectedRow = editor.Check.Rows.Single();
        editor.Check.Inspected.Single().Id.ShouldBe(7);
    }

    [Test]
    public void Check_UsesUnsavedEdits()
    {
        using var temp = new TempFolder();
        var (editor, host, _) = EditorSetup.Open(temp);
        host.Elements[1] = Accessory(1, "Тахометр");
        host.Selection.Add(1);
        editor.Check.SelectedMode = editor.Profile.Modes.First(m => m.Key == "heating");
        editor.Check.InspectSelectionCommand.Execute(null);
        editor.Check.Inspected.Single().ResultText.ShouldContain("04.05.01.07.05"); // арматура без признаков

        var tab = editor.Dictionaries;
        tab.SelectedKey = tab.Keys.First(k => k.Key == "kip");
        tab.Entries.First(e => e.DictionaryName == "iosTypes").Sets.First(s => s.Name == "kip").PatternsText += "\nтахометр";

        editor.Check.Inspected.Single().ResultText.ShouldContain("04.05.01.07.06");
    }

    [Test]
    public void DryRun_BrokenProfile_Refuses()
    {
        using var temp = new TempFolder();
        var (editor, host, dialogs) = EditorSetup.Open(temp);
        host.Elements[1] = Accessory(1, "Клапан");
        editor.General.Outputs.First(o => o.Field == "code").Parameter = "";
        editor.Check.DryRunCommand.Execute(null);
        editor.Check.Rows.ShouldBeEmpty();
        dialogs.Shown.Last().ShouldContain("ошиб");
    }
}
