using System.IO;
using OfficeOpenXml;
using OfficeOpenXml.ConditionalFormatting;
using OfficeOpenXml.Style;
using ParamChecker.Core.Checking;
using Color = System.Drawing.Color;

namespace ParamChecker.Core.Reports;

/// <summary>
///     Отчёт по модели: лист «Сводка» с итогами по правилам и параметрам и по листу на правило,
///     где у каждого элемента подсвечено, что заполнено, что пусто и какого параметра нет.
/// </summary>
public static class ReportWriter
{
    public const string SummarySheet = "Сводка";
    public const string MissingText = "нет параметра";

    private const string FilledStyle = "ParamFilled";
    private const string EmptyStyle = "ParamEmpty";
    private const string MissingStyle = "ParamMissing";
    private const string HeaderStyle = "ParamHeader";

    /// <summary>Строк, по которым подбирается ширина колонок: на больших листах подбор по всем — минуты.</summary>
    private const int AutoFitRows = 500;

    private static readonly string[] ElementColumns = ["ID", "Категория", "Семейство", "Тип", "Рабочий набор"];

    static ReportWriter() => ExcelPackage.License.SetNonCommercialPersonal("ToadTools");

    /// <param name="report">Итог проверки модели.</param>
    /// <param name="filePath">Куда сохранить отчёт.</param>
    /// <param name="problemsOnly">В листах правил только элементы с незаполненными параметрами.</param>
    /// <returns>Путь, по которому отчёт сохранён: если файл занят (открыт в Excel), рядом с временем в имени.</returns>
    public static string Write(ModelReport report, string filePath, bool problemsOnly)
    {
        using var package = new ExcelPackage();
        CreateStyles(package.Workbook);

        var summary = package.Workbook.Worksheets.Add(SummarySheet);
        var names = new SheetNames(SummarySheet);
        var sheets = report.Rules.Select((rule, i) => names.Next(rule.Title, i + 1)).ToList();

        for (var i = 0; i < report.Rules.Count; i++) WriteRule(package.Workbook.Worksheets.Add(sheets[i]), report.Rules[i]);
        WriteSummary(summary, report, sheets, problemsOnly);

        return Save(package, filePath);
    }

    private static void WriteSummary(ExcelWorksheet sheet, ModelReport report, IReadOnlyList<string> sheets, bool problemsOnly)
    {
        sheet.Cells[1, 1].Value = $"Заполнение параметров — {report.ModelName}";
        sheet.Cells[1, 1].Style.Font.Size = 16;
        sheet.Cells[1, 1].Style.Font.Bold = true;

        var row = 3;
        void Info(string label, object value)
        {
            sheet.Cells[row, 1].Value = label;
            sheet.Cells[row, 1].Style.Font.Color.SetColor(Color.DimGray);
            sheet.Cells[row, 2].Value = value;
            row++;
        }

        Info("Модель", report.ModelName);
        Info("Путь", report.SourcePath);
        Info("Профиль", report.ProfileName);
        Info("Элементы", report.ViewName.Length == 0 ? "вся модель" : $"вид «{report.ViewName}»");
        Info("Проверено", report.CheckedAt);
        sheet.Cells[row - 1, 2].Style.Numberformat.Format = "dd.mm.yyyy hh:mm";
        sheet.Cells[row - 1, 2].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;

        row++;
        var kpi = row;
        Kpi(sheet, row++, "Элементов проверено", report.Elements, null);
        Kpi(sheet, row++, "Заполнены полностью", report.CompleteElements, report.Completeness);
        Kpi(sheet, row++, "Значений проверено", report.Values, null);
        Kpi(sheet, row++, "Заполнено", report.Filled, report.FillRate);
        Kpi(sheet, row++, "Пусто", report.Empty, ModelReport.Ratio(report.Empty, report.Values));
        Kpi(sheet, row++, "Нет параметра", report.Missing, ModelReport.Ratio(report.Missing, report.Values));
        sheet.Cells[kpi + 1, 1, kpi + 1, 3].Style.Font.Bold = true;
        sheet.Cells[kpi + 3, 1, kpi + 3, 3].Style.Font.Bold = true;

        row++;
        sheet.Cells[row, 1].Value = "Полнота — доля элементов, у которых заполнены все параметры правила. " +
                                    "Заполненность — доля заполненных значений среди всех проверенных (элементы × параметры).";
        sheet.Cells[row, 1].Style.Font.Italic = true;
        sheet.Cells[row, 1].Style.Font.Color.SetColor(Color.DimGray);
        row++;
        if (problemsOnly)
        {
            sheet.Cells[row, 1].Value = "В листах правил — только элементы с незаполненными параметрами; итоги — по всем элементам.";
            sheet.Cells[row, 1].Style.Font.Italic = true;
            sheet.Cells[row, 1].Style.Font.Color.SetColor(Color.DimGray);
            row++;
        }

        row++;
        sheet.Cells[row++, 1].Value = "По правилам";
        sheet.Cells[row - 1, 1].Style.Font.Bold = true;
        var rulesHeader = row;
        Header(sheet, row++, "Правило", "Элементов", "Заполнены полностью", "Полнота", "Значений", "Заполнено", "Пусто",
            "Нет параметра", "Заполненность");
        for (var i = 0; i < report.Rules.Count; i++, row++)
        {
            var rule = report.Rules[i];
            var title = sheet.Cells[row, 1];
            var display = DisplayTitle(rule, sheets[i]);
            title.Value = display;
            title.Hyperlink = new ExcelHyperLink($"'{sheets[i].Replace("'", "''")}'!A1", display);
            title.Style.Font.UnderLine = true;
            title.Style.Font.Color.SetColor(Color.FromArgb(0x05, 0x63, 0xC1));
            if (rule.Elements == 0)
            {
                // Пустое правило — не «0 % заполнения»: элементов под него в модели просто нет.
                sheet.Cells[row, 2].Value = "нет элементов";
                sheet.Cells[row, 2].Style.Font.Color.SetColor(Color.DimGray);
                continue;
            }

            Numbers(sheet, row, 2, rule.Elements, rule.CompleteElements, rule.Completeness, rule.Values, rule.Filled, rule.Empty,
                rule.Missing, rule.FillRate);
        }

        Percent(sheet, rulesHeader + 1, row - 1, 4);
        Percent(sheet, rulesHeader + 1, row - 1, 9);

        row++;
        sheet.Cells[row++, 1].Value = "По параметрам";
        sheet.Cells[row - 1, 1].Style.Font.Bold = true;
        var parametersHeader = row;
        Header(sheet, row++, "Правило", "Параметр", "Элементов", "Заполнено", "Пусто", "Нет параметра", "Заполненность");
        for (var i = 0; i < report.Rules.Count; i++)
        {
            var rule = report.Rules[i];
            if (rule.Elements == 0) continue;

            foreach (var stats in rule.Stats)
            {
                sheet.Cells[row, 1].Value = DisplayTitle(rule, sheets[i]);
                sheet.Cells[row, 2].Value = stats.Name;
                Numbers(sheet, row, 3, stats.Total, stats.Filled, stats.Empty, stats.Missing, stats.FillRate);
                row++;
            }
        }

        Percent(sheet, parametersHeader + 1, row - 1, 7);

        row++;
        sheet.Cells[row, 1].Value = "Цвета в листах правил:";
        sheet.Cells[row, 2].Value = "заполнено";
        sheet.Cells[row, 2].StyleName = FilledStyle;
        sheet.Cells[row, 3].Value = "пусто";
        sheet.Cells[row, 3].StyleName = EmptyStyle;
        sheet.Cells[row, 4].Value = MissingText;
        sheet.Cells[row, 4].StyleName = MissingStyle;

        sheet.Column(1).Width = 34;
        sheet.Column(2).Width = 28;
        for (var column = 3; column <= 10; column++) sheet.Column(column).Width = 16;
        sheet.View.ShowGridLines = false;
    }

    private static string DisplayTitle(RuleReport rule, string sheet) => rule.Title.Trim().Length == 0 ? sheet : rule.Title.Trim();

    private static void Kpi(ExcelWorksheet sheet, int row, string label, int value, double? share)
    {
        sheet.Cells[row, 1].Value = label;
        sheet.Cells[row, 2].Value = value;
        sheet.Cells[row, 2].Style.Numberformat.Format = "# ##0";
        sheet.Cells[row, 2].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
        if (share is null) return;

        sheet.Cells[row, 3].Value = share;
        sheet.Cells[row, 3].Style.Numberformat.Format = "0.0%";
    }

    private static void Header(ExcelWorksheet sheet, int row, params string[] titles)
    {
        for (var i = 0; i < titles.Length; i++) sheet.Cells[row, i + 1].Value = titles[i];
        sheet.Cells[row, 1, row, titles.Length].StyleName = HeaderStyle;
    }

    private static void Numbers(ExcelWorksheet sheet, int row, int column, params object?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            var cell = sheet.Cells[row, column + i];
            cell.Value = values[i];
            cell.Style.Numberformat.Format = values[i] is double ? "0.0%" : "# ##0";
        }
    }

    /// <summary>Гистограмма в ячейках доли — видно, где провал, не вчитываясь в цифры.</summary>
    private static void Percent(ExcelWorksheet sheet, int fromRow, int toRow, int column)
    {
        if (toRow < fromRow) return;

        var bar = sheet.ConditionalFormatting.AddDatabar(sheet.Cells[fromRow, column, toRow, column], Color.FromArgb(0x63, 0xBE, 0x7B));
        bar.LowValue.Type = eExcelConditionalFormattingValueObjectType.Num;
        bar.LowValue.Value = 0;
        bar.HighValue.Type = eExcelConditionalFormattingValueObjectType.Num;
        bar.HighValue.Value = 1;
    }

    private static void WriteRule(ExcelWorksheet sheet, RuleReport rule)
    {
        var columns = ElementColumns.Concat(rule.Parameters).Append("Заполнено").ToArray();
        Header(sheet, 1, columns);

        var row = 2;
        foreach (var element in rule.Rows)
        {
            sheet.Cells[row, 1].Value = element.Id;
            sheet.Cells[row, 2].Value = element.Category;
            sheet.Cells[row, 3].Value = element.Family;
            sheet.Cells[row, 4].Value = element.Type;
            sheet.Cells[row, 5].Value = element.Workset;

            var filled = 0;
            for (var i = 0; i < element.Values.Count; i++)
            {
                var value = element.Values[i];
                var cell = sheet.Cells[row, ElementColumns.Length + 1 + i];
                switch (value.Status)
                {
                    case CellStatus.Filled:
                        cell.Value = value.Text;
                        cell.StyleName = FilledStyle;
                        filled++;
                        break;
                    case CellStatus.Empty:
                        cell.StyleName = EmptyStyle;
                        break;
                    default:
                        cell.Value = MissingText;
                        cell.StyleName = MissingStyle;
                        break;
                }
            }

            var share = sheet.Cells[row, columns.Length];
            share.Value = element.Values.Count == 0 ? 1d : (double)filled / element.Values.Count;
            share.Style.Numberformat.Format = "0%";
            row++;
        }

        var last = Math.Max(row - 1, 1);
        sheet.Cells[1, 1, last, columns.Length].AutoFilter = true;
        sheet.View.FreezePanes(2, ElementColumns.Length + 1);
        sheet.Cells[1, 1, Math.Min(last, AutoFitRows), columns.Length].AutoFitColumns(8, 50);
    }

    private static void CreateStyles(ExcelWorkbook workbook)
    {
        // Стандартные цвета Excel «Хороший / Нейтральный / Плохой»: мягче чистых зелёного, жёлтого и красного.
        Fill(workbook.Styles.CreateNamedStyle(FilledStyle).Style, Color.FromArgb(0xC6, 0xEF, 0xCE), Color.FromArgb(0x00, 0x61, 0x00));
        Fill(workbook.Styles.CreateNamedStyle(EmptyStyle).Style, Color.FromArgb(0xFF, 0xEB, 0x9C), Color.FromArgb(0x9C, 0x57, 0x00));
        Fill(workbook.Styles.CreateNamedStyle(MissingStyle).Style, Color.FromArgb(0xFF, 0xC7, 0xCE), Color.FromArgb(0x9C, 0x00, 0x06));

        var header = workbook.Styles.CreateNamedStyle(HeaderStyle).Style;
        Fill(header, Color.FromArgb(0x44, 0x54, 0x6A), Color.White);
        header.Font.Bold = true;
        header.WrapText = true;
        header.VerticalAlignment = ExcelVerticalAlignment.Center;
    }

    private static void Fill(ExcelStyle style, Color background, Color text)
    {
        style.Fill.PatternType = ExcelFillStyle.Solid;
        style.Fill.BackgroundColor.SetColor(background);
        style.Font.Color.SetColor(text);
    }

    private static string Save(ExcelPackage package, string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        try
        {
            package.SaveAs(new FileInfo(filePath));
            return filePath;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            // Чаще всего прошлый отчёт открыт в Excel — не теряем новый, кладём рядом.
            var alternative = Path.Combine(Path.GetDirectoryName(filePath)!,
                $"{Path.GetFileNameWithoutExtension(filePath)} ({DateTime.Now:HH-mm-ss}){Path.GetExtension(filePath)}");
            package.SaveAs(new FileInfo(alternative));
            return alternative;
        }
    }
}
