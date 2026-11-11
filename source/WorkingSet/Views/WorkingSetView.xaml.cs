using ToadTools.UI.Services;
using ToadDesign;
using WorkingSet.ViewModels;

namespace WorkingSet.Views
{
    public sealed partial class WorkingSetView
    {
        public WorkingSetViewModel _viewModel;
        public Configuration.Configuration Config { get; } = new();
        public WorkingSetView(WorkingSetViewModel viewModel)
        {
            InitializeComponent();
            
            ToadTheme.Mode = ToadThemeMode.Dark;
            
            _viewModel = viewModel;
            var settings = Config.LoadSettings();
            if (settings != null)
                viewModel.LoadFromSettings(settings);
            
            DataContext = viewModel;

            Closing += OnClosing;
        }
        
        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            var setting = _viewModel.ToSettings();
            if (setting != null)
                Config.SaveSettings(setting);
        }
    }
}