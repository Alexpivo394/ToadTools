#nullable enable
using System;
using System.Text.Json;

namespace ToadTools.UI.Services;

// Часть без WPF и ToadDesign: разбор файла и порог «Авто» подключаются в тесты ссылкой.
public sealed partial class ThemePreference
{
    /// <summary>Рабочая область ниже этой высоты (DIP) включает компактный размер при выборе «Авто».</summary>
    private const double AutoCompactBelowDip = 900;

    /// <summary>
    ///     Читает theme.json. Старый файл знает только <c>"dark"</c>: светлая тема лишь при явном <c>false</c>.
    ///     Нечитаемое или неполное содержимое даёт тёмную тему и «Авто».
    /// </summary>
    internal static (ThemeChoice Theme, DensityChoice Density) Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (ThemeChoice.Dark, DensityChoice.Auto);

            var theme = root.TryGetProperty("dark", out var dark) && dark.ValueKind == JsonValueKind.False
                ? ThemeChoice.Light
                : ThemeChoice.Dark;
            if (TryGetString(root, "theme") is { } themeText)
                theme = themeText.ToLowerInvariant() switch
                {
                    "light" => ThemeChoice.Light,
                    "dark" => ThemeChoice.Dark,
                    "system" => ThemeChoice.System,
                    _ => theme
                };

            var density = TryGetString(root, "density")?.ToLowerInvariant() switch
            {
                "normal" => DensityChoice.Normal,
                "compact" => DensityChoice.Compact,
                _ => DensityChoice.Auto
            };
            return (theme, density);
        }
        catch (JsonException)
        {
            return (ThemeChoice.Dark, DensityChoice.Auto);
        }
    }

    /// <summary>Компактный ли размер на экране с такой высотой рабочей области в DIP.</summary>
    internal static bool AutoCompact(double workAreaHeightDip) => workAreaHeightDip < AutoCompactBelowDip;

    private static string? TryGetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
