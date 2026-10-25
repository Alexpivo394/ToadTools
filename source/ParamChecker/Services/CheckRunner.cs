using System.Diagnostics;
using System.IO;
using Alexpivo394.Revit.ParamCheck.Core;
using Alexpivo394.Revit.ParamCheck.Profiles;
using Alexpivo394.Revit.ParamCheck.Reports;
using Alexpivo394.Revit.ParamCheck.Services;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ToadTools.UI.Progress;
using Wpf.Ui.Controls;

namespace ParamChecker.Services;

/// <param name="HistoryError">База не дописалась туда, куда настроено; текст — что случилось и куда записано.</param>
/// <param name="ToadFlow">Отправка итогов в аналитику ToadFlow; null — отправка выключена.</param>
public sealed record RunSummary(IReadOnlyList<ModelOutcome> Outcomes, bool Cancelled, string LogPath, string ReportsFolder, string? HistoryError,
    SendResult? ToadFlow = null);

/// <summary>
///     Проверяет модели по одной: открыть → проверить правила → отчёт → закрыть (<see cref="ModelInspector" />). Ошибка или
///     пропуск модели не останавливает остальные; «Прервать» останавливает после закрытия текущей.
/// </summary>
public sealed class CheckRunner(UIApplication uiApplication, CheckerSettings settings)
{
    private readonly ModelInspector _inspector = new(uiApplication.Application);
    private readonly ToadFlowSender _toadFlow = new(settings);

    public RunSummary Run(IReadOnlyList<CheckProfile> profiles)
    {
        var jobs = profiles.SelectMany(profile => profile.Models.Select(model => (Profile: profile, Model: model))).ToList();

        return Execute($"проверка {jobs.Count} моделей", (progress, log, history, outcomes) =>
        {
            for (var index = 0; index < jobs.Count; index++)
            {
                var (profile, model) = jobs[index];
                progress.Stage($"Профиль «{profile.Name}»");
                progress.Report(index, jobs.Count, $"{index + 1} из {jobs.Count}: {model.Name}");

                progress.BeginItem(model.Name, canSkip: true);
                ModelOutcome outcome;
                try
                {
                    outcome = _inspector.Check(model.Path, profile, Options(model.ViewName ?? profile.ViewName, model.WorksetKeyword ?? profile.WorksetKeyword),
                        ReportPath(profile, model.Name), new ItemProgress(progress), log);
                }
                finally
                {
                    progress.EndItem();
                }

                outcomes.Add(outcome);
                history?.Add(outcome);
                Enqueue(outcome, profile, log);
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
                outcome = ModelInspector.CheckOpen(document, profile, name, path, Options(profile.ViewName, ""), ReportPath(profile, name),
                    new ItemProgress(progress), log, stopwatch);
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
            Enqueue(outcome, profile, log);
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

    private CheckOptions Options(string? viewName, string? worksetKeyword) => new()
    {
        SkipLinks = settings.SkipLinks,
        ViewName = viewName ?? "",
        WorksetKeyword = worksetKeyword ?? "",
        ProblemsOnly = settings.ProblemsOnly
    };

    private string ReportPath(CheckProfile profile, string modelName) =>
        Path.Combine(Path.GetFullPath(settings.ReportsFolder.Trim()), ModelNames.ToFileName(profile.Name), ReportNames.ForModel(modelName));

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
        SendResult? sent = null;
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

            if (_toadFlow.Enabled)
            {
                progress.Stage("Отправка итогов в ToadFlow");
                sent = _toadFlow.Flush();
            }
        }

        log.Blank();
        log.Line($"Проверено: {outcomes.Count(o => o.Status == ModelStatus.Checked)}, пропущено: " +
                 $"{outcomes.Count(o => o.Status == ModelStatus.Skipped)}, с ошибками: {outcomes.Count(o => o.Status == ModelStatus.Failed)}");
        if (sent is not null)
            log.Line($"В ToadFlow отправлено итогов: {sent.Sent}" + (sent.Pending > 0 ? $", ждут отправки: {sent.Pending}" : "") +
                     (sent.Error is null ? "" : $" — {sent.Error}"));
        if (cancelled) log.Line("Проверка прервана пользователем");

        return new RunSummary(outcomes, cancelled, log.FilePath, folder, history?.Error, sent);
    }

    /// <summary>Итог — в очередь ToadFlow сразу после модели: если Revit упадёт посреди запуска, он не потеряется.</summary>
    private void Enqueue(ModelOutcome outcome, CheckProfile profile, RunLog log)
    {
        try
        {
            _toadFlow.Enqueue(outcome, profile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Line($"{outcome.ModelName}: итог не положен в очередь ToadFlow: {exception.Message}");
        }
    }

    /// <summary>Модель в окне прогресса: шаги и ход проверки — в строку модели, «Пропустить» и «Прервать» — просьба бросить её.</summary>
    private sealed class ItemProgress(ToadProgress progress) : ICheckProgress
    {
        public bool ShouldAbort => progress.ShouldAbortItem;

        public void Status(string? status) => progress.ItemStatus(status);

        public void Report(int current, int total, string? status) => progress.ItemReport(current, total, status);
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
