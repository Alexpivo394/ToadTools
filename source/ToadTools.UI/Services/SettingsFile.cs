#nullable enable
using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace ToadTools.UI.Services;

/// <summary>
///     Настройки одного инструмента в <c>%AppData%\ToadTools\{инструмент}\config.json</c>. Папка плагина для этого не
///     годится: она заменяется при каждом обновлении и бывает без прав на запись.
///     <para>
///         Настройки — удобство, а не данные: файл не прочитался или не записался — инструмент работает с умолчаниями.
///     </para>
/// </summary>
/// <typeparam name="T">Класс настроек с конструктором без параметров — он же даёт умолчания.</typeparam>
public sealed class SettingsFile<T> where T : class, new()
{
    private readonly string? _legacyPath;

    /// <param name="tool">Имя инструмента — папка в <c>%AppData%\ToadTools</c>.</param>
    /// <param name="legacyFolder">
    ///     Папка рядом с dll, где настройки лежали в прежних версиях; из неё они читаются, пока новых нет.
    /// </param>
    public SettingsFile(string tool, string? legacyFolder = null)
    {
        Path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToadTools", tool, "config.json");

        var location = Assembly.GetExecutingAssembly().Location;
        if (legacyFolder is not null && !string.IsNullOrEmpty(location))
            _legacyPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(location)!, legacyFolder, "config.json");
    }

    /// <summary>Файл настроек.</summary>
    public string Path { get; }

    /// <summary>Сохранённые настройки; их нет или файл не читается — умолчания.</summary>
    public T Load() => Read(Path) ?? (_legacyPath is null ? null : Read(_legacyPath)) ?? new T();

    /// <summary>Запоминает настройки.</summary>
    /// <returns><c>false</c> — записать не получилось, в следующий раз будут прежние.</returns>
    public bool Save(T settings)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, SettingsJson.Write(settings));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static T? Read(string path)
    {
        try
        {
            return File.Exists(path) ? SettingsJson.Read<T>(File.ReadAllText(path)) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
