using ClosedXML.Excel;
using WorksetCheck.Models;

namespace WorksetCheck.Services;

/// <summary>Выгрузка списка замечаний в Excel.</summary>
public static class ReportExporter
{
    public static void Save(string path, string modelTitle, IReadOnlyList<WorksetIssue> issues)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Проверка РН");

        sheet.Cell(1, 1).Value = $"Проверка рабочих наборов: {modelTitle}, {DateTime.Now:dd.MM.yyyy HH:mm}";
        sheet.Cell(1, 1).Style.Font.Bold = true;

        string[] headers = ["Id", "Категория", "Элемент", "Рабочий набор", "Замечание", "Действие"];
        for (var column = 0; column < headers.Length; column++)
        {
            sheet.Cell(3, column + 1).Value = headers[column];
            sheet.Cell(3, column + 1).Style.Font.Bold = true;
        }

        var row = 4;
        foreach (var issue in issues)
        {
            sheet.Cell(row, 1).Value = issue.Id;
            sheet.Cell(row, 2).Value = issue.Category;
            sheet.Cell(row, 3).Value = issue.Element;
            sheet.Cell(row, 4).Value = issue.Workset;
            sheet.Cell(row, 5).Value = issue.Problem;
            sheet.Cell(row, 6).Value = issue.ActionText;
            row++;
        }

        var lastRow = Math.Max(row - 1, 3);
        if (issues.Count > 0) sheet.Range(3, 1, lastRow, headers.Length).SetAutoFilter();
        sheet.SheetView.FreezeRows(3);
        // Ширина — по таблице, без заголовка листа в первой строке
        sheet.Columns(1, headers.Length).AdjustToContents(3, lastRow, 8, 70);

        workbook.SaveAs(path);
    }
}
