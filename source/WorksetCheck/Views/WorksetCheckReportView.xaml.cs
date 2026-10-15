using ToadTools.UI.Services;
using WorksetCheck.Models;
using WorksetCheck.ViewModels;

namespace WorksetCheck.Views;

public sealed partial class WorksetCheckReportView
{
    public WorksetCheckReportView(WorksetCheckReportViewModel viewModel)
    {
        InitializeComponent();

        // Тему не сбрасываем: отчёт открывается в той же теме, что выбрана в окне запуска.
        ThemeWatcherService.Watch(this);

        DataContext = viewModel;

        viewModel.CloseRequested += (_, _) => Close();
        DetailsGrid.MouseDoubleClick += (_, _) => viewModel.SelectItemCommand.Execute(DetailsGrid.SelectedItem as WorksetIssue);
    }
}
