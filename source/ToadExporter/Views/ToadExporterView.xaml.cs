using System.ComponentModel;
using ToadExporter.ViewModels;
using ToadTools.UI.Services;
using Wpf.Ui.Appearance;

namespace ToadExporter.Views;

public sealed partial class ToadExporterView
{
    private readonly ToadExporterViewModel _viewModel;

    public ToadExporterView(ToadExporterViewModel viewModel)
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
        _viewModel.SaveState();
        base.OnClosing(e);
    }
}
