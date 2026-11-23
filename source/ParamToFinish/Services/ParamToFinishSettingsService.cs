using System.IO;
using ToadTools.UI.Services;

namespace ParamToFinish.Services;

public sealed class ParamToFinishSettingsService
{
    private const string SettingsFileName = "settings.json";

    private readonly string _settingsPath;

    public ParamToFinishSettingsService()
    {
        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ToadTools",
            "ParamToFinish");

        _settingsPath = Path.Combine(settingsDirectory, SettingsFileName);
    }

    public ParamToFinishSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new ParamToFinishSettings();

            var json = File.ReadAllText(_settingsPath);

            if (string.IsNullOrWhiteSpace(json))
                return new ParamToFinishSettings();

            return SettingsJson.Read<ParamToFinishSettings>(json)
                   ?? new ParamToFinishSettings();
        }
        catch
        {
            return new ParamToFinishSettings();
        }
    }

    public void Save(ParamToFinishSettings settings)
    {
        try
        {
            var settingsDirectory = Path.GetDirectoryName(_settingsPath);

            if (!string.IsNullOrWhiteSpace(settingsDirectory))
                Directory.CreateDirectory(settingsDirectory);

            var json = SettingsJson.Write(settings);

            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // Settings persistence should never interrupt the Revit command.
        }
    }
}
