using System.Diagnostics;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ParamChecker.Core;
using ParamChecker.Core.Checking;
using ParamChecker.Core.Profiles;
using ParamChecker.Core.Reports;
using ToadTools.UI.Progress;
using Wpf.Ui.Controls;

namespace ParamChecker.Services;

/// <param name="HistoryError">База не дописалась туда, куда настроено; текст — что случилось и куда записано.</param>
public sealed record RunSummary(IReadOnlyList<ModelOutcome> Outcomes, bool Cancelled, string LogPath, string ReportsFolder, string? HistoryError);

/// <summary>
///     Проверяет модели по одной: открыть → проверить правила → отчёт → закрыть. Ошибка или пропуск модели не
///     останавливает остальные; «Прервать» останавливает после закрытия текущей.
/// </summary>
public sealed class CheckRunner(UIApplication uiApplication, CheckerSettings settings)
{
    private readonly ModelOpener _opener = new(uiApplication);

    public RunSummary Run(IReadOnlyList<CheckProfile> profiles)
    {
        TempWorkspace.CleanupUnused();
        var jobs = profiles.SelectMany(profile => profile.Models.Select(model => (Profile: profile, Model: model))).ToList();

        return Execute($"проверка {jobs.Count} моделей", (progress, log, history, outcomes) =>
        {
            for (var index = 0; index < jobs.Count; index++)
            {
                var (profile, model) = jobs[index];
                progress.Stage($"Профиль «{profile.Name}»");
                progress.Report(index, jobs.Count, $"{index + 1} из {jobs.Count}: {model.Name}");

                var outcome = CheckServerModel(profile, model, progress, log);
                outcomes.Add(outcome);
                history?.Add(outcome);
                if (progress.IsCancelled) return true;
            }

            progress.Report(jobs.Count, jobs.Count);
            return false;
        });
    }

    /// <summary>Проверяет уже открытую модель по правилам профиля — без открытия и закрытия.</summary>
    public RunSummary RunActive(Document document, CheckProfile profile)
    {
        var path = ActivePath(document);
        var name = path.Length > 0 ? ModelNames.FromPath(path) : document.Title;

        return Execute($"проверка открытой модели {name}", (progress, log, history, outcomes) =>
        {
            progress.Stage($"Профиль «{profile.Name}»");
            progress.BeginItem(name);
            var stopwatch = Stopwatch.StartNew();
            ModelOutcome outcome;
            try
            {
                outcome = CheckDocument(document, profile, name, path, profile.ViewName, stopwatch, progress, log);
            }
            catch (OperationCanceledException) when (progress.IsCancelled)
            {
                outcome = new ModelOutcome(profile.Name, name, path, ModelStatus.Skipped, stopwatch.Elapsed);
            }
            catch (CheckException exception)
            {
                log.Step(name, $"не проверена: {exception.Message}");
                outcome = new ModelOutcome(profile.Name, name, path, ModelStatus.Failed, stopwatch.Elapsed, Error: exception.Message);
            }
            catch (Exception exception)
            {
                log.Error(name, "проверка не выполнена", exception);
                outcome = new ModelOutcome(profile.Name, name, path, ModelStatus.Failed, stopwatch.Elapsed, Error: exception.Message);
            }
            finally
            {
                progress.EndItem();
            }

            outcomes.Add(outcome);
            history?.Add(outcome);
            return progress.IsCancelled;
        });
    }

    /// <summary>Путь открытой модели: у локальной копии — путь хранилища, чтобы имя совпало с проверкой с сервера.</summary>
    public static string ActivePath(Document document)
    {
        if (document.IsWorkshared && !document.IsDetached)
        {
            try
            {
                var central = document.GetWorksharingCentralModelPath();
                if (central is not null) return ModelPathUtils.ConvertModelPathToUserVisiblePath(central);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // нет пути к хранилищу — возьмём путь файла
            }
        }

        return document.PathName;
    }

    private RunSummary Execute(string title, Func<ToadProgress, RunLog, HistoryBuffer?, List<ModelOutcome>, bool> body)
    {
        var started = DateTime.Now;
        var folder = Path.GetFullPath(settings.ReportsFolder.Trim());
        Directory.CreateDirectory(folder);

        using var log = RunLog.Create(folder, started);
        log.Line($"ParamChecker — {title}");
        log.Line($"Отчёты: {folder}");
        log.Line($"Связи при открытии: {(settings.SkipLinks ? "не загружать" : "загружать")}; " +
                 $"в листах — {(settings.ProblemsOnly ? "только проблемные элементы" : "все элементы")}");
        log.Line(settings.HistoryEnabled ? $"База для дашбордов: {settings.HistoryPath()}" : "База для дашбордов: не ведётся");
        log.Blank();

        var history = settings.HistoryEnabled ? new HistoryBuffer(settings.HistoryPath(), started, log) : null;
        var outcomes = new List<ModelOutcome>();
        bool cancelled;
        using (var progress = ToadProgress.Show("ParamChecker", new ProgressOptions
               {
                   Icon = SymbolRegular.DocumentSearch24,
                   SkipText = "Пропустить модель",
                   CancelHint = "Прерываем — текущая модель будет закрыта без отчёта"
               }))
        {
            cancelled = body(progress, log, history, outcomes);
            if (history is not null)
            {
                progress.Stage("Запись базы для дашбордов");
                history.Flush(final: true);
            }
        }

        log.Blank();
        log.Line($"Проверено: {outcomes.Count(o => o.Status == ModelStatus.Checked)}, пропущено: " +
                 $"{outcomes.Count(o => o.Status == ModelStatus.Skipped)}, с ошибками: {outcomes.Count(o => o.Status == ModelStatus.Failed)}");
        if (cancelled) log.Line("Проверка прервана пользователем");

        return new RunSummary(outcomes, cancelled, log.FilePath, folder, history?.Error);
    }

    private ModelOutcome CheckServerModel(CheckProfile profile, ModelSource model, ToadProgress progress, RunLog log)
    {
        var name = model.Name;
        var path = model.Path.Trim();
        var stopwatch = Stopwatch.StartNew();
        progress.BeginItem(name, canSkip: true);

        // Копия модели без связей удаляется, когда документ уже закрыт.
        using var temp = TempWorkspace.Create();
        Document? document = null;
        try
        {
            document = _opener.Open(path, model.WorksetKeyword ?? profile.WorksetKeyword, settings.SkipLinks, temp, log, progress);
            return CheckDocument(document, profile, name, path, model.ViewName ?? profile.ViewName, stopwatch, progress, log);
        }
        catch (Exception exception) when (progress.ShouldAbortItem)
        {
            // Revit прервал открытие по нашей просьбе или модель бросили посреди проверки — это решение пользователя.
            log.Step(name, progress.IsCancelled ? "проверка прервана" : $"пропущена пользователем ({exception.GetType().Name})");
            return new ModelOutcome(profile.Name, name, path, ModelStatus.Skipped, stopwatch.Elapsed);
        }
        catch (CheckException exception)
        {
            log.Step(name, $"не проверена: {exception.Message}");
            return new ModelOutcome(profile.Name, name, path, ModelStatus.Failed, stopwatch.Elapsed, Error: exception.Message);
        }
        catch (Exception exception)
        {
            log.Error(name, "проверка не выполнена", exception);
            return new ModelOutcome(profile.Name, name, path, ModelStatus.Failed, stopwatch.Elapsed, Error: exception.Message);
        }
        finally
        {
            Close(document, name, progress, log);
            progress.EndItem();
        }
    }

    private ModelOutcome CheckDocument(Document document, CheckProfile profile, string name, string path, string? viewName,
        Stopwatch stopwatch, ToadProgress progress, RunLog log)
    {
        var report = ModelChecker.Check(document, profile, name, path, viewName ?? "", settings.ProblemsOnly, progress, log);

        progress.ItemStatus("Сохранение отчёта");
        var target = Path.Combine(Path.GetFullPath(settings.ReportsFolder.Trim()), ModelNames.ToFileName(profile.Name),
            ModelNames.ToFileName(name) + "_LOIReport.xlsx");
        var saved = ReportWriter.Write(report, target, settings.ProblemsOnly);
        log.Step(name, $"готово за {stopwatch.Elapsed:mm\\:ss}: полнота {ModelChecker.Percent(report.Completeness)}, " +
                       $"заполненность {ModelChecker.Percent(report.FillRate)}; отчёт {saved}" +
                       (saved == target ? "" : " (прежний отчёт занят — открыт в Excel?)"));

        return new ModelOutcome(profile.Name, name, path, ModelStatus.Checked, stopwatch.Elapsed, report, ReportPath: saved);
    }

    private static void Close(Document? document, string name, ToadProgress progress, RunLog log)
    {
        if (document is null || !document.IsValidObject) return;

        progress.ItemStatus("Закрытие модели");
        try
        {
            document.Close(false);
        }
        catch (Exception exception)
        {
            log.Error(name, "модель не закрылась", exception);
        }
    }

    /// <summary>
    ///     Дописывает модели в базу после каждой, чтобы итоги не пропали, если Revit упадёт посреди запуска. Пока файл
    ///     занят (открыт в Excel), строки копятся; если к концу запуска он так и не освободился — пишутся в соседний файл.
    /// </summary>
    private sealed class HistoryBuffer(string path, DateTime run, RunLog log)
    {
        private readonly List<ModelOutcome> _pending = [];

        public string? Error { get; private set; }

        public void Add(ModelOutcome outcome)
        {
            _pending.Add(outcome);
            Flush(final: false);
        }

        public void Flush(bool final)
        {
            if (_pending.Count == 0) return;
            try
            {
                HistoryWriter.Append(path, run, _pending);
                _pending.Clear();
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                log.Line($"База для дашбордов не записана: {exception.Message}");
                if (!final) return;
            }

            var alternative = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,
                $"{Path.GetFileNameWithoutExtension(path)} ({run:yyyy-MM-dd HH-mm}).xlsx");
            try
            {
                HistoryWriter.Append(alternative, run, _pending);
                Error = $"Файл базы был занят — итоги этого запуска записаны в {alternative}. Перенесите их в основную базу или " +
                        "просто запустите проверку ещё раз, когда файл освободится.";
                log.Line($"База для дашбордов записана в {alternative}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Error = $"Итоги этого запуска не записаны в базу для дашбордов: {exception.Message}";
            }

            _pending.Clear();
        }
    }
}
