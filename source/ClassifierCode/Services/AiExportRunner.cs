using System.IO;
using System.Text;
using Alexpivo394.Revit.Export.Core;
using Alexpivo394.Revit.Export.Services;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Kit;
using ClassifierCode.Engine.Library;
using ClassifierCode.Engine.Profile;
using ToadDesign;
using ToadTools.UI.Progress;
using UIApplication = Autodesk.Revit.UI.UIApplication;

namespace ClassifierCode.Services;

/// <param name="Models">Модели с сервера или из папки; открываются отсоединёнными без связей и закрываются без сохранения.</param>
/// <param name="CurrentDocument">Выгрузить и открытую сейчас модель.</param>
/// <param name="FindProfile">Профиль по привязке модели — им модель заполнялась (для поиска ручных правок).</param>
public sealed record AiExportJob(
    string ObjectName,
    string Folder,
    ClassifierProfile BaseProfile,
    IReadOnlyList<ModelJob> Models,
    bool CurrentDocument,
    string KitSource,
    Func<ModelBinding, ClassifierProfile?> FindProfile);

public sealed record AiExportFailure(string Model, string Error);

public sealed record AiExportSummary(IReadOnlyList<string> Exported, IReadOnlyList<AiExportFailure> Failed, int Skipped, bool Cancelled, string LogPath);

/// <summary>
///     Выгрузка моделей объекта для ИИ-агента: набор в рабочую папку, затем модели по одной.
///     Ошибка одной модели — в журнал, остальные выгружаются; «Пропустить» бросает модель, «Прервать» — всю выгрузку.
/// </summary>
public sealed class AiExportRunner(UIApplication uiApplication)
{
    public AiExportSummary Run(AiExportJob job)
    {
        KitFolder.Prepare(job.Folder, job.KitSource, job.BaseProfile, null);
        TempWorkspace.CleanupUnused();

        var exports = Path.Combine(job.Folder, KitFolder.Exports);
        var logPath = Path.Combine(exports, KitFolder.Log);
        using var log = new FileLog(logPath);
        log.Line($"=== {DateTime.Now:yyyy-MM-dd HH:mm} — объект «{job.ObjectName}», профиль-основа «{job.BaseProfile.Name}» {job.BaseProfile.Version}, моделей {job.Models.Count + (job.CurrentDocument ? 1 : 0)}");

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exported = new List<string>();
        var failed = new List<AiExportFailure>();
        var bindings = new List<ModelBinding?>();
        var skipped = 0;
        var cancelled = false;
        var opener = new ModelOpener(uiApplication.Application);
        var total = job.Models.Count + (job.CurrentDocument ? 1 : 0);

        using (var progress = ToadProgress.Show("Выгрузка для ИИ", new ProgressOptions
               {
                   Icon = Symbol.FileExport,
                   SkipText = "Пропустить модель",
                   CancelHint = "Прерываем — текущая модель будет закрыта без сохранения"
               }))
        {
            var itemProgress = new ItemProgress(progress);
            progress.Stage("Выгрузка моделей");
            var index = 0;

            if (job.CurrentDocument && uiApplication.ActiveUIDocument?.Document is { } current)
            {
                progress.Report(index++, total, $"{index} из {total}: {current.Title}");
                progress.BeginItem(current.Title);
                try
                {
                    Save(current, current.PathName, current.Title, job, exports, taken, exported, bindings, log);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failed.Add(new AiExportFailure(current.Title, exception.Message));
                    log.Line($"{current.Title}: ОШИБКА {exception.Message}");
                }

                progress.EndItem();
            }

            foreach (var model in job.Models)
            {
                if (progress.IsCancelled)
                {
                    cancelled = true;
                    break;
                }

                progress.Report(index++, total, $"{index} из {total}: {model.Name}");
                progress.BeginItem(model.Name, canSkip: true);
                try
                {
                    using var temp = TempWorkspace.Create();
                    var opened = opener.Open(model, new ExportOptions { SkipLinks = true, AllowUpgrade = true }, temp, itemProgress, log);
                    try
                    {
                        itemProgress.Status("Сбор параметров элементов");
                        Save(opened.Document, model.SourcePath, System.IO.Path.GetFileNameWithoutExtension(model.Name), job, exports, taken, exported, bindings, log);
                    }
                    finally
                    {
                        // Выгрузка уже записана: сбой закрытия — в журнал, а не в «ошибку модели».
                        try
                        {
                            opened.Document.Close(false);
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException)
                        {
                            log.Line($"{model.Name}: не закрылась ({exception.Message})");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    skipped++;
                    log.Line($"{model.Name}: пропущена");
                }
                catch (Exception exception)
                {
                    failed.Add(new AiExportFailure(model.Name, exception.Message));
                    log.Line($"{model.Name}: ОШИБКА {exception.Message}");
                }

                progress.EndItem();
                if (progress.IsCancelled) cancelled = true;
            }

            progress.Report(total, total);
        }

        if (KitFolder.CommonBinding(bindings) is { } binding && job.FindProfile(binding) is { } filledBy)
        {
            KitFolder.WriteFilledBy(job.Folder, filledBy);
            log.Line($"модели заполнялись профилем «{filledBy.Name}» {filledBy.Version} → kit\\filled-by.classifier.json");
        }
        else if (KitFolder.CommonBinding(bindings) is { } missing)
            log.Line($"модели заполнялись профилем «{missing.Name}» {missing.Version}, его нет в библиотеке — ручные правки агент не отличит");

        log.Line($"итог: выгружено {exported.Count}, ошибок {failed.Count}, пропущено {skipped}{(cancelled ? ", прервано" : string.Empty)}");
        return new AiExportSummary(exported, failed, skipped, cancelled, logPath);
    }

    private static void Save(Document document, string source, string modelName, AiExportJob job, string exports, HashSet<string> taken, List<string> exported,
        List<ModelBinding?> bindings, FileLog log)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var model = new FactsExporter(document, job.BaseProfile.Outputs).Export(source, modelName);
        var name = ExportNames.FileName(model.Model, model.Source, exports, taken);
        ExportWriter.Write(model, Path.Combine(exports, name));
        bindings.Add(model.Binding);
        exported.Add(name);
        log.Line($"{model.Model}: элементов {model.Elements.Count}, типов {model.Types.Count}, {watch.Elapsed.TotalSeconds:0} с → {name}");
    }

    private sealed class FileLog(string path) : IExportLog, IDisposable
    {
        private readonly StreamWriter _writer = new(path, true, new UTF8Encoding(true)) { AutoFlush = true };

        public void Step(string model, string text) => Line($"{model}: {text}");

        public void Line(string text) => _writer.WriteLine($"{DateTime.Now:HH:mm:ss} {text}");

        public void Dispose() => _writer.Dispose();
    }

    private sealed class ItemProgress(ToadProgress progress) : IExportProgress
    {
        public bool ShouldAbort => progress.ShouldAbortItem;
        public void Status(string? status) => progress.ItemStatus(status);
        public void Report(int current, int total, string? status) => progress.ItemReport(current, total, status);
    }
}
