using ClassifierCode.Models;
using ClassifierCode.ViewModels;
using ToadTools.UI.Services;
using Wpf.Ui.Appearance;

namespace ClassifierCode.Views;

public sealed partial class ClassifierCodeView
{
    public ClassifierCodeView(ClassifierCodeViewModel viewModel)
    {
        InitializeComponent();

        ThemeWatcherService.Initialize();
        ThemeWatcherService.Watch(this);
        ThemeWatcherService.ApplyTheme(ApplicationTheme.Dark);

        DataContext = viewModel;

        viewModel.Completed += OnCompleted;
    }

    private void OnCompleted(object? sender, ClassifierReport report)
    {
        Close();

        var reportView = new ClassifierReportView(new ClassifierReportViewModel(report));
        reportView.ShowDialog();
    }
}
