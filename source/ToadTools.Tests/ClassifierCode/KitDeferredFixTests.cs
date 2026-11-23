using System.Globalization;
using ClassifierCode.Check;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Kit;
using ClassifierCode.Engine.Profile;
using ClosedXML.Excel;
using Shouldly;

namespace ToadTools.Tests.ClassifierCode;

/// <summary>Отложенные замечания, которые могли тихо испортить результат.</summary>
public class KitDeferredFixTests
{
    private static ExportModel Named(string model, string source) =>
        new() { Model = model, Source = source, Elements = [new ExportElement { Id = 1, Category = "OST_PipeCurves" }] };

    [Test]
    public void FileName_StablePerSource_AcrossRuns()
    {
        using var temp = new TempFolder();
        string Run(string source)
        {
            var name = ExportNames.FileName("ОВВ", source, temp.Path, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            ExportWriter.Write(Named("ОВВ", source), temp.File(name));
            return name;
        }

        var first = Run(@"\\srv\Корпус1\ОВВ.rvt");
        var second = Run(@"\\srv\Корпус2\ОВВ.rvt");
        first.ShouldBe("ОВВ.export.json");
        second.ShouldNotBe(first);
        // Довыгрузка только второй модели пишет в её же файл, а не поверх первой.
        Run(@"\\srv\Корпус2\ОВВ.rvt").ShouldBe(second);
        Run(@"\\srv\Корпус1\ОВВ.rvt").ShouldBe(first);
        Directory.GetFiles(temp.Path, "*.export.json").Length.ShouldBe(2);
        ExportNames.ModelOf(second).ShouldBe("ОВВ");
    }

    [Test]
    public void FileName_SameRun_TwoSources_Differ()
    {
        using var temp = new TempFolder();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var a = ExportNames.FileName("ВК", "a", temp.Path, taken);
        var b = ExportNames.FileName("ВК", "b", temp.Path, taken);
        a.ShouldNotBe(b);
        ExportNames.FileName("a:b?", "x", temp.Path, taken).ShouldBe("a_b_.export.json");
    }

    [Test]
    public void LookupWinner_KeptInOrder_AndReadFirst()
    {
        using var temp = new TempFolder();
        var model = Named("ВК", "x");
        model.Elements[0].Parameters =
        [
            new ExportParameter { Name = "Марка", Value = "ПЕРВЫЙ", IsString = true },
            new ExportParameter { Name = "Марка", Value = "LOOKUP", IsString = true, Lookup = true }
        ];
        ExportWriter.Write(model, temp.File("x.export.json"));
        var facts = ExportFacts.Load(ExportReader.Read(temp.File("x.export.json"))).Single();
        facts.InstanceParameter("Марка").ShouldBe("LOOKUP");
        facts.InstanceStringValues().ShouldBe(["ПЕРВЫЙ", "LOOKUP"]); // порядок ParameterSet не тронут
    }

    [Test]
    public void Xlsx_NumericCode_Invariant()
    {
        using var temp = new TempFolder();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Коды");
            sheet.Cell("A1").Value = 4.05;
            sheet.Cell("B1").Value = "Позиция";
            workbook.SaveAs(temp.File("c.xlsx"));
        }

        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
            XlsxRows.Read(temp.File("c.xlsx"), null).Rows[0][0].ShouldBe("4.05");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Test]
    public void ClaudeMd_ImportsAgents_AndKeepsNotes()
    {
        using var kit = new TempFolder();
        foreach (var f in new[] { "AGENTS.md", "FORMAT.md", "profile.schema.json", "README_КООРДИНАТОРУ.txt" }) File.WriteAllText(kit.File(f), f);
        Directory.CreateDirectory(kit.File("check"));
        File.WriteAllText(kit.File("check\\check.exe"), "exe");
        using var work = new TempFolder();
        KitFolder.Prepare(work.Path, kit.Path, Donskoy.Profile.Source, null);
        File.ReadAllText(work.File("CLAUDE.md")).ShouldContain("@AGENTS.md");

        File.AppendAllText(work.File("CLAUDE.md"), "\nзаметка агента\n");
        KitFolder.Prepare(work.Path, kit.Path, Donskoy.Profile.Source, null);
        File.ReadAllText(work.File("CLAUDE.md")).ShouldContain("заметка агента");
    }

    private static (int Code, string Output) Cli(string folder, params string[] args)
    {
        var output = new StringWriter();
        return (CheckCli.Run(args, output, folder), output.ToString());
    }

    [Test]
    public void Cli_WildcardSkipsLog_AndUndetectedReported()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.File("profile.classifier.json"), ProfileSerializer.Write(Donskoy.Profile.Source));
        Directory.CreateDirectory(temp.File("exports"));
        ExportWriter.Write(Exp.Model("Проект1", [Exp.Pipe("Т1")]), temp.File("exports\\Проект1.export.json"));
        File.WriteAllText(temp.File("exports\\_журнал.txt"), "журнал");
        var (code, output) = Cli(temp.Path, "anomalies", "profile.classifier.json", "exports\\*");
        code.ShouldBe(0, output);
        output.ShouldContain("раздел не определён");
        Cli(temp.Path, "diff", "profile.classifier.json", "exports\\*").Output.ShouldContain("раздел не определён");
    }

    [Test]
    public void Cli_UnexpectedError_Code2_NoStackTrace()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.File("bad.xlsx"), "это не Excel");
        var (code, output) = Cli(temp.Path, "classifier", "bad.xlsx");
        code.ShouldBe(2);
        output.ShouldStartWith("Ошибка:");
        output.ShouldNotContain("   at ");
        output.ShouldNotContain("   в ");
    }
}
