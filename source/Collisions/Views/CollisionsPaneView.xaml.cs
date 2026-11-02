using System.Windows.Input;
using Collisions.ViewModels;

namespace Collisions.Views;

/// <summary>
///     Содержимое закрепляемой панели. Тема — светлая из словаря ToadTools.UI: окно-хозяин панели — главное окно Revit,
///     поэтому <c>ThemeWatcherService</c> (он перекрашивает фон окна) сюда не подключается.
/// </summary>
public sealed partial class CollisionsPaneView
{
    private readonly PaneViewModel _viewModel;

    public CollisionsPaneView(PaneViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        PreviewKeyDown += OnPaneKeyDown;
    }

    /// <summary>↑/↓ ведёт модель панели: ниже последней строки она догружает следующую страницу.</summary>
    private async void OnListKeyDown(object sender, KeyEventArgs e)
    {
        var step = e.Key switch
        {
            Key.Down => 1,
            Key.Up => -1,
            _ => 0
        };
        if (step == 0) return;

        e.Handled = true;
        // async void: исключение отсюда уронило бы Revit — показывается в панели
        try
        {
            await _viewModel.MoveAsync(step);
            if (_viewModel.SelectedItem is { } item)
            {
                List.ScrollIntoView(item);
                (List.ItemContainerGenerator.ContainerFromItem(item) as System.Windows.UIElement)?.Focus();
            }
        }
        catch (Exception exception)
        {
            _viewModel.ReportUnexpected(exception);
        }
    }

    /// <summary>
    ///     Ctrl+V в панели — текст буфера в модель панели: номер или ссылка на коллизию открывает её. В поле ввода вставка
    ///     обычная.
    /// </summary>
    private async void OnPaneKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.PasswordBox) return;

        string text;
        try
        {
            if (!System.Windows.Clipboard.ContainsText()) return;
            text = System.Windows.Clipboard.GetText();
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return; // буфер занят другим приложением
        }

        e.Handled = true;
        try
        {
            await _viewModel.PasteAsync(text);
        }
        catch (Exception exception)
        {
            _viewModel.ReportUnexpected(exception); // async void: исключение отсюда уронило бы Revit
        }
    }
}
