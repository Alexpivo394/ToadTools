using System.Windows;
using System.Windows.Input;
using ToadDesign;
using ParamChecker.ViewModels;
using ToadTools.UI.Services;

namespace ParamChecker.Views;

public sealed partial class ParametersWindow
{
    private readonly ParametersEditorViewModel _viewModel;

    public ParametersWindow(ParametersEditorViewModel viewModel)
    {
        InitializeComponent();

        // Тему не сбрасываем: окно открывается в той же теме, что главное.
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += (_, _) => NameInput.Focus();
    }

    /// <summary>Enter в поле имени добавляет параметр — так список набирается с клавиатуры подряд.</summary>
    /// <summary>Enter adds the typed name; a suggestion chosen with Enter is added too, a clicked one is only filled in.</summary>
    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Add();
    }

    private void OnSuggestionChosen(object sender, SuggestionChosenEventArgs e)
    {
        if (Keyboard.IsKeyDown(Key.Enter)) Add();
    }

    private void Add()
    {
        if (_viewModel.AddCommand.CanExecute(null)) _viewModel.AddCommand.Execute(null);
    }

    private void OnApply(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
