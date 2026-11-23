using ToadTools.UI.Services;

namespace QuantityCheck.Configuration;

/// <summary>Настройки окна в %AppData%\ToadTools\QuantityCheck\config.json; до переноса лежали в QuantityCheckSettings рядом с dll.</summary>
public class Configuration
{
    private readonly SettingsFile<Settings> _file = new("QuantityCheck", "QuantityCheckSettings");

    public Settings LoadSettings() => _file.Load();

    public void SaveSettings(Settings settings) => _file.Save(settings);
}
