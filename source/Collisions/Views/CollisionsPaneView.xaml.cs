using Collisions.ViewModels;

namespace Collisions.Views;

/// <summary>
///     Содержимое закрепляемой панели. Тема — светлая из словаря ToadTools.UI: окно-хозяин панели — главное окно Revit,
///     поэтому <c>ThemeWatcherService</c> (он перекрашивает фон окна) сюда не подключается.
/// </summary>
public sealed partial class CollisionsPaneView
{
    public CollisionsPaneView(PaneViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
