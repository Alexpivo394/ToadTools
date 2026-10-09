using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ParamChecker.ViewModels.PagesViewModels;
using ParamChecker.ViewModels.Windows;
using ToadTools.UI.Services;       
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Settings = ParamChecker.Views.Pages.Settings;

namespace ParamChecker.Views.Windows;

public sealed partial class ParamChecker : FluentWindow     
{
    private readonly ParamCheckerViewModel _viewModel;
    private SettingsViewModel _settingsVm;

    public Configuration.Configuration Configuration { get; } = new();

    public ParamChecker(ParamCheckerViewModel viewModel, SettingsViewModel settingsVm)
    {
        _settingsVm = settingsVm;
        ThemeWatcherService.Initialize();
        ThemeWatcherService.Watch(this);
        ThemeWatcherService.ApplyTheme(ApplicationTheme.Dark);
        var settings = Configuration.LoadSettings();
        if (settings != null)
            settingsVm.LoadFromSettings(settings);
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        _viewModel.NavigateAction = NavigateToPage;
        Closing += ParamChecker_Closing;
    }

    private void NavigateToPage(Page page)
    {
        MainFrame.Navigate(page);
    }

    private void OnProfileClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CustomNavItem item) item.SelectCommand.Execute(null);
    }

    private void OnSettingsClicked(object sender, MouseButtonEventArgs e)
    {

        var page = new Settings(_settingsVm);

        MainFrame.Navigate(page);
    }

    private void ParamChecker_Closing(object? sender, CancelEventArgs e)
    {
        var settings = _settingsVm.ToSettings();
        Configuration.SaveSettings(settings);
    }
}