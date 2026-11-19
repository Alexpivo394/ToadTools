using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using ClassifierCode.Editor.ViewModels;
using ToadTools.UI.Services;

namespace ClassifierCode.Views.Editor;

public sealed partial class ProfileEditorView
{
    private readonly ProfileEditorViewModel _viewModel;
    private bool _closingConfirmed;

    public ProfileEditorView(ProfileEditorViewModel viewModel)
    {
        InitializeComponent();
        ThemePreference.Apply();
        _viewModel = viewModel;
        DataContext = viewModel;
        if (viewModel.Host is Services.RevitEditorHost host) host.HideWindows = SetHidden;
    }

    /// <summary>Свернуть окно и всех его владельцев (окно запуска, отчёт) на время выбора в модели и вернуть.</summary>
    private void SetHidden(bool hidden)
    {
        for (Window? window = this; window != null; window = window.Owner)
            window.WindowState = hidden ? WindowState.Minimized : WindowState.Normal;
        if (!hidden) Activate();
    }

    /// <summary>Пересборка профиля после правки — не на каждую букву, а через паузу в 300 мс.</summary>
    public static Action<Action> Debouncer(Dispatcher dispatcher)
    {
        DispatcherTimer? timer = null;
        Action? pending = null;
        return action =>
        {
            pending = action;
            if (timer == null)
            {
                timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(300) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    pending?.Invoke();
                };
            }

            timer.Stop();
            timer.Start();
        };
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closingConfirmed && !_viewModel.ConfirmClose())
        {
            e.Cancel = true;
            return;
        }

        _closingConfirmed = true;
        base.OnClosing(e);
    }
}
