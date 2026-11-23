using ToadExporter.Core;
using ToadTools.UI.Services;

namespace ToadExporter.Services;

/// <summary>Хранит настройки окна в %AppData%\ToadTools\ToadExporter\config.json; до переноса лежали в ToadExporterSettings рядом с dll.</summary>
public sealed class SettingsStore
{
    private readonly SettingsFile<ExportSettings> _file = new("ToadExporter", "ToadExporterSettings");

    /// <summary>Настройки — удобство, а не данные: при любой ошибке молча берём умолчания.</summary>
    public ExportSettings Load() => _file.Load();

    public void Save(ExportSettings settings) => _file.Save(settings);
}
