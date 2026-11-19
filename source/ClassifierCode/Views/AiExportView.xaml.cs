using System.ComponentModel;
using ClassifierCode.ViewModels;
using ToadTools.UI.Services;

namespace ClassifierCode.Views;

public sealed partial class AiExportView
{
    private readonly AiExportViewModel _viewModel;

    public AiExportView(AiExportViewModel viewModel)
    {
        InitializeComponent();
        ThemePreference.Apply();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.SaveState();
        base.OnClosing(e);
    }
}
