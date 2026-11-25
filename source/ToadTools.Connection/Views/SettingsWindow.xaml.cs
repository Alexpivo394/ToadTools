using ToadTools.Connection.ViewModels;
using ToadTools.UI.Services;

namespace ToadTools.Connection.Views;

public sealed partial class SettingsWindow
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        ThemePreference.Apply();
        ViewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) =>
        {
            // async void: исключение отсюда уронило бы Revit
            try
            {
                await viewModel.Connection.CheckAsync();
            }
            catch (Exception exception)
            {
                viewModel.Connection.Error = ToadFlowErrors.Describe(exception);
            }
        };
        Closed += (_, _) => viewModel.Dispose();
    }

    public SettingsViewModel ViewModel { get; }
}
