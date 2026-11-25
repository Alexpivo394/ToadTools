using System.Text.RegularExpressions;

namespace ToadTools.Telemetry;

/// <summary>Команда ленты для счётчика: идентификатор, подпись для людей, источник и вкладка.</summary>
public sealed record NamedCommand(string CommandId, string Name, string Source, string? Tab);

/// <summary>Как называть запуск команды с ленты: источник по вкладке и идентификатору, подпись — одной строкой.</summary>
public static class CommandNaming
{
    public const string ToadToolsTab = "ToadTools";

    /// <summary>Вкладки самого Revit (русский и английский интерфейс): кнопки на них — штатные команды.</summary>
    private static readonly HashSet<string> RevitTabs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Архитектура", "Конструкция", "Сталь", "Сборные конструкции", "Системы", "Вставка", "Аннотации", "Анализ", "Формы и генплан",
        "Совместная работа", "Вид", "Управление", "Изменить", "Создание",
        "Architecture", "Structure", "Steel", "Precast", "Systems", "Insert", "Annotate", "Analyze", "Massing & Site", "Collaborate", "View",
        "Manage", "Modify", "Create"
    };

    private static readonly Regex Spaces = new(@"\s+");

    /// <summary>
    ///     null — запуск не записывается: нет идентификатора или это кнопка ToadTools (её считает сама команда, иначе двойной счёт).
    /// </summary>
    public static NamedCommand? From(string? id, string? text, string? tab)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (string.Equals(tab, ToadToolsTab, StringComparison.OrdinalIgnoreCase)) return null;
        // Вкладку не нашли, но кнопка своя: Revit пишет вкладку в идентификатор кнопки надстройки.
        if (id!.IndexOf("%" + ToadToolsTab + "%", StringComparison.OrdinalIgnoreCase) >= 0) return null;
        var commandId = id!.Trim();
        var name = Spaces.Replace(text ?? "", " ").Trim();
        var cleanTab = string.IsNullOrWhiteSpace(tab) ? null : tab!.Trim();
        // Кнопки сторонних надстроек Revit называет CustomCtrl_%…; штатные команды — ID_….
        var source = commandId.StartsWith("CustomCtrl_", StringComparison.Ordinal) ? "Addin"
            : commandId.StartsWith("ID_", StringComparison.Ordinal) || (cleanTab is not null && RevitTabs.Contains(cleanTab)) ? "Revit"
            : "Addin";
        return new NamedCommand(commandId, name.Length > 0 ? name : commandId, source, cleanTab);
    }

    private const string SplitSuffix = "_RibbonListButton";

    /// <summary>
    ///     Какой пункт разделённой кнопки запустила её основная часть, чтобы запуск с кнопки и из её списка был одной командой.
    ///     Синхронизированная кнопка знает текущий пункт; у штатной кнопки Revit без синхронизации id — «id пункта» +
    ///     <c>_RibbonListButton</c>. null — пункт не узнать, считается сама кнопка.
    /// </summary>
    public static string? SplitTarget(string? buttonId, string? currentId, IEnumerable<string?> itemIds)
    {
        if (!string.IsNullOrWhiteSpace(currentId)) return currentId;
        if (buttonId is null || !buttonId.EndsWith(SplitSuffix, StringComparison.Ordinal)) return null;
        var main = buttonId.Substring(0, buttonId.Length - SplitSuffix.Length);
        return itemIds.Contains(main, StringComparer.Ordinal) ? main : null;
    }

    /// <summary>Идентификатор команды ToadTools — имя класса без «Command».</summary>
    public static string ToadToolsId(string typeName) =>
        typeName.EndsWith("Command", StringComparison.Ordinal) ? typeName.Substring(0, typeName.Length - "Command".Length) : typeName;
}
