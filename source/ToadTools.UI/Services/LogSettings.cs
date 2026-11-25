#nullable enable
namespace ToadTools.UI.Services;

/// <summary>Настройки журнала в <c>%AppData%\ToadTools\logging.json</c>: <c>{"verbose":bool}</c>.</summary>
internal sealed class LogSettings
{
    /// <summary>Подробный журнал: к строкам «Info» добавляются «Debug» (запросы, шаги панелей).</summary>
    public bool Verbose { get; set; }
}
