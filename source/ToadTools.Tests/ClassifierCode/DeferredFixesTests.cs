using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Library;
using ClassifierCode.Engine.Profile;
using ClassifierCode.Engine.Running;
using Shouldly;

namespace ToadTools.Tests.ClassifierCode;

/// <summary>Отложенные находки ревью — каждая воспроизведена тестом до исправления.</summary>
public class DeferredFixesTests
{
    private static ClassifierProfile Small(string slowPattern = "x") => new()
    {
        Outputs = [new OutputField { Field = "code", Parameter = "Код" }],
        Texts = { ["name"] = new TextSourceDef { Parts = ["instance:Имя"] } },
        Types = [new KeyTitle { Key = "x", Title = "Икс" }, new KeyTitle { Key = "y", Title = "Игрек" }, new KeyTitle { Key = "z", Title = "Зет" }],
        Disciplines = [new KeyTitle { Key = "a", Title = "А" }, new KeyTitle { Key = "b", Title = "Б" }],
        Positions = [new Position { Code = "AX" }, new Position { Code = "AY" }, new Position { Code = "BX" }, new Position { Code = "BY" }],
        Dictionaries =
        {
            ["d"] = new DictionaryDef
            {
                Entries =
                [
                    new DictionaryEntryDef { Result = new ResultDef { Skip = "вручную" }, Patterns = ["вручную"] },
                    new DictionaryEntryDef { Result = new ResultDef { Type = "z" }, Patterns = ["зет"] },
                    new DictionaryEntryDef { Result = new ResultDef { Type = "x" }, Patterns = [slowPattern] }
                ]
            }
        },
        RuleSets =
        {
            ["t"] =
            [
                new RuleDef
                {
                    Steps = [new StepDef { Dictionary = "d", Source = "name" }],
                    Allow = new AllowDef { Values = ["x", "y"], Otherwise = "y" }, Default = new ResultDef { Type = "y" }
                }
            ]
        },
        Tables =
        {
            ["m"] = new TableDef
            {
                Rows =
                [
                    new TableRow { Discipline = "a", Type = "x", Position = "AX" }, new TableRow { Discipline = "a", Type = "y", Position = "AY" },
                    new TableRow { Discipline = "b", Type = "x", Position = "BX" }, new TableRow { Discipline = "b", Type = "y", Position = "BY" }
                ]
            }
        },
        Modes =
        [
            new ModeDef
            {
                Key = "m", Title = "М", TypeRules = "t", Table = "m", DefaultDiscipline = "b",
                DisciplineRules =
                [
                    new RuleDef { Title = "Родитель", Steps = [new StepDef { Inherit = "parent" }], Fallthrough = true },
                    new RuleDef { When = [new ConditionDef { Type = ["x"] }], Default = new ResultDef { Discipline = "a" } }
                ]
            }
        ],
        Exceptions =
        [
            new RuleDef { Title = "Родитель — икс", When = [new ConditionDef { Source = "name", Matches = "=родитель" }], Default = new ResultDef { Type = "x" } }
        ]
    };

    private static ClassificationEngine Engine(ClassifierProfile profile) => new(ProfileCompiler.Compile(profile).Profile!, "m");

    [Test]
    public void ExceptionType_SameDiscipline_RegardlessOfOrder()
    {
        var engine = Engine(Small());
        var parent = new FakeFacts { Instance = { ["Имя"] = "родитель" } };
        var child = new FakeFacts { Instance = { ["Имя"] = "ребёнок" }, Parent = parent };

        // Сначала вложенный: родитель — тип «икс» из исключения → раздел «А», и ребёнок берёт его.
        engine.Classify(child).Discipline.ShouldBe("a");
        engine.Classify(parent).Position!.Code.ShouldBe("AX");
    }

    [Test]
    public void Allow_DoesNotReplaceSkip()
    {
        var result = Engine(Small()).Classify(new FakeFacts { Instance = { ["Имя"] = "вручную" } });
        result.Status.ShouldBe(EngineStatus.Skipped);
        result.Reason.ShouldBe("вручную");

        // А обычный неразрешённый результат по-прежнему заменяется.
        Engine(Small()).Classify(new FakeFacts { Instance = { ["Имя"] = "зет" } }).Type.ShouldBe("y");
    }

    [Test]
    public void BrokenPattern_RememberedAcrossRecompiles()
    {
        var slow = new FakeFacts { Instance = { ["Имя"] = new string('a', 40) + "!" } };
        Engine(Small("re:^(a+)+$")).Classify(slow).Status.ShouldBe(EngineStatus.Unclassified);

        // Пересобранный профиль (новые объекты Regex) — тот же шаблон уже известен как сломанный.
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Engine(Small("re:^(a+)+$")).Classify(slow).Reason!.ShouldContain("re:^(a+)+$");
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));
    }

    [Test]
    public void UnknownCategory_IsIssue_WhenValidatorGiven()
    {
        var profile = Small();
        profile.Categories = ["OST_PipeCurve"];
        profile.Exceptions[0].When.Add(new ConditionDef { Category = ["OST_DuctCurve"] });
        var issues = ProfileCompiler.Compile(profile, name => name is "OST_PipeCurves" or "OST_DuctCurves").Issues;
        issues.ShouldContain(i => i.Path == "Категории" && i.Message.Contains("OST_PipeCurve"));
        issues.ShouldContain(i => i.Path.StartsWith("Исключения") && i.Message.Contains("OST_DuctCurve"));
        ProfileCompiler.Compile(profile).Issues.ShouldNotContain(i => i.Message.Contains("OST_PipeCurve"));
    }
}

public class DeferredEditorTests
{
    [Test]
    public void PositionCode_Edit_RenamesReferences()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        editor.Positions.Visible.First(p => p.Code == "04.05.01.01.03").Code = "ВК.АРМ";
        editor.Profile.Tables["ios"].Rows.ShouldContain(r => r.Position == "ВК.АРМ");
        editor.Profile.Tables["ios"].Rows.ShouldNotContain(r => r.Position == "04.05.01.01.03");
        editor.Issues.ShouldBeEmpty();
    }

    [Test]
    public void Fallbacks_ShownAndEditedByTitles()
    {
        using var temp = new TempFolder();
        var (editor, _, _) = EditorSetup.Open(temp);
        var row = editor.Mapping.Fallbacks.First(f => f.Type == "insulation");
        row.AlternativesText.ShouldBe("Тепло-огнезащита; Трубы (крепёж, гильзы, окраска)");
        row.AlternativesText = "Трубы (крепёж, гильзы, окраска); equipment";
        editor.Profile.Tables["ios"].Fallbacks["insulation"].ShouldBe(["pipeGroup", "equipment"]);
    }

    [Test]
    public void Pick_InspectsPickedElements()
    {
        using var temp = new TempFolder();
        var (editor, host, _) = EditorSetup.Open(temp);
        host.Elements[5] = new FakeFacts { Id = 5, Category = "OST_PipeAccessory", Domain = NetworkDomain.Pipe, Instance = { ["ADSK_Наименование"] = "Клапан" } };
        host.Picked.Add(5);
        editor.Check.PickCommand.Execute(null);
        editor.Check.Inspected.Single().Id.ShouldBe(5);
    }

    [Test]
    public void Save_FileChangedByOthers_Refuses()
    {
        using var temp = new TempFolder();
        var (editor, _, dialogs) = EditorSetup.Open(temp);
        var path = editor.Entry.Path!;
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        editor.General.Description = "моя правка";
        editor.SaveCommand.Execute(null);
        dialogs.Shown.Last().ShouldContain("изменён");
        editor.IsDirty.ShouldBeTrue();
    }
}

public class DeferredLibraryTests
{
    private static ClassifierProfile P(Guid id, string version) => new() { Id = id, Name = "Объект", Version = version, Modes = [new ModeDef { Key = "m" }] };

    [Test]
    public void SameId_PrefersBoundVersion_AndWarnsOnMismatch()
    {
        var id = Guid.NewGuid();
        IReadOnlyList<ProfileEntry> entries =
        [
            new(P(id, "1.0"), "personal.json", ProfileOrigin.Personal, false, null),
            new(P(id, "2.0"), "shared.json", ProfileOrigin.Shared, true, null)
        ];

        var exact = ProfileChoice.Resolve(entries, new ModelBinding(id, "Объект", "2.0", "m"), "x", null);
        exact.Entry.Path.ShouldBe("shared.json");
        exact.Warning.ShouldBeNull();

        var newer = ProfileChoice.Resolve(entries, new ModelBinding(id, "Объект", "0.9", "m"), "x", null);
        newer.Entry.Path.ShouldBe("shared.json"); // старшая из имеющихся
        newer.Warning!.ShouldContain("0.9");
    }

    [Test]
    public void Import_Existing_AsksBeforeReplacing()
    {
        using var temp = new TempFolder();
        using var outside = new TempFolder();
        var id = Guid.NewGuid();
        var library = new ProfileLibrary(temp.Path, [], () => []);
        library.Save(P(id, "1.0"), null);
        var file = outside.File("x.classifier.json");
        File.WriteAllText(file, ProfileSerializer.Write(P(id, "2.0")));

        var copy = library.Import(file, _ => false);
        copy.Profile!.Id.ShouldNotBe(id);
        library.List().Count(e => e.Profile?.Id == id).ShouldBe(1);

        var replaced = library.Import(file, existing => existing.Profile!.Version == "1.0");
        replaced.Profile!.Id.ShouldBe(id);
        library.List().Single(e => e.Profile?.Id == id).Profile!.Version.ShouldBe("2.0");
    }
}
