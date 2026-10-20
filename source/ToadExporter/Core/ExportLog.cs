using System.IO;
using System.Text;

namespace ToadExporter.Core;

/// <summary>
///     Журнал выгрузки в папке результатов. Пишется сразу на диск, чтобы остаться, даже если Revit упадёт посреди выгрузки.
/// </summary>
public sealed class ExportLog : IDisposable
{
    private readonly StreamWriter _writer;

    private ExportLog(string filePath)
    {
        FilePath = filePath;
        _writer = new StreamWriter(new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(true)) { AutoFlush = true };
    }

    public string FilePath { get; }

    public static ExportLog Create(string folder, DateTime now) =>
        new(Path.Combine(folder, $"ToadExporter_{now:yyyy-MM-dd_HH-mm}.log"));

    /// <param name="purgeAvailable">Очистка неиспользуемого есть только в Revit 2024 и новее.</param>
    public void Header(ExportSettings settings, int count, bool purgeAvailable)
    {
        var purge = purgeAvailable ? YesNo(settings.Purge) : "недоступна до Revit 2024";
        Line($"ToadExporter — выгрузка {count} моделей");
        Line(settings.SourceKind == SourceKind.Server
            ? $"Источник: Revit Server {settings.Server}"
            : $"Источник: папка {settings.SourceFolder}");
        Line($"Результаты: {settings.OutputFolder}");
        Line($"Открытие: не загружать связи — {YesNo(settings.SkipLinks)}, проверка — {YesNo(settings.Audit)}" +
             (settings.WorksetKeyword.Length > 0 ? $", не открывать РН с «{settings.WorksetKeyword}»" : ""));
        Line($"RVT: удалить связи RVT — {YesNo(settings.DeleteRevitLinks)}, CAD — {YesNo(settings.DeleteCad)}, " +
             $"облака точек — {YesNo(settings.DeletePointClouds)}, очистка — {purge}, " +
             $"сжатие — {YesNo(settings.Compact)}");
        Line($"NWC: вид «{settings.NwcViewName}», координаты — {(settings.NwcSharedCoordinates ? "общие" : "внутренние")}");
        _writer.WriteLine();
    }

    public void Step(string model, string text) => Line($"{model}: {text}");

    public void Model(ModelResult result)
    {
        var line = result.SkippedByUser
            ? $"{result.Name} — пропущена пользователем{Finished(result)}, {Format(result.Elapsed)}"
            : $"{result.Name} — RVT: {Format(result.Rvt)}, NWC: {Format(result.Nwc)}, {Format(result.Elapsed)}";
        if (!string.IsNullOrEmpty(result.Note)) line += $" — {result.Note}";
        Line(line);
    }

    public void Summary(IReadOnlyList<ModelResult> all, bool cancelled)
    {
        _writer.WriteLine();
        Line(SummaryText(all));
        if (cancelled) Line("Выгрузка прервана пользователем");
    }

    /// <summary>Итог одной строкой — для журнала и для окна итога.</summary>
    public static string SummaryText(IReadOnlyList<ModelResult> all)
    {
        var text = $"Выгружено: {all.Count(r => r.IsExported)}, пропущено: {all.Count(r => r.SkippedByUser)}, " +
                   $"с ошибками: {all.Count(r => r.HasErrors)}";
        var nothing = all.Count(r => r.IsNothingExported);
        return nothing > 0 ? text + $", без результата: {nothing}" : text;
    }

    /// <summary>Что успело выгрузиться до нажатия «Пропустить».</summary>
    private static string Finished(ModelResult result)
    {
        var done = new[] { (result.Rvt, "RVT"), (result.Nwc, "NWC") }
            .Where(step => step.Item1 == StepResult.Done)
            .Select(step => step.Item2)
            .ToList();
        return done.Count == 0 ? "" : $" ({string.Join(" и ", done)} успел выгрузиться)";
    }

    public void Dispose() => _writer.Dispose();

    private void Line(string text) => _writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] {text}");

    private static string YesNo(bool value) => value ? "да" : "нет";

    private static string Format(StepResult result) => result switch
    {
        StepResult.Done => "выгружено",
        StepResult.Skipped => "пропущено",
        StepResult.Failed => "ошибка",
        _ => "—"
    };

    private static string Format(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"mm\:ss");
}
