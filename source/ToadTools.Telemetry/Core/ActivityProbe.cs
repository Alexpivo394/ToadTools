using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ToadTools.Telemetry;

/// <summary>Активен ли человек в Revit и сколько памяти: вызовы Windows, быстрые, без WMI. Не бросают.</summary>
public static class ActivityProbe
{
    /// <summary>
    ///     Окно на переднем плане принадлежит процессу Revit (любое его окно, включая диалоги) и был ввод (мышь, клавиатура) не
    ///     раньше <paramref name="idleLimit" />.
    /// </summary>
    public static bool IsActive(IntPtr revitWindow, TimeSpan idleLimit)
    {
        try
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;
            GetWindowThreadProcessId(foreground, out var foregroundProcess);
            uint revitProcess;
            if (revitWindow == IntPtr.Zero) revitProcess = (uint)Process.GetCurrentProcess().Id;
            else GetWindowThreadProcessId(revitWindow, out revitProcess);
            return foregroundProcess == revitProcess && IdleTime() is { } idle && idle <= idleLimit;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>Сколько прошло с последнего ввода в Windows; null — не узнать.</summary>
    public static TimeSpan? IdleTime()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return null;
        // Оба счётчика — миллисекунды с запуска Windows, 32 бита: разность без знака переживает переполнение.
        var elapsed = unchecked((uint)Environment.TickCount - info.Time);
        return TimeSpan.FromMilliseconds(elapsed);
    }

    /// <summary>Свободная оперативная память машины, МБ; 0 — не узнать.</summary>
    public static long FreeMemoryMb() => Memory() is { } status ? (long)(status.AvailPhys / (1024 * 1024)) : 0;

    /// <summary>Вся оперативная память машины, МБ; null — не узнать.</summary>
    public static long? TotalMemoryMb() => Memory() is { } status ? (long)(status.TotalPhys / (1024 * 1024)) : null;

    /// <summary>Память процесса Revit (рабочий набор), МБ.</summary>
    public static long ProcessMemoryMb()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.WorkingSet64 / (1024 * 1024);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return 0;
        }
    }

    private static MemoryStatusEx? Memory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? status : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);
}
