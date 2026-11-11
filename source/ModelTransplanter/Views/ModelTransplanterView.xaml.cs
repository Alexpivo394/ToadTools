using ModelTransplanter.ViewModels;
using ToadDesign;
using ToadTools.UI.Services;

namespace ModelTransplanter.Views
{
    public sealed partial class ModelTransplanterView

    {
    private ModelTransplanterViewModel _viewModel;
    public Configuration.Configuration Configuration { get; } = new();

    public ModelTransplanterView(ModelTransplanterViewModel viewModel)
    {
        InitializeComponent(); 
        
        
        ToadTheme.Mode = ToadThemeMode.Dark;

        

        _viewModel = viewModel;
        var settings = Configuration.LoadSettings();
        if (settings != null)
            viewModel.LoadFromSettings(settings);

        DataContext = viewModel;

        Closing += OnClosing;
    }


    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var setting = _viewModel.ToSettings();
        if (setting != null)
            Configuration.SaveSettings(setting);
    }
    }
}