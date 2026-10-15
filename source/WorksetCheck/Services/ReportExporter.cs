using System.IO;
using OfficeOpenXml;
using WorksetCheck.Models;

namespace WorksetCheck.Services;

/// <summary>Выгрузка списка замечаний в Excel.</summary>
public static class ReportExporter
{
    public static void Save(string path, string modelTitle, IReadOnlyList<WorksetIssue> issues)
    {
        ExcelPackage.License.SetNonCommercialPersonal("ToadTools");
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Проверка РН");

        sheet.Cells[1, 1].Value = $"Проверка рабочих наборов: {modelTitle}, {DateTime.Now:dd.MM.yyyy HH:mm}";
        sheet.Cells[1, 1].Style.Font.Bold = true;

        string[] headers = ["Id", "Категория", "Элемент", "Рабочий набор", "Замечание", "Действие"];
        for (var column = 0; column < headers.Length; column++)
        {
            sheet.Cells[3, column + 1].Value = headers[column];
            sheet.Cells[3, column + 1].Style.Font.Bold = true;
        }

        var row = 4;
        foreach (var issue in issues)
        {
            sheet.Cells[row, 1].Value = issue.Id;
            sheet.Cells[row, 2].Value = issue.Category;
            sheet.Cells[row, 3].Value = issue.Element;
            sheet.Cells[row, 4].Value = issue.Workset;
            sheet.Cells[row, 5].Value = issue.Problem;
            sheet.Cells[row, 6].Value = issue.ActionText;
            row++;
        }

        if (issues.Count > 0) sheet.Cells[3, 1, row - 1, headers.Length].AutoFilter = true;
        sheet.View.FreezePanes(4, 1);
        sheet.Cells[3, 1, Math.Max(row - 1, 3), headers.Length].AutoFitColumns(8, 70);

        package.SaveAs(new FileInfo(path));
    }
}
