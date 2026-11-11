using ParamToFinish.ViewModels;
using ToadDesign;
using ToadTools.UI.Services;

namespace ParamToFinish.Views;

public sealed partial class ParamToFinishView
{
    public ParamToFinishView(ParamToFinishViewModel viewModel)
    {
        InitializeComponent();
        
        ThemePreference.Apply();
        
        DataContext = viewModel;
        
        viewModel.CloseRequested += (_, _) => Close();
    }
}