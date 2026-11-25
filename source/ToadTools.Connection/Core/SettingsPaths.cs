using System.IO;

namespace ToadTools.Connection;

/// <summary>Папки, с которыми работает окно «Настройки»; тесты подставляют временные.</summary>
/// <param name="AppData">Настройки ToadTools, <c>%AppData%\ToadTools</c>.</param>
/// <param name="Logs">Журналы, <c>%LocalAppData%\ToadTools\Logs</c>.</param>
/// <param name="Thumbnails">Кэш миниатюр коллизий, <c>%LocalAppData%\ToadTools\Clash\images</c>.</param>
/// <param name="Desktop">Рабочий стол — сюда кладётся архив для разработчика.</param>
public sealed record SettingsPaths(string AppData, string Logs, string Thumbnails, string Desktop)
{
    /// <summary>Настоящие папки пользователя.</summary>
    public static SettingsPaths Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToadTools"),
        ToadTools.UI.Services.ToadLog.Folder,
        // Та же папка, что ThumbnailCache.DefaultFolder в «Коллизиях»: подключение на плагин не ссылается.
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToadTools", "Clash", "images"),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
}
