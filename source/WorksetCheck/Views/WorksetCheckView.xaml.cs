using ToadTools.UI.Services;
using WorksetCheck.ViewModels;
using Wpf.Ui.Appearance;

namespace WorksetCheck.Views;

public sealed partial class WorksetCheckView
{
    public WorksetCheckView(WorksetCheckViewModel viewModel)
    {
        InitializeComponent();

        ThemeWatcherService.Initialize();
        ThemeWatcherService.Watch(this);
        ThemeWatcherService.ApplyTheme(viewModel.DarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light);

        DataContext = viewModel;

        // DialogResult у модального окна сам его закрывает.
        viewModel.StartRequested += (_, _) => DialogResult = true;
    }
}
