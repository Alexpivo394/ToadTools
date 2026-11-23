using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;

namespace WorkingSet.Models;


public class WorkingSetModel
{
    private readonly string _excelFilePath;

    public WorkingSetModel(string excelFilePath)
    {
        _excelFilePath = excelFilePath;
    }

    // Получение списка разделов из Excel
    public List<string> GetSections()
    {
        using var workbook = Open();
        return workbook.Worksheets.Select(worksheet => worksheet.Name).ToList();
    }

    // Получение рабочих наборов из конкретного раздела
    public List<string> GetWorksetsFromSection(string sectionName)
    {
        var worksets = new List<string>();

        using var workbook = Open();
        if (!workbook.Worksheets.TryGetWorksheet(sectionName, out var worksheet)) return worksets;

        var row = 1;
        while (true)
        {
            var worksetName = worksheet.Cell(row, 1).GetFormattedString();
            if (string.IsNullOrEmpty(worksetName)) break;

            worksets.Add(worksetName);
            row++;
        }

        return worksets;
    }

    // Файл общий, лежит на сетевом диске: читаем, даже если он открыт у кого-то в Excel
    private XLWorkbook Open()
    {
        using var stream = new FileStream(_excelFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return new XLWorkbook(stream);
    }
}
