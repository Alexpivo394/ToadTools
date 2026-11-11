#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using Newtonsoft.Json.Linq;
using ToadDesign;

namespace ToadTools.UI.Services;

/// <summary>
///     Light or dark theme of all ToadTools windows, one choice for every tool. Stored in
///     <c>%AppData%\ToadTools\theme.json</c>; dark until the user switches it.
///     <para>
///         Windows call <see cref="Apply" /> when they open; the "Тёмная тема" switches bind to <see cref="IsDark" /> of
///         <see cref="Instance" />, so a switch in one tool shows the theme chosen in another.
///     </para>
/// </summary>
public sealed class ThemePreference : INotifyPropertyChanged
{
    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToadTools", "theme.json");

    private bool _isDark;

    private ThemePreference()
    {
        _isDark = Load();
        ToadTheme.Mode = Mode;
    }

    /// <summary>The preference shared by all windows; created, read from disk and applied on first use.</summary>
    public static ThemePreference Instance { get; } = new();

    /// <summary>Whether the windows are dark. Setting it repaints every open window and remembers the choice.</summary>
    public bool IsDark
    {
        get => _isDark;
        set
        {
            if (_isDark == value) return;
            _isDark = value;
            ToadTheme.Mode = Mode;
            Save();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDark)));
        }
    }

    private ToadThemeMode Mode => _isDark ? ToadThemeMode.Dark : ToadThemeMode.Light;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Puts the remembered theme on; windows call it before they are shown.</summary>
    public static void Apply() => ToadTheme.Mode = Instance.Mode;

    private static bool Load()
    {
        try
        {
            return !File.Exists(FilePath) || (bool?)JObject.Parse(File.ReadAllText(FilePath))["dark"] != false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            return true;
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, new JObject { ["dark"] = _isDark }.ToString());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not saved: the next start opens in the previous theme; the windows keep working.
        }
    }
}
