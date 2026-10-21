using System.Windows;
using ParamChecker.ViewModels;
using ToadTools.UI.Services;
using Wpf.Ui.Controls;

namespace ParamChecker.Views;

public sealed partial class ParametersWindow
{
    private readonly ParametersEditorViewModel _viewModel;

    public ParametersWindow(ParametersEditorViewModel viewModel)
    {
        InitializeComponent();

        // Тему не сбрасываем: окно открывается в той же теме, что главное.
        ThemeWatcherService.Watch(this);
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += (_, _) => NameInput.Focus();
    }

    /// <summary>Enter в поле имени добавляет параметр — так список набирается с клавиатуры подряд.</summary>
    private void OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_viewModel.AddCommand.CanExecute(null)) _viewModel.AddCommand.Execute(null);
    }

    private void OnApply(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
