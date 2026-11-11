using ToadTools.Connection.ViewModels;
using ToadTools.UI.Services;

namespace ToadTools.Connection.Views;

public sealed partial class ConnectionWindow
{
    public ConnectionWindow(ConnectionViewModel viewModel)
    {
        
        InitializeComponent();
        ThemePreference.Apply();
        DataContext = viewModel;
        Loaded += async (_, _) =>
        {
            // async void: исключение отсюда уронило бы Revit
            try
            {
                await viewModel.CheckAsync();
            }
            catch (Exception exception)
            {
                viewModel.Error = ToadFlowErrors.Describe(exception);
            }
        };
    }
}
