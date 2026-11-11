using ToadDesign;
using ToadTools.UI.Services;
using WorksetCheck.ViewModels;

namespace WorksetCheck.Views;

public sealed partial class WorksetCheckView
{
    public WorksetCheckView(WorksetCheckViewModel viewModel)
    {
        InitializeComponent();

        ThemePreference.Apply();

        DataContext = viewModel;

        // DialogResult у модального окна сам его закрывает.
        viewModel.StartRequested += (_, _) => DialogResult = true;
    }
}
