using ClassifierCode.Editor.ViewModels;
using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Import;
using ClassifierCode.Engine.Profile;
using Shouldly;

namespace ToadTools.Tests.ClassifierCode;

public class ProfileEditorViewModelTests
{
    [Test]
    public void Edit_SetsDirty_AndRecompiles()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        editor.IsDirty.ShouldBeFalse();
        editor.Issues.ShouldBeEmpty();

        editor.General.Outputs.First(o => o.Field == "code").Parameter = "";
        editor.IsDirty.ShouldBeTrue();
        editor.Issues.ShouldContain(i => i.Path.StartsWith("Параметры"));
        editor.SaveCommand.CanExecute(null).ShouldBeTrue();
    }

    [Test]
    public void ReadOnly_SaveDisabled_SaveAsCopyWorks()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp, builtin: true);
        editor.IsReadOnly.ShouldBeTrue();
        editor.General.Name = "Мой объект";
        editor.SaveCommand.CanExecute(null).ShouldBeFalse();

        editor.SaveAsCopyCommand.Execute(null);
        editor.IsReadOnly.ShouldBeFalse();
        editor.IsDirty.ShouldBeFalse();
        editor.SavedProfileId.ShouldNotBeNull();
        File.ReadAllText(editor.Entry.Path!).ShouldContain("Мой объект");
    }

    [Test]
    public void Save_WritesFile()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        editor.General.Description = "Новое описание";
        editor.SaveCommand.Execute(null);
        editor.IsDirty.ShouldBeFalse();
        ProfileSerializer.Read(File.ReadAllText(editor.Entry.Path!)).Description.ShouldBe("Новое описание");
    }

    [Test]
    public void DefaultTabs_FourAndCheckFirst()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        editor.ShowAll.ShouldBeFalse();
        editor.VisibleTabs.Select(t => t.Title).ShouldBe(["Проверка", "Слова и шифры", "Сопоставление", "Исключения"]);
        editor.SelectedTab.ShouldBe(editor.VisibleTabs[0]);
        editor.VisibleTabs.ShouldAllBe(t => t.Hint.Length > 0);
    }

    [Test]
    public void ShowAll_AddsRest_AndPersists()
    {
        using var temp = new TempFolder();
        var settings = temp.File("settings.json");
        var (editor, _, _) = EditorSetup.Open(temp, settingsPath: settings);
        editor.ShowAll = true;
        editor.VisibleTabs.Select(t => t.Title).ShouldBe(["Проверка", "Слова и шифры", "Сопоставление", "Исключения", "Общее", "Классификатор", "Режимы", "Логика"]);
        global::ClassifierCode.Engine.Library.ClassifierSettings.Load(settings).ShowAllEditorTabs.ShouldBeTrue();

        var (again, _, _) = EditorSetup.Open(temp, settingsPath: settings);
        again.ShowAll.ShouldBeTrue();
        again.VisibleTabs.Count.ShouldBe(8);

        again.SelectedTab = again.VisibleTabs.First(t => t.Content == again.Logic);
        again.ShowAll = false;
        again.SelectedTab!.Content.ShouldBe(again.Check); // скрытая вкладка не остаётся выбранной
    }

    [Test]
    public void GoToIssue_HiddenTab_ShowsAll()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        editor.GoToIssue(new ProfileIssue("Параметры", "x"));
        editor.ShowAll.ShouldBeTrue();
        editor.SelectedTab!.Content.ShouldBe(editor.General);
        editor.VisibleTabs.ShouldContain(editor.SelectedTab);
    }

    [Test]
    public void GoToIssue_HiddenTab_ShowsForSessionOnly()
    {
        using var temp = new TempFolder();
        var settings = temp.File("settings.json");
        var (editor, _, _) = EditorSetup.Open(temp, settingsPath: settings);
        editor.GoToIssue(new ProfileIssue("Параметры", "x"));
        editor.ShowAll.ShouldBeTrue();
        global::ClassifierCode.Engine.Library.ClassifierSettings.Load(settings).ShowAllEditorTabs.ShouldBeFalse();
    }

    /// <summary>Окно запуска и редактор делят один экземпляр настроек: сохранение окна не затирает «Все настройки».</summary>
    [Test]
    public void ShowAll_SharedSettings_NotOverwritten()
    {
        using var temp = new TempFolder();
        var path = temp.File("settings.json");
        var shared = global::ClassifierCode.Engine.Library.ClassifierSettings.Load(path);
        var (editor, _, _) = EditorSetup.Open(temp, settingsPath: path, settings: shared);
        editor.ShowAll = true;
        shared.Save(path); // окно запуска сохраняет свои настройки после «Заполнить»
        global::ClassifierCode.Engine.Library.ClassifierSettings.Load(path).ShowAllEditorTabs.ShouldBeTrue();
    }

    [Test]
    public void GoToIssue_SwitchesTab()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        editor.GoToIssue(new ProfileIssue("Таблицы › ios › строка 3", "x"));
        editor.SelectedTab.ShouldBe(editor.Tabs.First(t => t.Content == editor.Mapping));
        editor.GoToIssue(new ProfileIssue("Классификатор", "x"));
        editor.SelectedTab!.Content.ShouldBe(editor.Positions);
        editor.GoToIssue(new ProfileIssue("Исключения › Не учитывать", "x"));
        editor.SelectedTab!.Content.ShouldBe(editor.Exceptions);
    }

    [Test]
    public void ConfirmClose_AsksWhenDirty()
    {
        using var temp = new TempFolder();
        var (editor, _, dialogs) = EditorSetup.Open(temp);
        editor.ConfirmClose().ShouldBeTrue();
        dialogs.Asked.ShouldBeEmpty();

        editor.General.Author = "Я";
        dialogs.Answer = null; // «Отмена»
        editor.ConfirmClose().ShouldBeFalse();
        dialogs.Answer = false; // «Не сохранять»
        editor.ConfirmClose().ShouldBeTrue();
        dialogs.Answer = true; // «Сохранить»
        editor.ConfirmClose().ShouldBeTrue();
        editor.IsDirty.ShouldBeFalse();
    }

    [Test]
    public void General_Categories_And_FileNamePatterns()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        var generic = editor.General.Categories.First(c => c.Name == "OST_GenericModel");
        generic.IsChecked.ShouldBeFalse();
        generic.IsChecked = true;
        editor.Profile.Categories.ShouldContain("OST_GenericModel");

        editor.General.FileNamePatternsText = "ШФТ*\n\n  DON*  ";
        editor.Profile.FileNamePatterns.ShouldBe(["ШФТ*", "DON*"]);
    }

    [Test]
    public void General_AddExtraOutput()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        editor.General.AddOutputCommand.Execute(null);
        var extra = editor.General.Outputs.Last();
        extra.Field = "Ед";
        extra.Parameter = "Мой_Код";
        editor.Profile.Outputs.Last().Field.ShouldBe("Ед");
        editor.Profile.Outputs.Last().Parameter.ShouldBe("Мой_Код");
        editor.General.ParameterNames.ShouldContain("Мой_Код");
    }
}

public class PositionsTabTests
{
    [Test]
    public void Search_FiltersByCodeAndDescription()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        editor.Positions.SearchText = "04.05.01.09.0";
        editor.Positions.Visible.ShouldAllBe(p => p.Code.StartsWith("04.05.01.09.0"));
        editor.Positions.SearchText = "огнезащ";
        editor.Positions.Visible.ShouldContain(p => p.Description.Contains("огнезащ", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public void Import_Replace_And_Append()
    {
        using var temp = new TempFolder();
        var (editor, host, dialogs) = EditorSetup.Open(temp);
        host.Xlsx = [["Код", "Наименование"], ["01", "Оборудование"], ["", "пусто"], ["01", "повтор"], ["02", "Трубы"]];

        editor.Positions.ImportExcelCommand.Execute(null);
        var preview = editor.Positions.Preview!;
        preview.CodeColumn.ShouldBe(0);
        preview.DescriptionColumn.ShouldBe(1);
        preview.FirstRow.ShouldBe(1);
        preview.Replace = false;
        editor.Positions.ApplyImportCommand.Execute(null);
        editor.Profile.Positions.ShouldContain(p => p.Code == "02");
        editor.Profile.Positions.Count(p => p.Code == "01").ShouldBe(1);
        editor.Profile.Positions.ShouldContain(p => p.Code == "04.05.01.01.01");
        dialogs.Shown.Last().ShouldContain("пропущено 2");
    }

    [Test]
    public void DeletePosition_InUse_Warns()
    {
        using var temp = new TempFolder();
        var (editor, _, dialogs) = EditorSetup.Open(temp);
        var row = editor.Positions.Visible.First(p => p.Code == "04.05.01.01.03");
        dialogs.Answer = false;
        editor.Positions.DeleteCommand.Execute(row);
        dialogs.Asked.Last().ShouldContain("Таблицы");
        editor.Profile.Positions.ShouldContain(p => p.Code == "04.05.01.01.03");

        dialogs.Answer = true;
        editor.Positions.DeleteCommand.Execute(row);
        editor.Profile.Positions.ShouldNotContain(p => p.Code == "04.05.01.01.03");
        editor.Issues.ShouldContain(i => i.Message.Contains("04.05.01.01.03"));
    }

    [Test]
    public void Paste_FromClipboard()
    {
        using var temp = new TempFolder();
        var (editor, host, _) = EditorSetup.Open(temp);
        host.Clipboard = "А1\tОдин\r\nА2\t\"Два, с запятой\"\r\n";
        editor.Positions.PasteCommand.Execute(null);
        editor.Positions.Preview!.FirstRow.ShouldBe(0);
        editor.Positions.ApplyImportCommand.Execute(null);
        editor.Profile.Positions.ShouldContain(p => p.Code == "А2" && p.Description == "Два, с запятой");
    }
}

public class PositionsImportTests
{
    [Test]
    public void Import_SkipsEmptyAndDuplicateCodes()
    {
        string[][] rows = [["01", "А"], ["", "Б"], ["01", "В"], ["  02 ", " Г "]];
        var (positions, skipped) = PositionsImport.FromRows(rows, 0, 1, 0, new Dictionary<string, int>());
        positions.Select(p => p.Code).ShouldBe(["01", "02"]);
        positions[1].Description.ShouldBe("Г");
        skipped.Count.ShouldBe(2);
    }

    [Test]
    public void Import_ExtraColumns()
    {
        string[][] rows = [["01", "А", "шт"]];
        var (positions, _) = PositionsImport.FromRows(rows, 0, 1, 0, new Dictionary<string, int> { ["Ед"] = 2 });
        positions[0].Extra["Ед"].ShouldBe("шт");
    }

    [Test]
    public void ParseTsv_QuotedCells()
    {
        var rows = PositionsImport.ParseTsv("a\tb\r\n\"c\td\"\t\"e \"\"q\"\"\"\n");
        rows.Count.ShouldBe(2);
        rows[1].ShouldBe(["c\td", "e \"q\""]);
    }

    [Test]
    public void GuessColumns_ByHeader()
    {
        var (code, description, first) = PositionsImport.Guess([["№", "Код классификатора", "Описание"], ["1", "04.05", "Раздел"]]);
        code.ShouldBe(1);
        description.ShouldBe(2);
        first.ShouldBe(1);
    }
}
