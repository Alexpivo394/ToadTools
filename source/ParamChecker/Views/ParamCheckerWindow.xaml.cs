using System.ComponentModel;
using ParamChecker.ViewModels;
using ToadTools.UI.Services;
using Wpf.Ui.Appearance;

namespace ParamChecker.Views;

public sealed partial class ParamCheckerWindow
{
    private readonly MainViewModel _viewModel;

    public ParamCheckerWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        ThemeWatcherService.Initialize();
        ThemeWatcherService.Watch(this);
        ThemeWatcherService.ApplyTheme(viewModel.Settings.DarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light);

        _viewModel = viewModel;
        DataContext = viewModel;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.Save();
        base.OnClosing(e);
    }
}
