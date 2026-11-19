using ClassifierCode.Editor.ViewModels;
using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Library;
using ClassifierCode.Engine.Profile;
using Shouldly;

namespace ToadTools.Tests.ClassifierCode;

/// <summary>Находки финального ревью: каждая воспроизведена тестом до исправления.</summary>
public class ReviewFixesTests
{
    [Test]
    public void SaveAsCopy_ThenEditEveryTab_EditsReachFile()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp, builtin: true);
        editor.General.Name = "Объект Б";
        editor.SaveAsCopyCommand.Execute(null);

        editor.Positions.Visible.First(p => p.Code == "04.05.01.01.01").Description = "Оборудование водопровода";
        editor.Modes.SelectedMode = editor.Modes.Modes.First(m => m.Key == "water");
        editor.Modes.SelectedMode.Title = "ВК объекта";
        editor.Dictionaries.SelectedKey = editor.Dictionaries.Keys.First(k => k.Key == "kip");
        editor.Dictionaries.SelectedKey.Title = "КИП объекта";
        editor.Exceptions.SelectedRule = editor.Exceptions.Rules[0];
        editor.Exceptions.SelectedRule.Title = "Не учитывать (объект)";
        editor.Exceptions.MoveDownCommand.Execute(null);
        editor.Mapping.SelectedMode = editor.Mapping.Modes.First(m => m.Key == "water");
        editor.Mapping.Rows.First(r => r.Type == "armature" && r.Discipline == "water").Position = "04.05.01.01.04";
        editor.Logic.SelectedMode = editor.Profile.Modes.First(m => m.Key == "cooling");
        editor.Logic.JsonText = editor.Logic.JsonText.Replace("\"Изоляция — по изолируемому элементу\"", "\"Изоляция Б\"");
        editor.Logic.ApplyCommand.Execute(null);
        editor.Logic.ApplyError.ShouldBeNull();
        editor.SaveCommand.Execute(null);

        var saved = ProfileSerializer.Read(File.ReadAllText(editor.Entry.Path!));
        saved.Name.ShouldBe("Объект Б");
        saved.Positions.First(p => p.Code == "04.05.01.01.01").Description.ShouldBe("Оборудование водопровода");
        saved.Modes.First(m => m.Key == "water").Title.ShouldBe("ВК объекта");
        saved.Types.First(t => t.Key == "kip").Title.ShouldBe("КИП объекта");
        saved.Exceptions[1].Title.ShouldBe("Не учитывать (объект)");
        saved.Tables["ios"].Rows.First(r => r.Type == "armature" && r.Discipline == "water").Position.ShouldBe("04.05.01.01.04");
        saved.Modes.First(m => m.Key == "cooling").DisciplineRules[0].Title.ShouldBe("Изоляция Б");
    }

    [Test]
    public void SaveAsCopy_Fails_KeepsOriginalId()
    {
        using var temp = new TempFolder();
        var (editor, _, dialogs) = EditorSetup.Open(temp);
        var id = editor.Profile.Id;
        var path = editor.Entry.Path!;
        Directory.Delete(temp.Path, true);
        File.WriteAllText(temp.Path, "файл вместо папки — запись копии не удастся");
        try
        {
            editor.SaveAsCopyCommand.Execute(null);
        }
        finally
        {
            File.Delete(temp.Path);
            Directory.CreateDirectory(temp.Path);
        }

        dialogs.Shown.ShouldNotBeEmpty();
        editor.Profile.Id.ShouldBe(id);
        editor.Entry.Path.ShouldBe(path);
    }

    [Test]
    public void Binding_ModeMissing_WarnsAndSelectsNothing()
    {
        var profile = new ClassifierProfile { Id = Guid.NewGuid(), Name = "Б", Version = "2.0", Modes = [new ModeDef { Key = "fire" }] };
        IReadOnlyList<ProfileEntry> entries = [new(profile, "b.json", ProfileOrigin.Personal, false, null)];
        var choice = ProfileChoice.Resolve(entries, new ModelBinding(profile.Id, "Б", "1.0", "vent"), "Модель", null);
        choice.Entry.Profile.ShouldBe(profile);
        choice.ModeMissing.ShouldBeTrue();
        choice.Warning!.ShouldContain("vent");
    }

    [Test]
    [Arguments("{\"schemaVersion\":\"abc\"}")]
    [Arguments("{}")]
    [Arguments("[]")]
    [Arguments("{\"schemaVersion\":1,\"modes\":[null]}")]
    [Arguments("{\"schemaVersion\":1,\"dictionaries\":{\"d\":{\"entries\":[null]}}}")]
    [Arguments("{\"schemaVersion\":1,\"ruleSets\":{\"r\":[null]}}")]
    public void Read_Malformed_GivesReadableError(string json)
    {
        var error = Should.Throw<ProfileReadException>(() => ProfileSerializer.Read(json));
        error.Message.ShouldStartWith("Не удалось прочитать профиль");
    }

    [Test]
    public void Library_MalformedFile_EntryWithError()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.File("x.classifier.json"), "{\"schemaVersion\":\"abc\"}");
        var library = new ProfileLibrary(temp.Path, [], () => []);
        library.List().Single().Error.ShouldNotBeNull();
    }

    [Test]
    public void Fragment_UnknownField_Rejected()
    {
        var error = Should.Throw<ProfileReadException>(() => ProfileSerializer.ReadFragment<RuleDef>("{\"fallthrought\":true}"));
        error.Message.ShouldContain("fallthrought");
    }

    [Test]
    public void Logic_TypoInJson_NotApplied()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        editor.Logic.SelectedMode = editor.Profile.Modes.First(m => m.Key == "cooling");
        editor.Logic.JsonText = editor.Logic.JsonText.Replace("\"fallthrough\"", "\"fallthrought\"");
        editor.Logic.ApplyCommand.Execute(null);
        editor.Logic.ApplyError!.ShouldContain("fallthrought");
        editor.IsDirty.ShouldBeFalse();
    }

    [Test]
    public void Exception_WithoutConditions_IsError()
    {
        var profile = ProfileSerializer.Clone(Donskoy.Profile.Source);
        profile.Exceptions.Add(new RuleDef { Title = "Пусто", Default = new ResultDef { Skip = "вручную" } });
        profile.Exceptions.Add(new RuleDef { Title = "Пустой список", When = [new ConditionDef { Category = [] }], Default = new ResultDef { Skip = "вручную" } });
        var issues = ProfileCompiler.Compile(profile).Issues;
        issues.ShouldContain(i => i.Path.Contains("Пусто") && i.Message.Contains("без условий"));
        issues.ShouldContain(i => i.Path.Contains("Пустой список") && i.Message.Contains("пуст"));
    }

    [Test]
    public void Import_ExtraColumns_FillsExtra()
    {
        using var temp = new TempFolder();
        var (editor, host, _) = EditorSetup.Open(temp);
        editor.General.AddOutputCommand.Execute(null);
        editor.General.Outputs.Last().Field = "Ед";
        host.Xlsx = [["Код", "Описание", "Единица"], ["01", "Оборудование", "шт"]];
        editor.Positions.ImportExcelCommand.Execute(null);
        var extra = editor.Positions.Preview!.Extras.Single();
        extra.Field.ShouldBe("Ед");
        extra.Column = 2;
        editor.Positions.ApplyImportCommand.Execute(null);
        editor.Profile.Positions.Single(p => p.Code == "01").Extra["Ед"].ShouldBe("шт");
    }
}
