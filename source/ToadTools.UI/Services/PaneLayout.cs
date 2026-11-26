#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ToadTools.UI.Services;

/// <summary>
///     Доли нижней зоны панелей (карточка под списком), которые пользователь выставил разделителем, — в
///     <c>%AppData%\ToadTools\Panes\layout.json</c>. Файл не читается или не пишется — панели с долями по умолчанию.
/// </summary>
public sealed class PaneLayout(string path)
{
    /// <summary>Меньше этой доли нижней зоны не бывает: карточка не пропадает совсем.</summary>
    public const double MinShare = 0.1;

    /// <summary>Больше этой доли не бывает: списку всегда остаётся место.</summary>
    public const double MaxShare = 0.9;

    public static PaneLayout Default { get; } = new(System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToadTools", "Panes", "layout.json"));

    private Dictionary<string, double>? _shares;

    public string Path { get; } = path;

    /// <summary>Сохранённая доля панели; не сохраняли — <c>null</c>.</summary>
    public double? Get(string key) => Shares.TryGetValue(key, out var share) ? Clamp(share) : null;

    /// <summary>Запоминает долю (в пределах <see cref="MinShare"/>…<see cref="MaxShare"/>).</summary>
    public void Set(string key, double share)
    {
        if (double.IsNaN(share)) return;
        Shares[key] = Clamp(share);
        Save();
    }

    /// <summary>Забывает долю: панель вернётся к умолчанию.</summary>
    public void Forget(string key)
    {
        if (Shares.Remove(key)) Save();
    }

    public static double Clamp(double share) => Math.Min(MaxShare, Math.Max(MinShare, share));

    /// <summary>Доля нижней зоны по высотам зон; обе нулевые — <see cref="double.NaN"/>.</summary>
    public static double ShareOf(double top, double bottom) => top + bottom > 0 ? bottom / (top + bottom) : double.NaN;

    private Dictionary<string, double> Shares => _shares ??= Load();

    private Dictionary<string, double> Load()
    {
        try
        {
            var read = File.Exists(Path) ? SettingsJson.Read<Dictionary<string, double>>(File.ReadAllText(Path)) : null;
            return read is null ? new(StringComparer.Ordinal) : new(read, StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new(StringComparer.Ordinal);
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, SettingsJson.Write(Shares));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Не запомнилось — в следующий раз будет прежняя доля.
        }
    }
}
