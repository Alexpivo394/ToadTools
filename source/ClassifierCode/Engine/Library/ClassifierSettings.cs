using System.IO;
using Newtonsoft.Json;

namespace ClassifierCode.Engine.Library;

/// <summary>Настройки пользователя: общие папки профилей и последний выбранный профиль. Ошибка чтения — умолчания.</summary>
public sealed class ClassifierSettings
{
    public List<string> SharedFolders { get; set; } = [];
    public Guid? LastProfileId { get; set; }

    /// <summary>Редактор показывает все вкладки, а не четыре основные.</summary>
    public bool ShowAllEditorTabs { get; set; }

    /// <summary>Окно «Выгрузка для ИИ»: последний источник, объект и папка.</summary>
    public AiExportSettings AiExport { get; set; } = new();

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToadTools", "ClassifierCode", "settings.json");

    public static ClassifierSettings Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonConvert.DeserializeObject<ClassifierSettings>(File.ReadAllText(path)) ?? new ClassifierSettings() : new ClassifierSettings();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ClassifierSettings();
        }
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Настройки — удобство: не записались, значит в следующий раз будут умолчания.
        }
    }
}

/// <summary>Источник: "server", "folder" или "current".</summary>
public sealed class AiExportSettings
{
    public string Source { get; set; } = "server";
    public string Server { get; set; } = string.Empty;
    public string SourceFolder { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public Guid? BaseProfileId { get; set; }
}
