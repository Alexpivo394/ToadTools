using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace ToadTools.UI.Services;

/// <summary>Высота рабочей области монитора, на котором показано главное окно Revit.</summary>
internal static class ScreenProbe
{
    private const uint MonitorDefaultToPrimary = 1;
    private const int MdtEffectiveDpi = 0;

    /// <summary>Высота рабочей области в DIP; без главного окна или при сбое — основной монитор.</summary>
    public static double WorkAreaHeightDip()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var window = process.MainWindowHandle;
            if (window != IntPtr.Zero)
            {
                var monitor = MonitorFromWindow(window, MonitorDefaultToPrimary);
                var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
                if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
                {
                    var heightPx = info.Work.Bottom - info.Work.Top;
                    var dpi = 96u;
                    try
                    {
                        if (GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0) dpi = dpiX;
                    }
                    catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
                    {
                        // Нет shcore (старая Windows): считаем 96 DPI.
                    }

                    return heightPx * 96.0 / dpi;
                }
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Нет данных о окне процесса: берём основной монитор.
        }

        return SystemParameters.WorkArea.Height;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }
}
