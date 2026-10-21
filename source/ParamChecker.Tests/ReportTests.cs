using OfficeOpenXml;
using ParamChecker.Core;
using ParamChecker.Core.Checking;
using ParamChecker.Core.Reports;
using Shouldly;

namespace ParamChecker.Tests;

public class ReportTests
{
    private static readonly ParamValue Filled = new(true, "ok");
    private static readonly ParamValue Empty = new(true, "");

    private static ModelReport Report()
    {
        var report = new ModelReport { ProfileName = "ОВ", ModelName = "Дом_ОВ", SourcePath = "RSN://rs/Дом_ОВ.rvt", CheckedAt = DateTime.Now };
        var rule = new RuleReport("Воздуховоды", ["Марка", "Код"]);
        foreach (var values in new[] { new[] { Filled, Filled }, [Filled, Empty], [ParamValue.Missing, Empty] })
        {
            rule.Count(values);
            rule.Rows.Add(new ElementRow(1, "Воздуховоды", "Сем", "Тип", "РН", values));
        }

        report.Rules.Add(rule);
        report.Rules.Add(new RuleReport("Воздуховоды", ["Марка"])); // то же название — лист должен получить другое имя
        return report;
    }

    [Test]
    public void Stats_CountElementsAndValues()
    {
        var rule = Report().Rules[0];

        rule.Elements.ShouldBe(3);
        rule.CompleteElements.ShouldBe(1);
        rule.Values.ShouldBe(6);
        rule.Filled.ShouldBe(3);
        rule.Empty.ShouldBe(2);
        rule.Missing.ShouldBe(1);
        rule.FillRate.ShouldBe(0.5);
        rule.Stats[0].FillRate.ShouldBe(2d / 3);
        rule.Stats[1].Filled.ShouldBe(1);
    }

    [Test]
    public void EmptyRule_HasNoRates()
    {
        var rule = new RuleReport("x", ["a"]);

        rule.FillRate.ShouldBeNull();
        rule.Completeness.ShouldBeNull();
    }

    [Test]
    public void SheetNames_AreValidAndUnique()
    {
        var names = new SheetNames(ReportWriter.SummarySheet);

        names.Next("Сводка", 1).ShouldBe("Сводка (2)");
        names.Next("", 2).ShouldBe("Правило 2");
        names.Next("Трубы: ОВ/ВК [все]?", 3).ShouldBe("Трубы_ ОВ_ВК _все__");
        names.Next(new string('я', 40), 4).Length.ShouldBe(31);
        names.Next(new string('я', 40), 5).ShouldBe(new string('я', 27) + " (2)");
    }

    [Test]
    public void WritesWorkbook()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pc_{Guid.NewGuid():N}.xlsx");
        try
        {
            ReportWriter.Write(Report(), path, problemsOnly: false).ShouldBe(path);

            using var package = new ExcelPackage(new FileInfo(path));
            package.Workbook.Worksheets.Select(s => s.Name).ShouldBe(["Сводка", "Воздуховоды", "Воздуховоды (2)"]);
            var sheet = package.Workbook.Worksheets["Воздуховоды"];
            sheet.Cells[1, 6].Text.ShouldBe("Марка");
            sheet.Cells[4, 6].Text.ShouldBe(ReportWriter.MissingText);
            sheet.Cells[2, 8].GetValue<double>().ShouldBe(1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void History_AppendsRunsToTables()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pc_{Guid.NewGuid():N}.xlsx");
        try
        {
            var checkedModel = new ModelOutcome("ОВ", "Дом_ОВ", "RSN://rs/Дом_ОВ.rvt", ModelStatus.Checked, TimeSpan.FromSeconds(42), Report());
            var failed = new ModelOutcome("ОВ", "Дом_ВК", "RSN://rs/Дом_ВК.rvt", ModelStatus.Failed, TimeSpan.Zero, Error: "нет вида");

            HistoryWriter.Append(path, new DateTime(2026, 10, 1, 9, 0, 0), [checkedModel, failed]);
            HistoryWriter.Append(path, new DateTime(2026, 10, 2, 9, 0, 0), [checkedModel]);

            using var package = new ExcelPackage(new FileInfo(path));
            var models = package.Workbook.Worksheets["Модели"].Tables["Models"];
            models.Address.Rows.ShouldBe(1 + 3);
            package.Workbook.Worksheets["Модели"].Cells[3, 5].Text.ShouldBe("Ошибка");
            package.Workbook.Worksheets["Правила"].Tables["Rules"].Address.Rows.ShouldBe(1 + 4);
            package.Workbook.Worksheets["Параметры"].Tables["Parameters"].Address.Rows.ShouldBe(1 + 6);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
