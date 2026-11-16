using ToadTools.Desktop.ViewModels;

namespace ToadTools.Desktop.Views;

/// <summary>Окно настроек и статуса. Закрытие окна не закрывает программу: она остаётся в трее.</summary>
public partial class SettingsWindow
{
    public SettingsWindow(App app)
    {
        InitializeComponent();
        var model = new SettingsViewModel(app);
        DataContext = model;
        Closed += (_, _) => model.Detach();
    }
}
