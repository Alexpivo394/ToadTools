using CreateCover.ViewModels;
using ToadDesign;
using ToadTools.UI.Services;

namespace CreateCover.Views;

public sealed partial class CreateCoverView
{
    public CreateCoverView(CreateCoverViewModel viewModel)
    {
        InitializeComponent();
        
        ToadTheme.Mode = ToadThemeMode.Dark;
        
        DataContext = viewModel;
    }
}