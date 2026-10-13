using Autodesk.Revit.DB;
using ToadTools.UI.Models;
using ToadTools.UI.Services;

namespace CableTrays;

/// <summary>
///     Итоговое сообщение команды: статистика + список замечаний.
///     Если есть замечания — окно с предупреждением, иначе — информационное.
/// </summary>
internal sealed class TrayReport
{
    private const int MaxItems = 15;

    private readonly string _header;
    private readonly List<string> _lines = new();
    private readonly List<string> _warnings = new();

    public TrayReport(string header)
    {
        _header = header;
    }

    public bool HasWarnings => _warnings.Count > 0;

    public void Line(string text) => _lines.Add(text);

    /// <summary>Строка статистики, выводится только если count > 0.</summary>
    public void Count(string text, int count)
    {
        if (count > 0) _lines.Add($"  • {text}: {count}");
    }

    /// <summary>Замечание со списком элементов (выводятся первые 15).</summary>
    public void Warning(string text, IReadOnlyCollection<string> items)
    {
        if (items.Count == 0) return;

        var block = $"⚠ {text}: {items.Count}\n" + string.Join("\n", items.Take(MaxItems).Select(i => "    " + i));
        if (items.Count > MaxItems) block += $"\n    ... и ещё {items.Count - MaxItems}";
        _warnings.Add(block);
    }

    public void Show()
    {
        var message = _header;
        if (_lines.Count > 0) message += "\n\n" + string.Join("\n", _lines);
        if (_warnings.Count > 0) message += "\n\n" + string.Join("\n\n", _warnings);

        ToadDialogService.Show(
            HasWarnings ? "Готово, есть замечания" : "Готово",
            message,
            DialogButtons.OK,
            HasWarnings ? DialogIcon.Warning : DialogIcon.Info
        );
    }

    public static string Describe(Element element) => $"ID {element.Id} — {element.Name}";

    public static void ShowWarning(string message)
    {
        ToadDialogService.Show("Внимание", message, DialogButtons.OK, DialogIcon.Warning);
    }

    /// <summary>
    ///     Запускает команду и показывает понятное сообщение при любой ошибке.
    ///     Транзакции внутри должны быть в using — тогда при ошибке изменения откатываются.
    /// </summary>
    public static void Run(string commandName, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            ToadDialogService.Show(
                "Ошибка",
                $"Команда «{commandName}» не выполнена, изменения в модели отменены.\n\n" +
                $"Причина: {e.Message}\n\n" +
                $"Подробности для разработчика ({e.GetType().Name}):\n{e.StackTrace}",
                DialogButtons.OK,
                DialogIcon.Error
            );
        }
    }
}
