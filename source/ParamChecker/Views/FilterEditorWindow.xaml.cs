using System.Windows;
using ParamChecker.ViewModels;
using ToadTools.UI.Services;

namespace ParamChecker.Views;

public sealed partial class FilterEditorWindow
{
    public FilterEditorWindow(FilterEditorViewModel viewModel)
    {
        InitializeComponent();

        // Тему не сбрасываем: окно открывается в той же теме, что главное.
        ThemeWatcherService.Watch(this);
        DataContext = viewModel;
    }

    private void OnApply(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
