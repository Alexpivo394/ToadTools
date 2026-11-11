using ToadDesign;
using System.ComponentModel;
using ParamChecker.ViewModels;
using ToadTools.UI.Services;

namespace ParamChecker.Views;

public sealed partial class ParamCheckerWindow
{
    private readonly MainViewModel _viewModel;

    public ParamCheckerWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        ThemePreference.Apply();

        _viewModel = viewModel;
        DataContext = viewModel;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.Save();
        base.OnClosing(e);
    }
}
