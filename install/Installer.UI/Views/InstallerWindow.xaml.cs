using System.ComponentModel;
using System.Windows;
using ToadDesign;

namespace Installer.UI.Views;

/// <summary>The single installer window; its content follows <see cref="SetupViewModel.Page" />.</summary>
public partial class InstallerWindow : ToadWindow
{
    private readonly SetupViewModel _model;

    public InstallerWindow(SetupViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
        model.CloseRequested += (_, _) => Close();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SetupViewModel.Page)) ShowPage();
        };
        ShowPage();
    }

    private void ShowPage()
    {
        FrameworkElement page = _model.Page switch
        {
            SetupPage.Versions => new VersionsPage(),
            SetupPage.Maintenance => new MaintenancePage(),
            SetupPage.Progress => new ProgressPage(),
            SetupPage.Done => new DonePage(),
            SetupPage.Error => new ErrorPage(),
            _ => new WelcomePage()
        };
        page.DataContext = _model;
        Host.Content = page;
    }

    /// <summary>Alt+F4 while the MSI works would leave it without a window: the progress page offers "Отмена" instead.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_model.CanClose) e.Cancel = true;
        base.OnClosing(e);
    }
}
