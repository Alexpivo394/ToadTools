using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Installer.UI;

/// <summary>Which Revit versions are installed and which are running now.</summary>
public static class RevitDetector
{
    /// <summary>Revit of the year is installed: its key under HKLM\SOFTWARE\Autodesk\Revit exists.</summary>
    public static bool IsInstalled(int year)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = root.OpenSubKey($@"SOFTWARE\Autodesk\Revit\Autodesk Revit {year}");
            return key != null;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>Years of the Revit processes running now (Revit 2024 → 2024).</summary>
    public static ISet<int> Running()
    {
        var years = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName("Revit"))
        {
            using (process)
            {
                try
                {
                    var file = process.MainModule?.FileName;
                    if (file == null) continue;
                    var major = FileVersionInfo.GetVersionInfo(file).FileMajorPart;
                    if (major is >= 20 and < 100) years.Add(2000 + major);
                    else if (Regex.Match(file, @"Revit (\d{4})") is { Success: true } match) years.Add(int.Parse(match.Groups[1].Value));
                }
                catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    // A process of another user or one that just exited: not ours to wait for.
                }
            }
        }

        return years;
    }
}
