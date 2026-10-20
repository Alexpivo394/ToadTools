using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.UI;
using SmartTags.Models;
using SmartTags.Services;
using SmartTags.ViewModels;
using SmartTags.Views;
using ToadTools.UI.Models;
using ToadTools.UI.Progress;
using ToadTools.UI.Services;

namespace SmartTags;

/// <summary>
///     Сценарий команды: проверка вида → окно настроек → (блокировка 3D) → расстановка с прогрессом → отчёт.
/// </summary>
public static class SmartTagsLauncher
{
    private const string Title = "Умные выноски";

    public static void Run(UIApplication application)
    {
        // Команду запустили заново, не закончив выбор на виде, — панель выбора больше не нужна.
        _pickPanel?.Close();

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

        var view = TagView.TryCreate(document, uiDocument.ActiveView, out var viewError);
        if (view is null)
        {
            Warn(viewError + "\n\nОткройте план, разрез, фасад или 3D-вид и запустите плагин снова.");
            return;
        }

        var scanner = new CategoryScanner(document, view.View);
        var viewCategories = scanner.ScanView();
        if (viewCategories.Count == 0)
        {
            Warn(scanner.EmptyHint ?? "На виде нет элементов, которые можно промаркировать.");
            return;
        }

        var settings = ShowForm(application, uiDocument, view, scanner, viewCategories);
        if (settings is null) return;

        if (view.IsUnlocked3D && !ConfirmLock(view)) return;

        var result = RunWithProgress(application, view, settings);

        // Заблокировали копию 3D-вида — переключаемся на неё, марки стоят там.
        if (result.LockedCopyId is { } copyId && document.GetElement(copyId) is View copy)
            uiDocument.ActiveView = copy;

        var reportViewModel = new SmartTagsReportViewModel(result, settings);
        var report = new SmartTagsReportView(reportViewModel);
        SetOwner(report, application);
        report.ShowDialog();

        if (reportViewModel.ElementsToSelect is { Count: > 0 } toSelect)
        {
            uiDocument.Selection.SetElementIds(toSelect);
            try
            {
                uiDocument.ShowElements(toSelect);
            }
            catch
            {
                // Показать не удалось (например, элементы скрыты) — выделения достаточно.
            }
        }
    }

    /// <summary>
    ///     Окно настроек. Кнопка «Выбрать на виде» закрывает окно и завершает команду: пользователь выбирает
    ///     элементы обычным выбором Revit, а по «Готово» на панели выбора команда запускается снова —
    ///     уже в режиме «только выбранные».
    /// </summary>
    private static SmartTagsSettings? ShowForm(UIApplication application, UIDocument uiDocument, TagView view,
        CategoryScanner scanner, List<TaggableCategory> viewCategories)
    {
        var selection = scanner.WithInsulation(uiDocument.Selection.GetElementIds());
        var selectionCategories = scanner.ScanSelection(selection);
        if (selection.Count > 0 && selectionCategories.Count == 0)
        {
            Warn(scanner.EmptyHint + "\n\nБудут промаркированы все видимые элементы вида — " +
                 "или выберите подходящие элементы кнопкой «Выбрать на виде».");
            selection = [];
        }

        var session = new SmartTagsSession(view, viewCategories, selectionCategories, selection.ToList());
        var viewModel = new SmartTagsViewModel(session, new SmartTagsSettingsStore());
        var form = new SmartTagsView(viewModel);
        SetOwner(form, application);

        if (form.ShowDialog() == true) return viewModel.Result;
        if (viewModel.IsPicking) StartPicking(application);
        return null;
    }

    /// <summary>
    ///     Показывает панель «Готово / Отмена» и отдаёт управление Revit: внутри команды доступен только
    ///     режим выбора API (без привычных Ctrl/Shift и рамки в обе стороны), а так работает обычный выбор Revit.
    /// </summary>
    private static void StartPicking(UIApplication application)
    {
        // Событие создаётся только в контексте API — здесь, внутри команды; потом переиспользуется.
        _resume ??= ExternalEvent.Create(new ResumeHandler());

        var panel = new PickPanelView(application.MainWindowHandle);
        panel.Done += (_, _) => _resume.Raise();
        panel.Closed += (_, _) =>
        {
            if (ReferenceEquals(_pickPanel, panel)) _pickPanel = null;
        };

        _pickPanel = panel;
        panel.Show();
    }

    private static PickPanelView? _pickPanel;
    private static ExternalEvent? _resume;

    /// <summary>По «Готово» на панели выбора — снова запускает команду с текущим выделением.</summary>
    private sealed class ResumeHandler : IExternalEventHandler
    {
        public void Execute(UIApplication application)
        {
            try
            {
                Run(application);
            }
            catch (Exception ex)
            {
                ToadDialogService.Show(Title, ex.Message, DialogButtons.OK, DialogIcon.Error);
            }
        }

        public string GetName() => Title;
    }

    private static RunResult RunWithProgress(UIApplication application, TagView view, SmartTagsSettings settings)
    {
        // Окно прогресса само блокирует окна Revit на время работы и живёт в своём потоке.
        using var progress = ToadProgress.Show(Title, new ProgressOptions { Icon = Wpf.Ui.Controls.SymbolRegular.TagMultiple24 });
        return new SmartTagsRunner(view, settings).Run(progress);
    }

    private static bool ConfirmLock(TagView view)
    {
        var rename = ViewLocker.IsDefaultName(view.Name)
            ? $"\n\nСтандартный вид «{view.Name}» без имени не заблокировать: он получит имя «{ViewLocker.LockedViewName}»."
            : string.Empty;

        var answer = ToadDialogService.Show(Title,
            $"Марки на 3D-виде можно поставить только после блокировки ориентации вида «{view.Name}». " +
            "Заблокировать вид и продолжить? Снять блокировку можно потом командой Revit «Разблокировать вид»." + rename,
            DialogButtons.YesNo, DialogIcon.Warning);
        return answer == "Yes";
    }

    private static void Warn(string message) => ToadDialogService.Show(Title, message, DialogButtons.OK, DialogIcon.Warning);

    private static void SetOwner(Window window, UIApplication application)
    {
        new WindowInteropHelper(window).Owner = application.MainWindowHandle;
    }
}
