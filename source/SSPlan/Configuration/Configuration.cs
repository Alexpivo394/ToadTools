using ToadTools.UI.Services;

namespace SSPlan.Configuration;

/// <summary>Настройки окна в %AppData%\ToadTools\SSPlan\config.json; до переноса лежали в SSPlanSettings рядом с dll.</summary>
public class Configuration
{
    private readonly SettingsFile<Settings> _file = new("SSPlan", "SSPlanSettings");

    public Settings LoadSettings() => _file.Load();

    public void SaveSettings(Settings settings) => _file.Save(settings);
}
