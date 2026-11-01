using System.Windows.Input;
using Collisions.ViewModels;

namespace Collisions.Views;

/// <summary>
///     Содержимое закрепляемой панели. Тема — светлая из словаря ToadTools.UI: окно-хозяин панели — главное окно Revit,
///     поэтому <c>ThemeWatcherService</c> (он перекрашивает фон окна) сюда не подключается.
/// </summary>
public sealed partial class CollisionsPaneView
{
    private readonly PaneViewModel _viewModel;

    public CollisionsPaneView(PaneViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
    }

    /// <summary>↑/↓ ведёт модель панели: ниже последней строки она догружает следующую страницу.</summary>
    private async void OnListKeyDown(object sender, KeyEventArgs e)
    {
        var step = e.Key switch
        {
            Key.Down => 1,
            Key.Up => -1,
            _ => 0
        };
        if (step == 0) return;

        e.Handled = true;
        await _viewModel.MoveAsync(step);
        if (_viewModel.SelectedItem is { } item)
        {
            List.ScrollIntoView(item);
            (List.ItemContainerGenerator.ContainerFromItem(item) as System.Windows.UIElement)?.Focus();
        }
    }
}
