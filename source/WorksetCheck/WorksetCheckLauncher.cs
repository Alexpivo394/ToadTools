using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.UI;
using ToadTools.UI.Models;
using ToadTools.UI.Progress;
using ToadTools.UI.Services;
using WorksetCheck.Models;
using WorksetCheck.Services;
using WorksetCheck.ViewModels;
using WorksetCheck.Views;

namespace WorksetCheck;

/// <summary>
///     Сценарий команды: окно с именами РН → проверка открытой модели с прогрессом → отчёт
///     (из него — исправление) → при необходимости открыть 3D-вид или выделить элемент.
/// </summary>
public static class WorksetCheckLauncher
{
    private const string Title = WorksetCheckSession.Title;

    public static void Run(UIApplication application)
    {
        var uiDocument = application.ActiveUIDocument;
        if (uiDocument is null)
        {
            Warn("Нет открытого документа.");
            return;
        }

        var document = uiDocument.Document;
        if (document.IsFamilyDocument)
        {
            Warn("Плагин работает только в проекте, не в редакторе семейств.");
            return;
        }

        if (!document.IsWorkshared)
        {
            Warn("В модели нет рабочих наборов — проверять нечего.");
            return;
        }

        var rules = AskRules(application, document);
        if (rules is null) return;

        var session = new WorksetCheckSession(document, rules, uiDocument.ActiveView.Id);
        CheckResult result;
        try
        {
            using var progress = ToadProgress.Show(Title, WorksetCheckSession.ProgressOptions);
            result = session.Check(progress);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (result.Issues.Count == 0 && result.ClosedWorksets.Count == 0)
        {
            ToadDialogService.Show(Title,
                $"Рабочие наборы в порядке: проверено элементов — {result.CheckedElements}, замечаний нет.",
                DialogButtons.OK, DialogIcon.Info);
            return;
        }

        var reportViewModel = new WorksetCheckReportViewModel(session, result);
        var report = new WorksetCheckReportView(reportViewModel);
        SetOwner(report, application);
        report.ShowDialog();

        if (reportViewModel.ViewToOpen is { } viewId && document.GetElement(viewId) is View view)
        {
            uiDocument.ActiveView = view;
        }
        else if (reportViewModel.ElementsToSelect is { Count: > 0 } toSelect)
        {
            uiDocument.Selection.SetElementIds(toSelect);
            try
            {
                uiDocument.ShowElements(toSelect);
            }
            catch
            {
                // Показать не удалось (например, элемент скрыт на всех видах) — выделения достаточно.
            }
        }
    }

    /// <summary>Окно с именами РН: прошлые значения или найденные в модели. Null — окно закрыли.</summary>
    private static WorksetRules? AskRules(UIApplication application, Document document)
    {
        var configuration = new Configuration.Configuration();
        var settings = configuration.LoadSettings() ?? new Configuration.Settings();

        var worksets = new FilteredWorksetCollector(document)
            .OfKind(WorksetKind.UserWorkset)
            .ToWorksets()
            .Select(workset => workset.Name)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var detected = new WorksetDetector(document).Detect();

        var viewModel = new WorksetCheckViewModel(worksets, detected, settings);
        var form = new WorksetCheckView(viewModel);
        SetOwner(form, application);
        var confirmed = form.ShowDialog() == true;

        configuration.SaveSettings(viewModel.ToSettings());
        return confirmed ? viewModel.ToRules() : null;
    }

    private static void Warn(string message) => ToadDialogService.Show(Title, message, DialogButtons.OK, DialogIcon.Warning);

    private static void SetOwner(Window window, UIApplication application)
    {
        new WindowInteropHelper(window).Owner = application.MainWindowHandle;
    }
}
