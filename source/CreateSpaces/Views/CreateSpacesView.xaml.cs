using CreateSpaces.ViewModels;
using ToadDesign;
using ToadTools.UI.Services;

namespace CreateSpaces.Views;

public sealed partial class CreateSpacesView
{
    public CreateSpacesView(CreateSpacesViewModel viewModel)
    {
        InitializeComponent();
        
        ThemePreference.Apply();
        
        DataContext = viewModel;
    }
}