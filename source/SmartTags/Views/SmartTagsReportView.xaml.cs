using SmartTags.Models;
using SmartTags.ViewModels;
using ToadTools.UI.Services;

namespace SmartTags.Views;

public sealed partial class SmartTagsReportView
{
    public SmartTagsReportView(SmartTagsReportViewModel viewModel)
    {
        InitializeComponent();

        // Тему не сбрасываем: отчёт открывается в той же теме, что выбрана в окне запуска.
        ThemeWatcherService.Watch(this);

        DataContext = viewModel;

        viewModel.CloseRequested += (_, _) => Close();
        DetailsGrid.MouseDoubleClick += (_, _) => viewModel.SelectItemCommand.Execute(DetailsGrid.SelectedItem as ItemReport);
    }
}
