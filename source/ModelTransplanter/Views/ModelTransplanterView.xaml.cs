using ModelTransplanter.ViewModels;
using ToadTools.UI.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ModelTransplanter.Views
{
    public sealed partial class ModelTransplanterView

    {
    private ModelTransplanterViewModel _viewModel;
    public Configuration.Configuration Configuration { get; } = new();

    public ModelTransplanterView(ModelTransplanterViewModel viewModel)
    {
        InitializeComponent(); 
        
        
        ThemeWatcherService.Initialize();
        ThemeWatcherService.Watch(this);
        ThemeWatcherService.ApplyTheme(ApplicationTheme.Dark);

        

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