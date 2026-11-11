using ToadDesign;
using System.ComponentModel;
using ToadExporter.ViewModels;
using ToadTools.UI.Services;

namespace ToadExporter.Views;

public sealed partial class ToadExporterView
{
    private readonly ToadExporterViewModel _viewModel;

    public ToadExporterView(ToadExporterViewModel viewModel)
    {
        InitializeComponent();

        ToadTheme.Mode = viewModel.Settings.DarkTheme ? ToadThemeMode.Dark : ToadThemeMode.Light;

        _viewModel = viewModel;
        DataContext = viewModel;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.SaveState();
        base.OnClosing(e);
    }
}
