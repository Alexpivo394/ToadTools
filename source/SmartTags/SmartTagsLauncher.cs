using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SmartTags.Models;
using SmartTags.Services;
using SmartTags.ViewModels;
using SmartTags.Views;
using ToadTools.UI.Models;
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
    ///     Окно настроек. Кнопка «Выбрать на виде» закрывает окно, даёт выбрать элементы в модели
    ///     и открывает окно снова — уже в режиме «только выбранные».
    /// </summary>
    private static SmartTagsSettings? ShowForm(UIApplication application, UIDocument uiDocument, TagView view,
        CategoryScanner scanner, List<TaggableCategory> viewCategories)
    {
        while (true)
        {
            var selection = uiDocument.Selection.GetElementIds();
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
            if (!viewModel.IsPicking) return null;

            PickElements(uiDocument, view, selection);
        }
    }

    /// <summary>Выбор элементов на виде: доступны только категории, которые плагин умеет маркировать.</summary>
    private static void PickElements(UIDocument uiDocument, TagView view, ICollection<ElementId> current)
    {
        var preselected = current
            .Select(view.Document.GetElement)
            .Where(element => element is not null)
            .Select(element => new Reference(element))
            .ToList();

        try
        {
            var picked = uiDocument.Selection.PickObjects(ObjectType.Element, new TaggableFilter(),
                "Выберите элементы для маркировки и нажмите «Готово» (Esc — отмена)", preselected);
            uiDocument.Selection.SetElementIds(picked.Select(reference => reference.ElementId).ToList());
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            // Esc — оставляем прежнее выделение и возвращаемся в окно.
        }
    }

    private sealed class TaggableFilter : ISelectionFilter
    {
        public bool AllowElement(Element element)
        {
            return element is not ElementType && TagCategoryMap.IsTaggable(RevitCompat.CategoryOf(element));
        }

        public bool AllowReference(Reference reference, XYZ position) => false;
    }

    private static RunResult RunWithProgress(UIApplication application, TagView view, SmartTagsSettings settings)
    {
        var progressViewModel = new ProgressViewModel();
        var progress = new ProgressView(progressViewModel);
        SetOwner(progress, application);

        // Пока идёт работа, окно Revit недоступно — как при модальном окне. Иначе во время прокачки
        // сообщений можно было бы нажать что-то в Revit посреди открытой транзакции.
        var revitWindow = application.MainWindowHandle;
        EnableWindow(revitWindow, false);
        try
        {
            progress.Show();
            return new SmartTagsRunner(view, settings).Run(progressViewModel);
        }
        finally
        {
            EnableWindow(revitWindow, true);
            progress.Close();
        }
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool enable);
}
