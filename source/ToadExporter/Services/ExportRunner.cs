using System.Diagnostics;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ToadExporter.Core;
using ToadTools.UI.Progress;
using Wpf.Ui.Controls;

namespace ToadExporter.Services;

public sealed record RunSummary(IReadOnlyList<ModelResult> Results, bool Cancelled, string LogPath);

/// <summary>
///     Выгружает модели по одной: открыть → NWC → RVT → закрыть. Ошибка или пропуск одной модели не
///     останавливает остальные; «Прервать» останавливает после закрытия текущей.
/// </summary>
public sealed class ExportRunner(UIApplication uiApplication)
{
    private readonly ModelOpener _opener = new(uiApplication);

    public RunSummary Run(IReadOnlyList<ModelJob> jobs, ExportSettings settings)
    {
        TempWorkspace.CleanupUnused();

        var output = Path.GetFullPath(settings.OutputFolder.Trim());
        Directory.CreateDirectory(output);

        var results = new List<ModelResult>();
        var cancelled = false;
        var nwcAvailable = NwcExporter.IsAvailable();

        using var log = ExportLog.Create(output, DateTime.Now);
        log.Header(settings, jobs.Count, RvtSaver.PurgeAvailable);

        using (var progress = ToadProgress.Show("ToadExporter", new ProgressOptions
               {
                   Icon = SymbolRegular.ArrowExportUp24,
                   SkipText = "Пропустить модель",
                   CancelHint = "Прерываем — текущая модель будет закрыта без сохранения"
               }))
        {
            progress.Stage("Выгрузка моделей");
            for (var index = 0; index < jobs.Count; index++)
            {
                progress.Report(index, jobs.Count, $"{index + 1} из {jobs.Count}: {jobs[index].Name}");
                results.Add(RunOne(jobs[index], settings, output, nwcAvailable, progress, log));

                if (progress.IsCancelled)
                {
                    cancelled = true;
                    break;
                }
            }

            progress.Report(results.Count, jobs.Count);
        }

        log.Summary(results, cancelled);
        return new RunSummary(results, cancelled, log.FilePath);
    }

    private ModelResult RunOne(ModelJob job, ExportSettings settings, string output, bool nwcAvailable,
        ToadProgress progress, ExportLog log)
    {
        var result = new ModelResult { Name = job.Name };
        var notes = new List<string>();
        var stopwatch = Stopwatch.StartNew();
        var baseName = Path.GetFileNameWithoutExtension(job.Name);
        var target = Path.Combine(output, job.Name);
        Document? document = null;
        OpenedModel? opened = null;
        string? central = null;

        progress.BeginItem(job.Name, canSkip: true);
        using var temp = TempWorkspace.Create();
        try
        {
            opened = _opener.Open(job, settings, temp, progress, log);
            document = opened.Document;

            if (job.Nwc && !nwcAvailable)
            {
                result.Nwc = StepResult.Skipped;
                notes.Add("Navisworks Exporter не установлен");
            }
            else if (job.Nwc)
            {
                ThrowIfAborted(progress);
                progress.ItemStatus("Экспорт NWC");
                log.Step(job.Name, "экспорт NWC");
                result.Nwc = Attempt(progress, notes, "NWC", () =>
                {
                    var step = NwcExporter.Export(document, output, baseName, settings, out var note);
                    Note(notes, note);
                    if (step == StepResult.Done && settings.NwcExportLinks && opened.LinksUnloaded)
                        notes.Add("связи не загружались — в NWC их нет");
                    return step;
                });
            }

            if (job.Rvt)
            {
                ThrowIfAborted(progress);
                log.Step(job.Name, "подготовка и сохранение RVT");
                result.Rvt = Attempt(progress, notes, "RVT", () =>
                {
                    var path = target;
                    if (document.IsWorkshared)
                        path = Path.Combine(Directory.CreateDirectory(Path.Combine(temp.Directory, "central")).FullName, job.Name);
                    Note(notes, RvtSaver.Save(document, path, opened.IsTransmitted, settings, progress));
                    if (document.IsWorkshared) central = path;
                    return StepResult.Done;
                });
            }
        }
        catch (Exception exception) when (progress.ShouldAbortItem || exception is OperationCanceledException)
        {
            result.SkippedByUser = true;
        }
        catch (Exception exception)
        {
            // Модель не открылась — всё, что просили, не выгружено.
            if (job.Rvt && result.Rvt == StepResult.NotRequested) result.Rvt = StepResult.Failed;
            if (job.Nwc && result.Nwc == StepResult.NotRequested) result.Nwc = StepResult.Failed;
            notes.Add(exception.Message);
        }
        finally
        {
            var workshared = document is { IsValidObject: true, IsWorkshared: true };
            Close(document, job, progress, log);
            if (result.Rvt == StepResult.Done && central is not null) CreateLocal(central, target, result, notes, progress);
            if (result.Rvt == StepResult.Done && opened is not null)
            {
                // Связи не загружались — Revit сохраняет их выгруженными, у получателя их нужно загрузить вручную.
                if (opened.LinksUnloaded && !settings.DeleteRevitLinks) notes.Add("связи RVT сохранены выгруженными");

                // Модели совместной работы пометку «передан» снял SaveAs; обычному файлу её снимаем сами.
                if (opened.IsTransmitted && !workshared) ClearTransmitted(target, notes);
            }
            result.Elapsed = stopwatch.Elapsed;
            result.Note = notes.Count == 0 ? null : string.Join("; ", notes);
            log.Model(result);
            progress.EndItem();
        }

        return result;
    }

    private static void CreateLocal(string central, string target, ModelResult result, List<string> notes, ToadProgress progress)
    {
        progress.ItemStatus("Создание локальной копии RVT");
        try
        {
            RvtSaver.CreateLocal(central, target);
        }
        catch (Exception exception)
        {
            result.Rvt = StepResult.Failed;
            notes.Add($"RVT: {exception.Message}");
        }
    }

    private static void ClearTransmitted(string filePath, List<string> notes)
    {
        try
        {
            ModelOpener.ClearTransmitted(filePath);
        }
        catch (Exception exception)
        {
            notes.Add($"RVT: не удалось снять пометку «передан»: {exception.Message}");
        }
    }

    /// <summary>Шаг, ошибка которого не мешает следующему шагу той же модели.</summary>
    private static StepResult Attempt(ToadProgress progress, List<string> notes, string format, Func<StepResult> step)
    {
        try
        {
            return step();
        }
        catch (Exception exception) when (!progress.ShouldAbortItem && exception is not OperationCanceledException)
        {
            notes.Add($"{format}: {exception.Message}");
            return StepResult.Failed;
        }
    }

    private static void Note(List<string> notes, string? note)
    {
        if (!string.IsNullOrEmpty(note)) notes.Add(note!);
    }

    private static void ThrowIfAborted(ToadProgress progress)
    {
        if (progress.ShouldAbortItem) throw new OperationCanceledException();
    }

    private static void Close(Document? document, ModelJob job, ToadProgress progress, ExportLog log)
    {
        if (document is null || !document.IsValidObject) return;

        progress.ItemStatus("Закрытие модели");
        try
        {
            document.Close(false);
        }
        catch (Exception exception)
        {
            log.Step(job.Name, $"не удалось закрыть модель: {exception.Message}");
        }
    }
}
