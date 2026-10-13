using System.ComponentModel;
using SmartTags.ViewModels;
using ToadTools.UI.Services;
using Wpf.Ui.Appearance;

namespace SmartTags.Views;

public sealed partial class SmartTagsView
{
    private readonly SmartTagsViewModel _viewModel;

    public SmartTagsView(SmartTagsViewModel viewModel)
    {
        InitializeComponent();

        ThemeWatcherService.Initialize();
        ThemeWatcherService.Watch(this);
        ThemeWatcherService.ApplyTheme(ApplicationTheme.Dark);

        _viewModel = viewModel;
        DataContext = viewModel;

        // DialogResult у модального окна сам его закрывает.
        viewModel.StartRequested += (_, _) => DialogResult = true;
        viewModel.PickRequested += (_, _) => Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Закрыли крестиком — выбор всё равно запоминаем, как при запуске.
        if (DialogResult != true) _viewModel.SaveState();
        base.OnClosing(e);
    }
}
