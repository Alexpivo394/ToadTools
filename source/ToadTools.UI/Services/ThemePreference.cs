#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ToadDesign;

namespace ToadTools.UI.Services;

/// <summary>
///     Theme (light, dark, as in Windows) and size (normal, compact, auto) of all ToadTools windows, one choice for
///     every tool. Stored in <c>%AppData%\ToadTools\theme.json</c>; dark and auto until the user switches them.
///     <para>
///         Windows call <see cref="Apply" /> when they open; the switches bind to <see cref="Instance" />, so a switch
///         in one tool shows the choice made in another.
///     </para>
/// </summary>
public sealed partial class ThemePreference : INotifyPropertyChanged
{
    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToadTools", "theme.json");

    private ThemeChoice _theme;
    private DensityChoice _density;
    private bool _isCompact;

    private ThemePreference()
    {
        (_theme, _density) = Load();
        _isCompact = ComputeCompact();
        ToadTheme.Mode = Mode;
        ToadTheme.Density = CurrentDensity;
    }

    /// <summary>The preference shared by all windows; created, read from disk and applied on first use.</summary>
    public static ThemePreference Instance { get; } = new();

    /// <summary>Chosen theme. Setting it repaints every open window and remembers the choice.</summary>
    public ThemeChoice Theme
    {
        get => _theme;
        set
        {
            if (_theme == value) return;
            var wasDark = IsDark;
            _theme = value;
            ToadTheme.Mode = Mode;
            Save();
            Raise(nameof(Theme));
            if (wasDark != IsDark) Raise(nameof(IsDark));
        }
    }

    /// <summary>Chosen size; <see cref="DensityChoice.Auto" /> follows the screen of the Revit window.</summary>
    public DensityChoice Density
    {
        get => _density;
        set
        {
            if (_density == value) return;
            _density = value;
            Save();
            Raise(nameof(Density));
            Refresh();
        }
    }

    /// <summary>Whether the windows are compact now: chosen explicitly or, for "Auto", decided by the screen height.</summary>
    public bool IsCompact => _isCompact;

    /// <summary>
    ///     Whether the windows are dark; kept for the switches that still bind to it. Setting it picks
    ///     <see cref="ThemeChoice.Dark" /> or <see cref="ThemeChoice.Light" />.
    /// </summary>
    public bool IsDark
    {
        get => _theme == ThemeChoice.Dark;
        set => Theme = value ? ThemeChoice.Dark : ThemeChoice.Light;
    }

    private ToadThemeMode Mode => _theme switch
    {
        ThemeChoice.Light => ToadThemeMode.Light,
        ThemeChoice.System => ToadThemeMode.System,
        _ => ToadThemeMode.Dark
    };

    private ToadDensity CurrentDensity => _isCompact ? ToadDensity.Compact : ToadDensity.Normal;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Puts the remembered theme and size on; windows call it before they are shown.</summary>
    public static void Apply() => Instance.Refresh();

    /// <summary>Recomputes the size (the Revit window may have moved to another monitor) and puts theme and size on.</summary>
    private void Refresh()
    {
        var compact = ComputeCompact();
        var changed = compact != _isCompact;
        _isCompact = compact;
        ToadTheme.Mode = Mode;
        ToadTheme.Density = CurrentDensity;
        if (changed) Raise(nameof(IsCompact));
    }

    private bool ComputeCompact() => _density switch
    {
        DensityChoice.Normal => false,
        DensityChoice.Compact => true,
        _ => AutoCompact(ScreenProbe.WorkAreaHeightDip())
    };

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static (ThemeChoice, DensityChoice) Load()
    {
        try
        {
            return File.Exists(FilePath) ? Parse(File.ReadAllText(FilePath)) : (ThemeChoice.Dark, DensityChoice.Auto);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (ThemeChoice.Dark, DensityChoice.Auto);
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            // "dark" остаётся для прежних версий плагина, читающих тот же файл.
            var json = new JsonObject
            {
                ["dark"] = _theme != ThemeChoice.Light,
                ["theme"] = _theme.ToString().ToLowerInvariant(),
                ["density"] = _density.ToString().ToLowerInvariant()
            };
            File.WriteAllText(FilePath, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not saved: the next start opens in the previous choice; the windows keep working.
        }
    }
}
