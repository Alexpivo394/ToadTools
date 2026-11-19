using ClassifierCode.Engine.Library;
using Shouldly;

namespace ToadTools.Tests.ClassifierCode;

public class ProfileAnalysisTests
{
    [Test]
    public void Columns_FromModeRules()
    {
        var profile = Donskoy.Profile.Source;
        var vent = profile.Modes.First(m => m.Key == "vent");
        var disciplines = ProfileAnalysis.ReachableDisciplines(profile, vent);
        disciplines.ShouldContain("vent");
        disciplines.ShouldContain("smoke");
        disciplines.ShouldContain("heatSupply");
        disciplines.ShouldNotContain("electrical");

        var types = ProfileAnalysis.ReachableTypes(profile, "ios");
        types.ShouldContain("kip");
        types.ShouldContain("ductGroup");
        types.ShouldNotContain("uerm");
    }

    [Test]
    public void Gap_WhenReachableAndNoFallback()
    {
        var profile = Donskoy.Profile.Source;
        var fire = profile.Modes.First(m => m.Key == "fire");
        var gaps = ProfileAnalysis.Gaps(profile, fire);
        gaps.ShouldContain(("powder", "pipe"));
        // Изоляция в ПТ закрыта заменой на «Трубы».
        gaps.ShouldNotContain(("fire", "insulation"));
    }
}

public class MappingTabTests
{
    [Test]
    public void Rows_FilterByDiscipline_AndEditUpdatesProfile()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        var mapping = editor.Mapping;
        mapping.SelectedMode = mapping.Modes.First(m => m.Key == "water");
        mapping.DisciplineFilter = mapping.DisciplineFilters.First(f => f.Key == "storm");
        mapping.Rows.ShouldAllBe(r => r.Discipline == "storm");

        var row = mapping.Rows.First(r => r.Type == "funnel");
        row.DescriptionPreview.ShouldBe("Воронки и трапы");
        row.Position = "04.05.01.03.02";
        editor.Profile.Tables["ios"].Rows.First(r => r.Discipline == "storm" && r.Type == "funnel").Position.ShouldBe("04.05.01.03.02");
        editor.IsDirty.ShouldBeTrue();
        mapping.Suggest("04.05.01.03.0").ShouldContain("04.05.01.03.04");
    }

    [Test]
    public void AddFromGap_ClosesGap()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        var mapping = editor.Mapping;
        mapping.SelectedMode = mapping.Modes.First(m => m.Key == "fire");
        var gap = mapping.Gaps.First(g => g.Discipline == "powder" && g.Type == "pipe");
        mapping.AddFromGapCommand.Execute(gap);
        mapping.Rows.ShouldContain(r => r.Discipline == "powder" && r.Type == "pipe");
        mapping.Gaps.ShouldNotContain(g => g.Discipline == "powder" && g.Type == "pipe");
    }

    [Test]
    public void SharedTable_Note()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        editor.Mapping.SelectedMode = editor.Mapping.Modes.First(m => m.Key == "vent");
        editor.Mapping.SharedNote!.ShouldContain("ПТ");
    }
}

public class DictionariesTabTests
{
    [Test]
    public void RenameKey_UpdatesAllReferences()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        var tab = editor.Dictionaries;
        tab.SelectedKey = tab.Keys.First(k => k.Key == "fireCabinet");
        tab.RenameTo = "pozharnyShkaf";
        tab.RenameCommand.Execute(null);

        var p = editor.Profile;
        p.Types.ShouldContain(t => t.Key == "pozharnyShkaf");
        p.Tables["ios"].Rows.ShouldContain(r => r.Type == "pozharnyShkaf");
        p.Tables["ios"].Fallbacks.ShouldContainKey("pozharnyShkaf");
        p.Dictionaries["iosPre"].Entries.ShouldContain(e => e.Result.Type == "pozharnyShkaf");
        p.RuleSets["ios"].SelectMany(r => r.Steps).ShouldContain(s => s.OnlyIf.Contains("pozharnyShkaf"));
        p.RuleSets["ios"].First(r => r.Allow != null).Allow!.Values.ShouldContain("pozharnyShkaf");
        editor.Issues.ShouldBeEmpty();
    }

    [Test]
    public void DeleteKey_InUse_Warns()
    {
        using var temp = new TempFolder();
        var (editor, _, dialogs) = EditorSetup.Open(temp);
        var tab = editor.Dictionaries;
        tab.SelectedKey = tab.Keys.First(k => k.Key == "kip");
        dialogs.Answer = false;
        tab.DeleteCommand.Execute(null);
        dialogs.Asked.Last().ShouldContain("Словари");
        editor.Profile.Types.ShouldContain(t => t.Key == "kip");
    }

    [Test]
    public void Entries_ForKey_EditSetText()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        var tab = editor.Dictionaries;
        tab.SelectedKey = tab.Keys.First(k => k.Key == "kip");
        var entry = tab.Entries.First(e => e.DictionaryName == "iosTypes");
        var set = entry.Sets.First(s => s.Name == "kip");
        set.PatternsText.ShouldContain("манометр");

        set.PatternsText += "\nтахометр";
        editor.Profile.PatternSets["kip"].ShouldContain("тахометр");
        editor.IsDirty.ShouldBeTrue();
        editor.Issues.ShouldBeEmpty();
    }

    [Test]
    public void LiveCheck_ShowsMatches()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        var tab = editor.Dictionaries;
        tab.TestText = "Манометр показывающий";
        tab.Matches.ShouldContain(m => m.Contains("iosTypes") && m.Contains("КИП") && m.Contains("манометр"));
    }

    [Test]
    public void AddType_AndEntry()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        var tab = editor.Dictionaries;
        tab.AddTypeCommand.Execute(null);
        var key = tab.SelectedKey!;
        key.Title = "Тахометры";
        tab.AddEntryDictionary = "iosTypes";
        tab.AddEntryCommand.Execute(null);
        tab.Entries.Single().PatternsText = "тахометр";
        editor.Profile.Dictionaries["iosTypes"].Entries.Last().Result.Type.ShouldBe(key.Key);
        editor.Profile.Dictionaries["iosTypes"].Entries.Last().Patterns.ShouldBe(["тахометр"]);
    }
}

public class ModesTabTests
{
    [Test]
    public void EditTokens_AndAddMode()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        var tab = editor.Modes;
        tab.SelectedMode = tab.Modes.First(m => m.Key == "water");
        tab.SelectedMode.FileTokensText = "ВК, НВК ,К2К";
        editor.Profile.Modes.First(m => m.Key == "water").FileTokens.ShouldBe(["ВК", "НВК", "К2К"]);

        tab.AddCommand.Execute(null);
        var added = tab.SelectedMode!;
        added.Key.ShouldNotBe("water");
        editor.Profile.Modes.Count.ShouldBe(8);
        editor.Profile.Modes.Last().DisciplineRules.Count.ShouldBe(editor.Profile.Modes.First(m => m.Key == "water").DisciplineRules.Count);
        editor.Issues.ShouldBeEmpty();
    }
}
