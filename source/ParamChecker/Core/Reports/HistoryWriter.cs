using System.IO;
using OfficeOpenXml;
using OfficeOpenXml.Table;
using ParamChecker.Core.Checking;

namespace ParamChecker.Core.Reports;

/// <summary>
///     База для дашбордов (Power BI, Excel): один файл, в который каждый запуск дописывает строки. Три таблицы Excel
///     в «длинном» формате — каждая строка одно измерение, поэтому история копится, а срезы строятся без формул:
///     <list type="bullet">
///         <item><c>Models</c> — модель в запуске: статус, итоги;</item>
///         <item><c>Rules</c> — правило модели в запуске;</item>
///         <item><c>Parameters</c> — параметр правила модели в запуске.</item>
///     </list>
///     Все таблицы связываются по «Запуск» + «Профиль» + «Модель». Последний запуск модели — максимум «Запуск».
/// </summary>
public static class HistoryWriter
{
    private static readonly TableSpec Models = new("Models", "Модели",
        ["Запуск", "Профиль", "Модель", "Путь", "Статус", "Ошибка", "Длительность, с", "Элементов", "Заполнены полностью",
            "Полнота", "Значений", "Заполнено", "Пусто", "Нет параметра", "Заполненность"]);

    private static readonly TableSpec Rules = new("Rules", "Правила",
        ["Запуск", "Профиль", "Модель", "Правило", "Элементов", "Заполнены полностью", "Полнота", "Значений", "Заполнено",
            "Пусто", "Нет параметра", "Заполненность"]);

    private static readonly TableSpec Parameters = new("Parameters", "Параметры",
        ["Запуск", "Профиль", "Модель", "Правило", "Параметр", "Элементов", "Заполнено", "Пусто", "Нет параметра", "Заполненность"]);

    static HistoryWriter() => ExcelPackage.License.SetNonCommercialPersonal("ToadTools");

    /// <summary>Дописывает модели запуска в базу; файла нет — создаёт.</summary>
    /// <param name="run">Начало запуска — общее для всех его строк.</param>
    /// <exception cref="IOException">Файл занят, например открыт в Excel.</exception>
    public static void Append(string filePath, DateTime run, IReadOnlyList<ModelOutcome> outcomes)
    {
        if (outcomes.Count == 0) return;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        using var package = File.Exists(filePath) ? new ExcelPackage(new FileInfo(filePath)) : new ExcelPackage();

        var models = new List<object?[]>();
        var rules = new List<object?[]>();
        var parameters = new List<object?[]>();
        foreach (var outcome in outcomes)
        {
            var report = outcome.Report;
            models.Add([
                run, outcome.ProfileName, outcome.ModelName, outcome.SourcePath, StatusText(outcome.Status), outcome.Error,
                Math.Round(outcome.Duration.TotalSeconds), report?.Elements, report?.CompleteElements, report?.Completeness,
                report?.Values, report?.Filled, report?.Empty, report?.Missing, report?.FillRate
            ]);
            if (report is null) continue;

            foreach (var rule in report.Rules)
            {
                rules.Add([
                    run, outcome.ProfileName, outcome.ModelName, rule.Title, rule.Elements, rule.CompleteElements,
                    rule.Completeness, rule.Values, rule.Filled, rule.Empty, rule.Missing, rule.FillRate
                ]);
                parameters.AddRange(rule.Stats.Select(stats => new object?[]
                {
                    run, outcome.ProfileName, outcome.ModelName, rule.Title, stats.Name, stats.Total, stats.Filled,
                    stats.Empty, stats.Missing, stats.FillRate
                }));
            }
        }

        AppendRows(package, Models, models);
        AppendRows(package, Rules, rules);
        AppendRows(package, Parameters, parameters);
        package.SaveAs(new FileInfo(filePath));
    }

    public static string StatusText(ModelStatus status) => status switch
    {
        ModelStatus.Checked => "Проверена",
        ModelStatus.Skipped => "Пропущена",
        _ => "Ошибка"
    };

    private static void AppendRows(ExcelPackage package, TableSpec spec, IReadOnlyList<object?[]> rows)
    {
        var sheet = package.Workbook.Worksheets[spec.Sheet] ?? package.Workbook.Worksheets.Add(spec.Sheet);
        var table = sheet.Tables[spec.Table] ?? CreateTable(sheet, spec);
        if (rows.Count == 0) return;

        // Пустая таблица хранит одну пустую строку данных — её занимает первая новая строка.
        var empty = table.Address.Rows == 2 && sheet.Cells[table.Address.End.Row, 1].Value is null;
        var first = empty ? table.Address.End.Row : table.Address.End.Row + 1;
        var add = empty ? rows.Count - 1 : rows.Count;
        if (add > 0) table.AddRow(add);

        sheet.Cells[first, 1].LoadFromArrays(rows);
        var last = first + rows.Count - 1;
        for (var column = 1; column <= spec.Columns.Length; column++)
        {
            var format = Format(spec.Columns[column - 1]);
            if (format is not null) sheet.Cells[first, column, last, column].Style.Numberformat.Format = format;
        }
    }

    private static ExcelTable CreateTable(ExcelWorksheet sheet, TableSpec spec)
    {
        for (var i = 0; i < spec.Columns.Length; i++) sheet.Cells[1, i + 1].Value = spec.Columns[i];
        var table = sheet.Tables.Add(sheet.Cells[1, 1, 2, spec.Columns.Length], spec.Table);
        table.TableStyle = TableStyles.Medium2;
        sheet.View.FreezePanes(2, 1);
        for (var i = 1; i <= spec.Columns.Length; i++) sheet.Column(i).Width = spec.Columns[i - 1] is "Путь" or "Ошибка" ? 50 : 18;
        return table;
    }

    private static string? Format(string column) => column switch
    {
        "Запуск" => "dd.mm.yyyy hh:mm",
        "Полнота" or "Заполненность" => "0.0%",
        _ => null
    };

    private sealed record TableSpec(string Table, string Sheet, string[] Columns);
}
