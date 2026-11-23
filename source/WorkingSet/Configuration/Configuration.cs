using ToadTools.UI.Services;

namespace WorkingSet.Configuration;

/// <summary>Настройки окна в %AppData%\ToadTools\WorkingSet\config.json; до переноса лежали в WorkingSetSettings рядом с dll.</summary>
public class Configuration
{
    private readonly SettingsFile<Settings> _file = new("WorkingSet", "WorkingSetSettings");

    public Settings LoadSettings() => _file.Load();

    public void SaveSettings(Settings settings) => _file.Save(settings);
}
