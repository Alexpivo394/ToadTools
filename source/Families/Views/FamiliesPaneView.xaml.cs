using Families.ViewModels;
using ToadTools.UI.Services;

namespace Families.Views;

/// <summary>Содержимое закрепляемой панели «Семейства». Корень темы ToadDesign — сама панель.</summary>
public sealed partial class FamiliesPaneView
{
    public FamiliesPaneView(FamiliesPaneViewModel viewModel)
    {
        InitializeComponent();
        ThemePreference.Apply();
        DataContext = viewModel;
    }
}
