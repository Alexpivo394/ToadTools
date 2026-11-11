using System;
using System.IO;

namespace Installer.UI;

/// <summary>A small log of the installer window in %TEMP%\ToadTools.Installer.log: start, pages, failures.</summary>
public static class Trace
{
    private static readonly string File = Path.Combine(Path.GetTempPath(), "ToadTools.Installer.log");
    private static readonly object Gate = new();

    public static void Write(string text)
    {
        try
        {
            lock (Gate) System.IO.File.AppendAllText(File, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + text + Environment.NewLine);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // No log is better than a failed installation.
        }
    }
}
