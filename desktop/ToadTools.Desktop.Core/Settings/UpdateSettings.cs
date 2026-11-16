using System.Net;

namespace ToadTools.Desktop.Core.Settings;

/// <summary>Как часто проверять обновления.</summary>
public enum CheckInterval
{
    Manual,
    OnStartup,
    Hours1,
    Hours3,
    Hours6,
    Hours12,
    Hours24
}

public enum ProxyMode
{
    /// <summary>Прокси Windows с учётной записью пользователя (корпоративные сети).</summary>
    System,
    None,
    Custom
}

/// <summary>Настройки программы (%APPDATA%\ToadTools\settings.json).</summary>
public sealed class UpdateSettings
{
    public CheckInterval Interval { get; set; } = CheckInterval.Hours6;
    public bool AutoDownload { get; set; } = true;
    public bool InstallWhenRevitCloses { get; set; } = true;
    public bool AutoUpdateApp { get; set; } = true;
    public bool Notifications { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;

    /// <summary>Год Revit → ставить ли туда плагин. Года нет в словаре — ставить.</summary>
    public Dictionary<int, bool> RevitYears { get; set; } = new();

    public ProxyMode Proxy { get; set; } = ProxyMode.System;
    public string? ProxyAddress { get; set; }

    public bool IsYearEnabled(int year) => !RevitYears.TryGetValue(year, out var enabled) || enabled;
}

public static class UpdateSettingsExtensions
{
    /// <summary>Период проверки; null — только вручную или при запуске.</summary>
    public static TimeSpan? ToTimeSpan(this CheckInterval interval) => interval switch
    {
        CheckInterval.Hours1 => TimeSpan.FromHours(1),
        CheckInterval.Hours3 => TimeSpan.FromHours(3),
        CheckInterval.Hours6 => TimeSpan.FromHours(6),
        CheckInterval.Hours12 => TimeSpan.FromHours(12),
        CheckInterval.Hours24 => TimeSpan.FromHours(24),
        _ => null
    };

    /// <summary>HTTP с прокси из настроек. Свой адрес не разобрался — системный прокси.</summary>
    public static HttpMessageHandler CreateHandler(this UpdateSettings settings)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            UseDefaultCredentials = true
        };

        switch (settings.Proxy)
        {
            case ProxyMode.None:
                handler.UseProxy = false;
                break;
            case ProxyMode.Custom when Uri.TryCreate(settings.ProxyAddress, UriKind.Absolute, out var address)
                                       && address.Scheme is "http" or "https":
                handler.UseProxy = true;
                handler.Proxy = new WebProxy(address) { UseDefaultCredentials = true };
                break;
            default:
                handler.UseProxy = true;
                handler.DefaultProxyCredentials = CredentialCache.DefaultCredentials;
                break;
        }

        return handler;
    }
}
