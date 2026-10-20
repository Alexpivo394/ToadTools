using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using ToadExporter.Core;

namespace ToadExporter.Services;

/// <summary>Хранит настройки окна в ToadExporterSettings/config.json рядом с dll.</summary>
public sealed class SettingsStore
{
    private readonly string _path = Path.Combine(SettingsRoot(), "ToadExporterSettings", "config.json");

    /// <summary>Папка сборки; у сборки, загруженной из памяти, её нет — тогда %AppData%\ToadTools.</summary>
    private static string SettingsRoot()
    {
        var location = Assembly.GetExecutingAssembly().Location;
        return string.IsNullOrEmpty(location)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToadTools")
            : Path.GetDirectoryName(location)!;
    }

    /// <summary>Настройки — удобство, а не данные: при любой ошибке молча берём умолчания.</summary>
    public ExportSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new ExportSettings();
            return JsonConvert.DeserializeObject<ExportSettings>(File.ReadAllText(_path)) ?? new ExportSettings();
        }
        catch
        {
            return new ExportSettings();
        }
    }

    public void Save(ExportSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonConvert.SerializeObject(settings, Formatting.Indented));
        }
        catch
        {
            // Не сохранились — в следующий раз будут умолчания, работе это не мешает.
        }
    }
}
