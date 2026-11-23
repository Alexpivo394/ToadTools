using System.IO;
using System.Reflection;
using Autodesk.Revit.DB;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ToadTools.UI.Services;
using Alexpivo394.Revit.ParamCheck.Profiles;

namespace ParamChecker.Services;

/// <summary>
///     Хранит настройки и профили пользователя в %AppData%\ToadTools\ParamChecker. Папка плагина для этого не годится:
///     она общая для всех пользователей компьютера, может быть без прав на запись и пересоздаётся при обновлении.
/// </summary>
public sealed class SettingsStore
{
    private static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToadTools", "ParamChecker");

    private readonly string _settingsPath = Path.Combine(Folder, "settings.json");
    private readonly string _profilesPath = Path.Combine(Folder, "profiles.json");

    /// <summary>Как <see cref="SettingsJson.Options" />, но пустые поля переноса из прежних версий в файл не пишутся.</summary>
    private static readonly JsonSerializerOptions Json = new(SettingsJson.Options)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { OmitEmptyLegacy } }
    };

    /// <summary>Имя BuiltInCategory по числу — для профилей старого формата.</summary>
    public static string? CategoryName(int id) =>
        // Значение — само перечисление: с Revit 2024 его базовый тип long, до того — int, а IsDefined требует точный тип.
        Enum.IsDefined(typeof(BuiltInCategory), (BuiltInCategory)id) ? ((BuiltInCategory)id).ToString() : null;

    /// <summary>Настройки — удобство, а не данные: при ошибке берём умолчания.</summary>
    public CheckerSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
                return Read(File.ReadAllText(_settingsPath)) ?? new CheckerSettings();

            return LegacySettings() ?? new CheckerSettings();
        }
        catch
        {
            return new CheckerSettings();
        }
    }

    public void SaveSettings(CheckerSettings settings) => Write(_settingsPath, JsonSerializer.Serialize(settings, Json));

    /// <summary>Профили с прошлого раза; пусто — если их ещё не было или файл не читается.</summary>
    /// <param name="error">Почему файл профилей не прочитался — показать пользователю, чтобы он не потерял их молча.</param>
    public ProfileSet LoadProfiles(out string? error)
    {
        error = null;
        if (!File.Exists(_profilesPath)) return new ProfileSet();

        try
        {
            return ProfileSerializer.Deserialize(File.ReadAllText(_profilesPath), CategoryName);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            // Не затираем нечитаемый файл пустыми профилями — откладываем его рядом.
            var backup = _profilesPath + $".{DateTime.Now:yyyyMMdd-HHmmss}.bak";
            TryCopy(_profilesPath, backup);
            error = $"Профили с прошлого раза не загружены ({exception.Message}). Файл сохранён как {backup}";
            return new ProfileSet();
        }
    }

    public void SaveProfiles(ProfileSet set) => Write(_profilesPath, ProfileSerializer.Serialize(set));

    private static CheckerSettings? Read(string json) => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<CheckerSettings>(json, Json);

    private static void OmitEmptyLegacy(JsonTypeInfo type)
    {
        if (type.Type != typeof(CheckerSettings)) return;
        foreach (var property in type.Properties)
            if (property.Name is "ToadFlowUrl" or "ToadFlowTokenProtected")
                property.ShouldSerialize = (_, value) => value is string { Length: > 0 };
    }

    /// <summary>Настройки старых версий лежали в ParamCheckerSettings\config.json рядом с dll.</summary>
    private static CheckerSettings? LegacySettings()
    {
        var location = Assembly.GetExecutingAssembly().Location;
        if (string.IsNullOrEmpty(location)) return null;

        var path = Path.Combine(Path.GetDirectoryName(location)!, "ParamCheckerSettings", "config.json");
        if (!File.Exists(path)) return null;

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var json = document.RootElement;
        if (json.ValueKind != JsonValueKind.Object) return null;

        var settings = new CheckerSettings
        {
            SkipLinks = !json.TryGetProperty("SkipLinks", out var skipLinks) || skipLinks.ValueKind != JsonValueKind.False
        };
        if (json.TryGetProperty("ReportsPath", out var reportsPath) && reportsPath.ValueKind == JsonValueKind.String && reportsPath.GetString() is { Length: > 0 } reports)
            settings.ReportsFolder = reports;
        return settings;
    }

    private static void Write(string path, string content)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var temp = path + ".tmp";
            File.WriteAllText(temp, content);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Не сохранилось — в следующий раз будут прежние данные; работе это не мешает.
        }
    }

    private static void TryCopy(string from, string to)
    {
        try
        {
            File.Copy(from, to, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
