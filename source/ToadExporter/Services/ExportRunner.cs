using System.IO;
using Alexpivo394.Revit.Export.Core;
using Alexpivo394.Revit.Export.Services;
using Autodesk.Revit.UI;
using ToadExporter.Core;
using ToadTools.UI.Progress;
using Wpf.Ui.Controls;

namespace ToadExporter.Services;

public sealed record RunSummary(IReadOnlyList<ModelResult> Results, bool Cancelled, string LogPath);

/// <summary>
///     Выгружает модели по одной через <see cref="ModelExporter" /> (пакет Alexpivo394.Revit.Export, общий с фермой ToadFlow).
///     Ошибка или пропуск одной модели не останавливает остальные; «Прервать» останавливает после закрытия текущей.
/// </summary>
public sealed class ExportRunner(UIApplication uiApplication)
{
    private readonly ModelExporter _exporter = new(uiApplication.Application);

    public RunSummary Run(IReadOnlyList<ModelJob> jobs, ExportSettings settings)
    {
        TempWorkspace.CleanupUnused();

        var output = Path.GetFullPath(settings.OutputFolder.Trim());
        Directory.CreateDirectory(output);

        var options = settings.ToOptions();
        var results = new List<ModelResult>();
        var cancelled = false;

        using var log = ExportLog.Create(output, DateTime.Now);
        log.Header(settings, jobs.Count, RvtSaver.PurgeAvailable);

        using (var progress = ToadProgress.Show("ToadExporter", new ProgressOptions
               {
                   Icon = SymbolRegular.ArrowExportUp24,
                   SkipText = "Пропустить модель",
                   CancelHint = "Прерываем — текущая модель будет закрыта без сохранения"
               }))
        {
            var itemProgress = new ItemProgress(progress);
            progress.Stage("Выгрузка моделей");
            for (var index = 0; index < jobs.Count; index++)
            {
                progress.Report(index, jobs.Count, $"{index + 1} из {jobs.Count}: {jobs[index].Name}");
                progress.BeginItem(jobs[index].Name, canSkip: true);
                var result = _exporter.Export(jobs[index], options, output, itemProgress, log);
                log.Model(result);
                progress.EndItem();
                results.Add(result);

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

    /// <summary>Ход выгрузки модели — в строку текущей модели окна прогресса; «Пропустить» и «Прервать» бросают модель.</summary>
    private sealed class ItemProgress(ToadProgress progress) : IExportProgress
    {
        public bool ShouldAbort => progress.ShouldAbortItem;

        public void Status(string? status) => progress.ItemStatus(status);

        public void Report(int current, int total, string? status) => progress.ItemReport(current, total, status);
    }
}
