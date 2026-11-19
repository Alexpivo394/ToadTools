using System.IO;
using OfficeOpenXml;

namespace ClassifierCode.Check;

/// <summary>Лист Excel → строки текстом. Берутся сохранённые значения ячеек (у формул — посчитанные), не формулы.</summary>
public static class XlsxRows
{
    public static (List<string[]> Rows, string Sheet, IReadOnlyList<string> Sheets) Read(string path, string? sheet)
    {
        ExcelPackage.License.SetNonCommercialPersonal("ToadTools");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); // книга может быть открыта в Excel
        using var package = new ExcelPackage(stream);
        var sheets = package.Workbook.Worksheets.Select(s => s.Name).ToList();
        var worksheet = sheet == null
            ? package.Workbook.Worksheets.FirstOrDefault(s => s.Dimension != null)
            : int.TryParse(sheet, out var number) && number >= 1 && number <= sheets.Count
                ? package.Workbook.Worksheets[number - 1]
                : package.Workbook.Worksheets.FirstOrDefault(s => string.Equals(s.Name, sheet, StringComparison.OrdinalIgnoreCase));
        if (worksheet == null)
            throw new InvalidDataException($"Нет листа «{sheet}». Листы: {string.Join(", ", sheets)}");

        var rows = new List<string[]>();
        if (worksheet.Dimension == null) return (rows, worksheet.Name, sheets);
        for (var r = 1; r <= worksheet.Dimension.End.Row; r++)
        {
            var row = new string[worksheet.Dimension.End.Column];
            for (var c = 1; c <= row.Length; c++) row[c - 1] = Text(worksheet.Cells[r, c].Value);
            rows.Add(row);
        }

        return (rows, worksheet.Name, sheets);
    }

    /// <summary>Числа — с точкой при любой локали: код «4.05», записанный в Excel числом, не должен стать «4,05».</summary>
    private static string Text(object? value) => value switch
    {
        null => string.Empty,
        IFormattable number when value is double or float or decimal or int or long => number.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString()!.Trim()
    };
}
