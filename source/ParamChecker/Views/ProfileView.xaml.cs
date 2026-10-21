using System.Windows;
using System.Windows.Controls.Primitives;

namespace ParamChecker.Views;

public sealed partial class ProfileView
{
    public ProfileView() => InitializeComponent();

    /// <summary>Меню правила открывается обычным щелчком по «⋮», а не только правой кнопкой.</summary>
    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { ContextMenu: { } menu } button) return;

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Left;
        menu.IsOpen = true;
    }
}
