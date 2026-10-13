using SmartTags.ViewModels;
using ToadTools.UI.Services;

namespace SmartTags.Views;

public sealed partial class ProgressView
{
    public ProgressView(ProgressViewModel viewModel)
    {
        InitializeComponent();

        // Тему не сбрасываем: окно открывается в той же теме, что выбрана в окне запуска.
        ThemeWatcherService.Watch(this);

        DataContext = viewModel;
    }
}
