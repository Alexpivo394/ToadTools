using System.Globalization;
using System.IO;
using ClosedXML.Excel;

namespace ClassifierCode.Check;

/// <summary>Лист Excel → строки текстом. Берутся сохранённые значения ячеек (у формул — посчитанные), не формулы.</summary>
public static class XlsxRows
{
    public static (List<string[]> Rows, string Sheet, IReadOnlyList<string> Sheets) Read(string path, string? sheet)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); // книга может быть открыта в Excel
        using var workbook = new XLWorkbook(stream);
        var sheets = workbook.Worksheets.Select(s => s.Name).ToList();
        var worksheet = sheet == null
            ? workbook.Worksheets.FirstOrDefault(s => !s.IsEmpty())
            : int.TryParse(sheet, out var number) && number >= 1 && number <= sheets.Count
                ? workbook.Worksheet(number)
                : workbook.Worksheets.FirstOrDefault(s => string.Equals(s.Name, sheet, StringComparison.OrdinalIgnoreCase));
        if (worksheet == null)
            throw new InvalidDataException($"Нет листа «{sheet}». Листы: {string.Join(", ", sheets)}");

        var rows = new List<string[]>();
        if (worksheet.LastRowUsed() is not { } lastRow || worksheet.LastColumnUsed() is not { } lastColumn) return (rows, worksheet.Name, sheets);
        for (var r = 1; r <= lastRow.RowNumber(); r++)
        {
            var row = new string[lastColumn.ColumnNumber()];
            for (var c = 1; c <= row.Length; c++) row[c - 1] = Text(worksheet.Cell(r, c).CachedValue);
            rows.Add(row);
        }

        return (rows, worksheet.Name, sheets);
    }

    /// <summary>Числа — с точкой при любой локали: код «4.05», записанный в Excel числом, не должен стать «4,05».</summary>
    private static string Text(XLCellValue value) => value.Type switch
    {
        XLDataType.Blank => string.Empty,
        XLDataType.Number => value.GetNumber().ToString(null, CultureInfo.InvariantCulture),
        XLDataType.Text => value.GetText().Trim(),
        _ => value.ToString(CultureInfo.InvariantCulture).Trim()
    };
}
