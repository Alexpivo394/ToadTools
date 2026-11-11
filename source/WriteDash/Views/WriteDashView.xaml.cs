using ToadTools.UI.Services;
using ToadDesign;
using WriteDash.ViewModels;

namespace WriteDash.Views;

public sealed partial class WriteDashView
{
    public WriteDashView(WriteDashViewModel viewModel)
    {
        InitializeComponent();
        
        ToadTheme.Mode = ToadThemeMode.Dark;
        
        DataContext = viewModel;
    }
}