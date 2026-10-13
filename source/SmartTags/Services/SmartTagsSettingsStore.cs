using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using SmartTags.Models;

namespace SmartTags.Services;

/// <summary>Хранит настройки окна в SmartTagsSettings/config.json рядом с dll.</summary>
public sealed class SmartTagsSettingsStore
{
    private readonly string _path = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
        "SmartTagsSettings", "config.json");

    /// <summary>Настройки — удобство, а не данные: при любой ошибке молча берём умолчания.</summary>
    public SmartTagsFormState Load()
    {
        try
        {
            if (!File.Exists(_path)) return new SmartTagsFormState();
            return JsonConvert.DeserializeObject<SmartTagsFormState>(File.ReadAllText(_path)) ?? new SmartTagsFormState();
        }
        catch
        {
            return new SmartTagsFormState();
        }
    }

    public void Save(SmartTagsFormState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonConvert.SerializeObject(state, Formatting.Indented));
        }
        catch
        {
            // Не сохранились — в следующий раз будут умолчания, работе это не мешает.
        }
    }
}
