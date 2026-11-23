using ToadTools.UI.Services;

namespace ModelTransplanter.Configuration;

/// <summary>Настройки окна в %AppData%\ToadTools\ModelTransplanter\config.json; до переноса лежали в ModelTransplanterSettings рядом с dll.</summary>
public class Configuration
{
    private readonly SettingsFile<Settings> _file = new("ModelTransplanter", "ModelTransplanterSettings");

    public Settings LoadSettings() => _file.Load();

    public void SaveSettings(Settings settings) => _file.Save(settings);
}
