using ToadDesign;
using ClassifierCode.Models;
using ClassifierCode.ViewModels;
using ToadTools.UI.Services;

namespace ClassifierCode.Views;

public sealed partial class ClassifierCodeView
{
    public ClassifierCodeView(ClassifierCodeViewModel viewModel)
    {
        InitializeComponent();

        ThemePreference.Apply();

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
