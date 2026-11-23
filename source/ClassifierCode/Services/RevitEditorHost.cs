using System.IO;
using ClassifierCode.Editor;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Library;
using ClassifierCode.Engine.Profile;
using ClassifierCode.Models;
using Microsoft.Win32;
using ClosedXML.Excel;
using ToadTools.UI.Models;
using ToadTools.UI.Services;

namespace ClassifierCode.Services;

/// <summary>Редактор профиля в Revit: факты элементов, параметры проекта, выделение, файлы и буфер обмена.</summary>
public sealed class RevitEditorHost(Autodesk.Revit.UI.UIDocument uiDocument) : IEditorHost
{
    private const string ProfileFilter = "Профиль классификатора (*.classifier.json)|*.classifier.json|Все файлы (*.*)|*.*";

    private readonly Document _doc = uiDocument.Document;
    private readonly FactsBuilder _facts = new(uiDocument.Document);
    private readonly List<long> _selection = uiDocument.Selection.GetElementIds().Select(RevitCompat.IdValue).ToList();

    public IReadOnlyList<string> ProjectParameterNames()
    {
        var names = new List<string>();
        var iterator = _doc.ParameterBindings.ForwardIterator();
        while (iterator.MoveNext())
            if (iterator.Key != null)
                names.Add(iterator.Key.Name);
        return names.Distinct().ToList();
    }

    public IReadOnlyList<long> CurrentSelection() => _selection;

    /// <summary>Свернуть или вернуть окна плагина (их ставит окно редактора).</summary>
    public Action<bool>? HideWindows { get; set; }

    /// <summary>
    ///     Окна плагина модальные и отключают главное окно Revit. На время выбора сворачиваем их, включаем Revit и
    ///     после выбора возвращаем как было.
    /// </summary>
    public IReadOnlyList<long> PickElements()
    {
        var revit = uiDocument.Application.MainWindowHandle;
        HideWindows?.Invoke(true);
        EnableWindow(revit, true);
        SetForegroundWindow(revit);
        try
        {
            var picked = uiDocument.Selection.PickObjects(Autodesk.Revit.UI.Selection.ObjectType.Element,
                "Выберите элементы для проверки и нажмите «Готово» (Esc — отмена)");
            return picked.Select(r => RevitCompat.IdValue(r.ElementId)).ToList();
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return [];
        }
        finally
        {
            EnableWindow(revit, false);
            HideWindows?.Invoke(false);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnableWindow(IntPtr hWnd, bool enable);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private Element? Element(long id) => _doc.GetElement(RevitCompat.ToId(id));

    public IElementFacts? Facts(long id) => Element(id) is { } element ? _facts.Get(element) : null;

    public string Label(long id) => Element(id) is { } element ? ClassifierReport.Describe(element) + " · " + id : id.ToString();

    public IReadOnlyList<long> CollectScope(RunScope scope, ClassifierProfile profile)
    {
        var code = profile.Outputs.FirstOrDefault(o => o.Field == "code");
        if (code == null) return [];
        var categories = ElementCollector.Categories(profile.Categories);
        return new ElementCollector()
            .Collect(_doc, _doc.ActiveView, scope, _selection.Select(RevitCompat.ToId).ToList(), categories, code)
            .Select(e => RevitCompat.IdValue(e.Id)).ToList();
    }

    public string CurrentCode(long id, ClassifierProfile profile)
    {
        var code = profile.Outputs.FirstOrDefault(o => o.Field == "code");
        var element = Element(id);
        if (code == null || element == null) return string.Empty;
        var parameter = ClassifierParameterWriter.Find(element, code) ??
                        (_doc.GetElement(element.GetTypeId()) is { } type ? ClassifierParameterWriter.Find(type, code) : null);
        return parameter?.AsString() ?? string.Empty;
    }

    public void Select(IEnumerable<long> ids) => uiDocument.Selection.SetElementIds(ids.Select(RevitCompat.ToId).ToList());

    /// <summary>Первый непустой лист книги Excel, все ячейки как текст.</summary>
    public IReadOnlyList<string[]>? ReadXlsx()
    {
        var dialog = new OpenFileDialog { Title = "Классификатор в Excel", Filter = "Книга Excel (*.xlsx)|*.xlsx" };
        if (dialog.ShowDialog() != true) return null;

        try
        {
            using var stream = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); // книга может быть открыта в Excel
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheets.FirstOrDefault(s => !s.IsEmpty());
            if (sheet?.RangeUsed() is not { } used) return [];

            var rows = new List<string[]>();
            var columns = used.LastColumn().ColumnNumber();
            for (var r = used.FirstRow().RowNumber(); r <= used.LastRow().RowNumber(); r++)
            {
                var row = new string[columns];
                for (var c = 1; c <= columns; c++) row[c - 1] = CellText(sheet.Cell(r, c));
                rows.Add(row);
            }

            return rows;
        }
        catch (Exception exception) // книга повреждена, не xlsx или занята — это «файл не прочитан», а не сбой плагина
        {
            ToadDialogService.Show("Файл не прочитан", exception.Message, DialogButtons.OK, DialogIcon.Error);
            return null;
        }
    }

    /// <summary>Текст ячейки, как его показывает Excel; у формулы — сохранённый результат.</summary>
    private static string CellText(IXLCell cell) =>
        cell.HasFormula ? cell.CachedValue.ToString(System.Globalization.CultureInfo.CurrentCulture) : cell.GetFormattedString();

    public string? ClipboardText() => System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null;

    public string? AskSavePath(string defaultName)
    {
        var dialog = new SaveFileDialog { Title = "Экспорт профиля", Filter = ProfileFilter, FileName = defaultName };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? AskOpenPath()
    {
        var dialog = new OpenFileDialog { Title = "Импорт профиля", Filter = ProfileFilter };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

/// <summary>Вопросы редактора через диалоги ToadTools.</summary>
public sealed class ToadDialogs : IDialogs
{
    public bool? Ask(string title, string message) =>
        ToadDialogService.Show(title, message, DialogButtons.YesNoCancel, DialogIcon.Warning) switch
        {
            "Yes" => true,
            "No" => false,
            _ => null
        };

    public void Info(string title, string message) => ToadDialogService.Show(title, message, DialogButtons.OK, DialogIcon.Info);
}
