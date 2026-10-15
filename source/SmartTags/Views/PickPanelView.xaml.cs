using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ToadTools.UI.Services;

namespace SmartTags.Views;

/// <summary>
///     Немодальная панель на время выбора элементов: пользователь выбирает обычными средствами Revit
///     и нажимает «Готово» (или «Отмена»).
/// </summary>
public sealed partial class PickPanelView
{
    private readonly IntPtr _owner;

    public PickPanelView(IntPtr owner)
    {
        InitializeComponent();

        // Тему не сбрасываем: панель открывается в той же теме, что выбрана в окне плагина.
        ThemeWatcherService.Watch(this);

        _owner = owner;
        new WindowInteropHelper(this).Owner = owner;
        SourceInitialized += (_, _) => PlaceNearOwnerCorner();
    }

    /// <summary>Пользователь нажал «Готово».</summary>
    public event EventHandler? Done;

    private void OnDone(object sender, RoutedEventArgs e)
    {
        Done?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    /// <summary>Правый верхний угол окна Revit под лентой — чтобы не закрывать середину вида.</summary>
    private void PlaceNearOwnerCorner()
    {
        if (!GetWindowRect(_owner, out var rect)) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        Left = rect.Right / dpi.DpiScaleX - Width - 40;
        Top = rect.Top / dpi.DpiScaleY + 200;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);
}
