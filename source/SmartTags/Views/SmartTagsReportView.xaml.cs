using SmartTags.Models;
using SmartTags.ViewModels;
using ToadTools.UI.Services;

namespace SmartTags.Views;

public sealed partial class SmartTagsReportView
{
    public SmartTagsReportView(SmartTagsReportViewModel viewModel)
    {
        InitializeComponent();
        ThemePreference.Apply();

        // Тему не сбрасываем: отчёт открывается в той же теме, что выбрана в окне запуска.

        DataContext = viewModel;

        viewModel.CloseRequested += (_, _) => Close();
        DetailsGrid.MouseDoubleClick += (_, _) => viewModel.SelectItemCommand.Execute(DetailsGrid.SelectedItem as ItemReport);
    }
}
