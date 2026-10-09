using ClassifierCode.ViewModels;
using ToadTools.UI.Services;

namespace ClassifierCode.Views;

public sealed partial class ClassifierReportView
{
    public ClassifierReportView(ClassifierReportViewModel viewModel)
    {
        InitializeComponent();

        // Тему не сбрасываем: отчёт открывается в той же теме, что выбрана в окне запуска.
        ThemeWatcherService.Watch(this);

        DataContext = viewModel;

        viewModel.CloseRequested += (_, _) => Close();
    }
}
