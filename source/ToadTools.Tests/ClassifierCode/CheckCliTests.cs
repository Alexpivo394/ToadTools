using ClassifierCode.Check;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Profile;
using OfficeOpenXml;
using Shouldly;

namespace ToadTools.Tests.ClassifierCode;

public class CheckCliTests
{
    private static (int Code, string Output) Run(string folder, params string[] args)
    {
        var output = new StringWriter();
        var code = CheckCli.Run(args, output, folder);
        return (code, output.ToString());
    }

    /// <summary>Рабочая папка: встроенный профиль и выгрузка «ШФТ-ОВО» с трубами и арматурой.</summary>
    private static TempFolder Workspace()
    {
        var temp = new TempFolder();
        File.WriteAllText(temp.File("profile.classifier.json"), ProfileSerializer.Write(Donskoy.Profile.Source));
        Directory.CreateDirectory(temp.File("exports"));
        ExportWriter.Write(Exp.Model("ШФТ-ОВО", [Exp.Pipe("Т1"), Exp.Pipe("Т1"), Exp.Accessory("Клапан", "Т1"), Exp.Pipe("")]),
            temp.File("exports\\ШФТ-ОВО.export.json"));
        return temp;
    }

    [Test]
    public void Classifier_Pnr_ReadsCachedValues()
    {
        using var temp = new TempFolder();
        ExcelPackage.License.SetNonCommercialPersonal("ToadTools");
        using (var package = new ExcelPackage())
        {
            package.Workbook.Worksheets.Add("Кодификатор до 3-го уровня").Cells["A1"].Value = "Код статьи";
            var sheet = package.Workbook.Worksheets.Add("Кодификатор концепция");
            sheet.Cells["A1"].Value = "Код новый";
            sheet.Cells["B1"].Value = "Наименование статей оборотов";
            sheet.Cells["C1"].Value = "Ед. изм.";
            sheet.Cells["A2"].Value = "04.01";
            sheet.Cells["B2"].Value = "Общестроительные работы";
            sheet.Cells["A3"].Value = "04.05.01.12";
            sheet.Cells["B3"].Value = "ИТП/ЦТП";
            sheet.Cells["A4"].Formula = "A3&\".01\"";
            sheet.Cells["B4"].Value = "Оборудование";
            sheet.Cells["C4"].Value = "шт.";
            sheet.Calculate();
            package.SaveAs(new FileInfo(temp.File("codes.xlsx")));
        }

        var (code, output) = Run(temp.Path, "classifier", "codes.xlsx", "--sheet", "Кодификатор концепция", "--prefix", "04.05");
        code.ShouldBe(0, output);
        output.ShouldContain("позиций: 2");
        var tsv = File.ReadAllLines(temp.File("work\\positions.tsv"));
        tsv.ShouldContain(l => l.StartsWith("04.05.01.12.01\tОборудование\tшт."));
        tsv[0].ShouldContain("Ед. изм.");
    }

    [Test]
    public void Validate_ExitCodes()
    {
        using var temp = Workspace();
        var ok = Run(temp.Path, "validate", "profile.classifier.json", "--exports", "exports\\*.export.json");
        ok.Code.ShouldBe(0, ok.Output);

        var broken = ProfileSerializer.Clone(Donskoy.Profile.Source);
        broken.Outputs.Clear();
        File.WriteAllText(temp.File("broken.json"), ProfileSerializer.Write(broken));
        var (code, output) = Run(temp.Path, "validate", "broken.json");
        code.ShouldBe(1);
        output.ShouldContain("Параметры");

        Run(temp.Path, "validate", "нет-такого.json").Code.ShouldBe(2);
    }

    [Test]
    public void Run_WritesWork()
    {
        using var temp = Workspace();
        var (code, output) = Run(temp.Path, "run", "profile.classifier.json", "exports\\*.export.json");
        code.ShouldBe(0, output);
        output.ShouldContain("ШФТ-ОВО");
        File.Exists(temp.File("work\\run\\run.md")).ShouldBeTrue();
        File.ReadAllText(temp.File("work\\run\\codes.tsv")).ShouldContain("04.05.01.07.04.01");

        Run(temp.Path, "summary", "exports\\*.export.json").Code.ShouldBe(0);
        File.Exists(temp.File("work\\summary\\systems.tsv")).ShouldBeTrue();
        Run(temp.Path, "anomalies", "profile.classifier.json", "exports\\*.export.json").Code.ShouldBe(0);
        File.Exists(temp.File("work\\anomalies.tsv")).ShouldBeTrue();
        Run(temp.Path, "diff", "profile.classifier.json", "exports\\*.export.json").Code.ShouldBe(0);
    }

    [Test]
    public void Explain_ById_PrintsTrace()
    {
        using var temp = Workspace();
        var (code, output) = Run(temp.Path, "explain", "profile.classifier.json", "exports\\ШФТ-ОВО.export.json", "3");
        code.ShouldBe(0, output);
        output.ShouldContain("04.05.01.07.05");
        output.ShouldContain("Тип:");

        Run(temp.Path, "explain", "profile.classifier.json", "exports\\ШФТ-ОВО.export.json", "клапан").Output.ShouldContain("04.05.01.07.05");
    }

    [Test]
    public void UnknownCommand_Usage()
    {
        using var temp = new TempFolder();
        var (code, output) = Run(temp.Path, "нечто");
        code.ShouldBe(2);
        output.ShouldContain("classifier");
        Run(temp.Path).Output.ShouldContain("run");
    }
}
