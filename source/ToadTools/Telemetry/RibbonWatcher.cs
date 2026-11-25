using Autodesk.Windows;
using ToadTools.UI.Services;

namespace ToadTools.Telemetry;

/// <summary>
///     Запуски команд с ленты Revit (<see cref="ComponentManager.ItemExecuted" />): штатные команды и чужие надстройки. Кнопки
///     ToadTools пропускаются — их считает сама команда. Ловит ленту и панель быстрого доступа; горячие клавиши, повтор
///     последней команды (Enter, контекстное меню) — нет (проверено в Revit 2024). Основная часть разделённой кнопки считается
///     её пунктом (<see cref="CommandNaming.SplitTarget" />).
///     Обработчик только ищет вкладку кнопки и кладёт запуск в счётчик в памяти.
/// </summary>
internal sealed class RibbonWatcher(Action<CommandRun> record)
{
    /// <summary>Не чаще: обход ленты при неизвестной кнопке — это работа в потоке Revit.</summary>
    private static readonly TimeSpan RebuildEvery = TimeSpan.FromSeconds(30);

    private Dictionary<RibbonItem, string> _tabs = new();
    private DateTime _builtAt = DateTime.MinValue;

    public void Start() => ComponentManager.ItemExecuted += OnExecuted;

    public void Stop() => ComponentManager.ItemExecuted -= OnExecuted;

    private void OnExecuted(object? sender, Autodesk.Internal.Windows.RibbonItemExecutedEventArgs e)
    {
        try
        {
            var item = e.Item;
            if (item is null) return;
            if (item is RibbonListButton list)
            {
                var current = list is RibbonSplitButton { IsSynchronizedWithCurrentItem: true } ? list.Current?.Id : null;
                if (CommandNaming.SplitTarget(list.Id, current, list.Items.Select(i => i.Id)) is { } target &&
                    list.Items.FirstOrDefault(i => i.Id == target) is { } chosen)
                    item = chosen;
            }

            if (!_tabs.TryGetValue(item, out var tab) && DateTime.UtcNow - _builtAt > RebuildEvery)
            {
                _tabs = BuildTabs();
                _builtAt = DateTime.UtcNow;
                _tabs.TryGetValue(item, out tab);
            }

            if (CommandNaming.From(item.Id, item.Text, tab) is { } named)
                record(new CommandRun(named.CommandId, named.Name, named.Source, named.Tab, CommandOutcome.Succeeded, 0));
        }
        catch (Exception exception)
        {
            ErrorReport.Log("Аналитика ToadFlow: лента", exception);
        }
    }

    /// <summary>Кнопка → подпись её вкладки, по всей ленте, включая стопки и выпадающие кнопки.</summary>
    private static Dictionary<RibbonItem, string> BuildTabs()
    {
        var tabs = new Dictionary<RibbonItem, string>();
        if (ComponentManager.Ribbon is not { } ribbon) return tabs;
        foreach (var tab in ribbon.Tabs)
        foreach (var panel in tab.Panels)
            if (panel.Source is { } source)
                Collect(source.Items, tab.Title ?? tab.Id ?? "", tabs);
        return tabs;
    }

    private static void Collect(IEnumerable<RibbonItem> items, string tab, Dictionary<RibbonItem, string> tabs)
    {
        foreach (var item in items)
        {
            tabs[item] = tab;
            if (item is RibbonRowPanel row) Collect(row.Items, tab, tabs);
            if (item is RibbonListButton list) Collect(list.Items, tab, tabs);
        }
    }
}
