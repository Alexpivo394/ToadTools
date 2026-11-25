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
        // Карточка не выше половины панели: на низком экране список не сжимается в ноль, карточка прокручивается.
        SizeChanged += (_, _) => CardScroll.MaxHeight = Math.Max(160, ActualHeight / 2);
    }
}
