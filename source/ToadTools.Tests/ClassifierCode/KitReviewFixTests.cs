using ClassifierCode.Check;
using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Kit;
using ClassifierCode.Engine.Library;
using ClassifierCode.Engine.Profile;
using Shouldly;

namespace ToadTools.Tests.ClassifierCode;

/// <summary>Замечания финального ревью набора ИИ.</summary>
public class KitReviewFixTests
{
    /// <summary>Два раздела по системе (A → a, B → b), один тип; строка таблицы есть только у «a».</summary>
    private static CompiledProfile Mini(Guid? id = null, string version = "1.0") =>
        ProfileCompiler.Compile(ProfileSerializer.Read($$"""
            {
              "schemaVersion": 1, "id": "{{id ?? Guid.NewGuid()}}", "name": "Мини", "version": "{{version}}",
              "outputs": [ { "field": "code", "parameter": "Код" } ],
              "categories": [ "OST_PipeCurves" ],
              "texts": { "system": { "parts": [ "systems" ], "normalize": true } },
              "positions": [ { "code": "1", "description": "a-труба" }, { "code": "2", "description": "b-труба" } ],
              "disciplines": [ { "key": "a", "title": "Раздел A" }, { "key": "b", "title": "Раздел B" } ],
              "types": [ { "key": "t", "title": "Труба" } ],
              "dictionaries": { "sys": { "entries": [
                { "result": { "discipline": "a" }, "patterns": [ "#Ж" ] },
                { "result": { "discipline": "b" }, "patterns": [ "#Ф" ] } ] } },
              "ruleSets": { "types": [ { "title": "Все", "default": { "type": "t" } } ] },
              "tables": { "main": { "rows": [ { "discipline": "a", "type": "t", "position": "1" } ] } },
              "modes": [ { "key": "m", "title": "Режим", "fileTokens": [ "М" ], "typeRules": "types", "table": "main",
                "disciplineRules": [ { "title": "Система", "steps": [ { "dictionary": "sys", "source": "system" } ] } ] } ]
            }
            """), null).Profile!;

    private static FakeFacts Pipe(string system, string? code = null)
    {
        var f = new FakeFacts { Category = "OST_PipeCurves", Domain = NetworkDomain.Pipe, SystemText = system, FamilyName = "Труба", TypeName = "Сталь" };
        f.Instance["ADSK_Наименование"] = "Труба";
        if (code != null) f.Instance["__code"] = code;
        return f;
    }

    [Test]
    public void Anomaly_MinorityUnclassified_InOtherDiscipline_Found()
    {
        // 40 труб в A получают код, 2 в B дают «раздел B + труба» без строки таблицы — это ошибка модели, а не повод для правила.
        var pipes = Enumerable.Range(0, 40).Select(_ => Pipe("Ж")).Concat([Pipe("Ф"), Pipe("Ф")]).ToList();
        var found = AnomalyFinder.Find(Mini(), [Exp.Model("Х-М", pipes)], null);
        found.Count.ShouldBe(2);
        found[0].Where.ShouldContain("Раздел B");
        found[0].Expected.ShouldContain("Раздел A");
    }

    [Test]
    public void Diff_ManualWhenFilledByGaveNothing()
    {
        var id = Guid.NewGuid();
        var profile = Mini(id);
        var model = Exp.Model("Х-М", [Pipe("Ф", code: "2")]);
        model.Binding = new ModelBinding(id, "Мини", "1.0", "m");
        var diff = DiffFinder.Find(profile, profile, [model], null).Single();
        diff.IsManual.ShouldBeTrue(); // профиль заполнения кода не дал, а код стоит — поставили руками
    }

    [Test]
    public void Diff_NotManual_WhenModelFilledByOtherProfileOrVersion()
    {
        var id = Guid.NewGuid();
        var filledBy = Mini(id, "1.0");
        var other = Exp.Model("Х-М", [Pipe("Ф", code: "2")]);
        other.Binding = new ModelBinding(Guid.NewGuid(), "Чужой", "1.0", "m");
        DiffFinder.Find(filledBy, filledBy, [other], null).Single().IsManual.ShouldBeFalse();

        var older = Exp.Model("Х-М", [Pipe("Ф", code: "2")]);
        older.Binding = new ModelBinding(id, "Мини", "0.9", "m");
        DiffFinder.Find(filledBy, filledBy, [older], null).Single().IsManual.ShouldBeFalse();
    }

    [Test]
    public void FindFilledBy_ExactVersionOnly()
    {
        var id = Guid.NewGuid();
        var library = new[] { Mini(id, "1.0").Source, Mini(id, "2.0").Source };
        KitFolder.FindFilledBy(library, new ModelBinding(id, "Мини", "2.0", "m"))!.Version.ShouldBe("2.0");
        KitFolder.FindFilledBy(library, new ModelBinding(id, "Мини", "3.0", "m")).ShouldBeNull();
    }

    [Test]
    public void ReadHeader_CategoriesWithoutElements()
    {
        using var temp = new TempFolder();
        var model = Exp.Model("Х-М", [Pipe("Ж")]);
        ExportWriter.Write(model, temp.File("x.export.json"));
        var header = ExportReader.ReadHeader(temp.File("x.export.json"));
        header.Model.ShouldBe("Х-М");
        header.Categories.ShouldContain("OST_PipeCurves");
        header.Elements.ShouldBeEmpty();
    }

    [Test]
    public void Reader_RejectsJsonThatIsNotExport()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.File("p.json"), "{\"schemaVersion\":1,\"name\":\"профиль\"}");
        Should.Throw<InvalidDataException>(() => ExportReader.Read(temp.File("p.json"))).Message.ShouldContain("не выгрузка");
    }

    [Test]
    public void ParameterNames_Interned()
    {
        using var temp = new TempFolder();
        ExportWriter.Write(Exp.Model("Х-М", [Pipe("Ж"), Pipe("Ж")]), temp.File("x.export.json"));
        var elements = ExportReader.Read(temp.File("x.export.json")).Elements;
        ReferenceEquals(elements[0].Parameters[0].Name, elements[1].Parameters[0].Name).ShouldBeTrue();
    }

    private static (int Code, string Output) Cli(string folder, params string[] args)
    {
        var output = new StringWriter();
        return (CheckCli.Run(args, output, folder), output.ToString());
    }

    [Test]
    public void Cli_UnknownOption_Fails()
    {
        using var temp = new TempFolder();
        var (code, output) = Cli(temp.Path, "summary", "x.export.json", "--prefx", "04.05");
        code.ShouldBe(2);
        output.ShouldContain("--prefx");
    }

    [Test]
    public void Cli_EqualsSyntax_AndNumbers()
    {
        using var temp = new TempFolder();
        Cli(temp.Path, "classifier", "нет.xlsx", "--code-col", "B").Output.ShouldContain("--code-col");
        var (code, output) = Cli(temp.Path, "classifier", "нет.xlsx", "--sheet=Кодификатор концепция");
        code.ShouldBe(2);
        output.ShouldNotContain("--sheet"); // опция разобрана, ошибка — в отсутствии файла
        output.ShouldContain("нет.xlsx");
    }

    [Test]
    public void Cli_RepeatedExports_Accumulate()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.File("profile.classifier.json"), ProfileSerializer.Write(Donskoy.Profile.Source));
        ExportWriter.Write(Exp.Model("А", [Exp.Pipe("Т1")]), temp.File("a.export.json"));
        ExportWriter.Write(Exp.Model("Б", [Exp.Pipe("Т1")]), temp.File("b.export.json"));
        var (code, output) = Cli(temp.Path, "summary", "a.export.json", "b.export.json");
        code.ShouldBe(0, output);
        output.ShouldContain("А:");
        output.ShouldContain("Б:");
        Cli(temp.Path, "validate", "profile.classifier.json", "--exports", "a.export.json", "--exports", "b.export.json").Code.ShouldBe(0);
    }

    [Test]
    public void Cli_ModesWithUnknownKey_Warns()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.File("profile.classifier.json"), ProfileSerializer.Write(Donskoy.Profile.Source));
        ExportWriter.Write(Exp.Model("А", [Exp.Pipe("Т1")]), temp.File("a.export.json"));
        File.WriteAllText(temp.File("modes.json"), "{\"А\":\"отопление\"}");
        var (_, output) = Cli(temp.Path, "run", "profile.classifier.json", "a.export.json", "--modes", "modes.json");
        output.ShouldContain("отопление");
        output.ShouldContain("нет в профиле");
    }
}
