using ToadTools.UI.Services;

namespace WorksetCheck.Configuration;

/// <summary>Настройки окна в %AppData%\ToadTools\WorksetCheck\config.json; до переноса лежали в WorksetCheckSettings рядом с dll.</summary>
public class Configuration
{
    private readonly SettingsFile<Settings> _file = new("WorksetCheck", "WorksetCheckSettings");

    public Settings LoadSettings() => _file.Load();

    public void SaveSettings(Settings settings) => _file.Save(settings);
}
