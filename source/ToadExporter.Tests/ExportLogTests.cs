using Shouldly;
using Alexpivo394.Revit.Export.Core;
using ToadExporter.Core;

namespace ToadExporter.Tests;

public class ExportLogTests
{
    [Test]
    public void FileName_UsesStartTime()
    {
        var dir = NewDir();
        using var log = ExportLog.Create(dir, new DateTime(2026, 10, 19, 14, 5, 0));

        Path.GetFileName(log.FilePath).ShouldBe("ToadExporter_2026-10-19_14-05.log");
    }

    [Test]
    public void Log_ModelLineAndSummary()
    {
        var dir = NewDir();
        string path;
        using (var log = ExportLog.Create(dir, DateTime.Now))
        {
            path = log.FilePath;
            log.Model(new ModelResult
            {
                Name = "AR.rvt", Rvt = StepResult.Done, Nwc = StepResult.Skipped,
                Note = "вид «Navisworks» не найден", Elapsed = TimeSpan.FromSeconds(134)
            });
            log.Model(new ModelResult { Name = "KR.rvt", SkippedByUser = true, Elapsed = TimeSpan.FromSeconds(40) });
            log.Model(new ModelResult { Name = "OV.rvt", Rvt = StepResult.Failed, Note = "файл занят", Elapsed = TimeSpan.FromHours(1.5) });
            log.Summary([
                new ModelResult { Rvt = StepResult.Done },
                new ModelResult { SkippedByUser = true },
                new ModelResult { Rvt = StepResult.Failed }
            ], cancelled: true);
        }

        var text = File.ReadAllText(path);
        text.ShouldContain("AR.rvt — RVT: выгружено, NWC: пропущено, 02:14 — вид «Navisworks» не найден");
        text.ShouldContain("KR.rvt — пропущена пользователем, 00:40");
        text.ShouldContain("OV.rvt — RVT: ошибка, NWC: —, 1:30:00 — файл занят");
        text.ShouldContain("Выгружено: 1, пропущено: 1, с ошибками: 1");
        text.ShouldContain("Выгрузка прервана пользователем");
    }

    [Test]
    public void SkippedModel_MentionsFinishedNwc()
    {
        var text = Write(log => log.Model(new ModelResult
        {
            Name = "AR.rvt", SkippedByUser = true, Nwc = StepResult.Done, Elapsed = TimeSpan.FromSeconds(5)
        }));

        text.ShouldContain("AR.rvt — пропущена пользователем (NWC успел выгрузиться), 00:05");
    }

    [Test]
    public void Summary_CountsModelsWithNothingExported()
    {
        var text = Write(log => log.Summary([
            new ModelResult { Rvt = StepResult.Done },
            new ModelResult { Nwc = StepResult.Skipped, Note = "вид не найден" }
        ], cancelled: false));

        text.ShouldContain("Выгружено: 1, пропущено: 0, с ошибками: 0, без результата: 1");
    }

    [Test]
    public void Summary_OmitsZeroNothingExported()
    {
        var text = Write(log => log.Summary([new ModelResult { Rvt = StepResult.Done }], cancelled: false));

        text.ShouldContain("Выгружено: 1, пропущено: 0, с ошибками: 0");
        text.ShouldNotContain("без результата");
    }

    [Test]
    public void Header_PurgeUnavailable_SaysSo()
    {
        var text = Write(log => log.Header(new ExportSettings { Purge = true }, 1, purgeAvailable: false));

        text.ShouldContain("очистка — недоступна до Revit 2024");
    }

    [Test]
    public void Header_PurgeAvailable_SaysYes()
    {
        var text = Write(log => log.Header(new ExportSettings { Purge = true }, 1, purgeAvailable: true));

        text.ShouldContain("очистка — да");
    }

    private static string Write(Action<ExportLog> write)
    {
        string path;
        using (var log = ExportLog.Create(NewDir(), DateTime.Now))
        {
            path = log.FilePath;
            write(log);
        }

        return File.ReadAllText(path);
    }

    [Test]
    public void Step_HasTimestamp()
    {
        var dir = NewDir();
        string path;
        using (var log = ExportLog.Create(dir, DateTime.Now))
        {
            path = log.FilePath;
            log.Step("AR.rvt", "Открытие модели");
        }

        File.ReadAllText(path).ShouldMatch(@"\[\d\d:\d\d:\d\d\] AR\.rvt: Открытие модели");
    }

    private static string NewDir() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "te-log-" + Guid.NewGuid().ToString("N"))).FullName;
}
