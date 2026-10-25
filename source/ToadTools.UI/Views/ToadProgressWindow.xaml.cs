#nullable enable
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ToadTools.UI.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;
using Point = System.Windows.Point;

namespace ToadTools.UI.Views;

/// <summary>
///     Окно прогресса. Создаётся в отдельном потоке (см. <see cref="Progress.ToadProgress" />), поэтому
///     ресурсы темы у него свои — общие ресурсы окон Revit-потока отсюда трогать нельзя.
/// </summary>
public sealed partial class ToadProgressWindow
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(100);

    private readonly ToadProgressViewModel _viewModel;
    private readonly IntPtr _anchor;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _showTimer;
    private bool _allowClose;

    public ToadProgressWindow(ToadProgressViewModel viewModel, ApplicationTheme theme, IntPtr anchor, TimeSpan showDelay)
    {
        _viewModel = viewModel;
        _anchor = anchor;

        Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = theme });
        Resources.MergedDictionaries.Add(new ControlsDictionary());
        InitializeComponent();
        DataContext = viewModel;

        ProgressTitleBar.MinimizeActionOverride = (_, _) => MinimizeKeepingActivation();
        SourceInitialized += (_, _) =>
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(InterceptMinimize);

        Loaded += (_, _) =>
        {
            // FluentWindow красит подложку по глобальной теме WPF-UI, а словарь у окна свой. Если тему окон плагина
            // ещё никто не применил, подложка выходила светлой под белым текстом тёмной темы.
            WindowBackgroundManager.UpdateBackground(this, theme, WindowBackdropType);
            CenterOverAnchor();
        };
        Closing += OnClosing;

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = RefreshInterval };
        _refreshTimer.Tick += (_, _) =>
        {
            _viewModel.Refresh();
            KeepAboveRevit();
        };
        _refreshTimer.Start();

        // Быстрые операции заканчиваются раньше, чем окно успело бы появиться, — и не мелькают.
        _showTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = showDelay };
        _showTimer.Tick += (_, _) =>
        {
            _showTimer.Stop();
            _viewModel.Refresh();
            Show();
        };
        _showTimer.Start();
    }

    /// <summary>Закрыть окно по завершении работы (пользователь сам закрыть его не может).</summary>
    public void ForceClose()
    {
        _allowClose = true;
        _showTimer.Stop();
        _refreshTimer.Stop();
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Alt+F4 не должен прятать окно посреди работы — для остановки есть «Прервать».
        if (!_allowClose) e.Cancel = true;
    }

    /// <summary>
    ///     Окно живёт в своём потоке и не принадлежит окнам Revit: открывая модель, Revit активирует своё
    ///     окно, и окно плагина перекрыло бы прогресс. Поэтому окно поверх всех, пока активен Revit;
    ///     при переключении в другую программу — обычное.
    /// </summary>
    private void KeepAboveRevit()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var processId);
        var revitActive = processId == CurrentProcessId;
        if (Topmost != revitActive) Topmost = revitActive;
    }

    private static readonly uint CurrentProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

    /// <summary>
    ///     Обычное сворачивание отдаёт активность следующему окну — главному окну Revit, поток которого занят
    ///     работой. Активация посреди операции (Revit сам прокачивает сообщения при открытии модели) вешала
    ///     и роняла Revit. Поэтому окно сворачивается, оставаясь активным.
    /// </summary>
    private void MinimizeKeepingActivation() => ShowWindow(new WindowInteropHelper(this).Handle, SwShowMinimized);

    /// <summary>Сворачивание из системного меню и с панели задач — так же, как кнопкой в заголовке.</summary>
    private IntPtr InterceptMinimize(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmSysCommand && (wParam.ToInt64() & 0xFFF0) == ScMinimize)
        {
            MinimizeKeepingActivation();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private const int WmSysCommand = 0x0112;
    private const int ScMinimize = 0xF020;
    private const int SwShowMinimized = 2;

    /// <summary>Ставит окно по центру окна, из которого запущена команда (Revit или окно плагина).</summary>
    private void CenterOverAnchor()
    {
        var workArea = SystemParameters.WorkArea;
        var center = new Point(workArea.Left + workArea.Width / 2, workArea.Top + workArea.Height / 2);

        if (_anchor != IntPtr.Zero && !IsIconic(_anchor) && GetWindowRect(_anchor, out var rect) &&
            rect.Right > rect.Left && PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
        {
            var fromDevice = target.TransformFromDevice;
            center = fromDevice.Transform(new Point((rect.Left + rect.Right) / 2.0, (rect.Top + rect.Bottom) / 2.0));
        }

        Left = center.X - ActualWidth / 2;
        Top = center.Y - ActualHeight / 2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
