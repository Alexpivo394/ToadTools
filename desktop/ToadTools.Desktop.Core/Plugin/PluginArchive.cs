using System.IO.Compression;
using System.Text.RegularExpressions;

namespace ToadTools.Desktop.Core.Plugin;

/// <summary>Архив плагина не того вида: путь за пределы папки, лишнее на верхнем уровне, ни одного полного года.</summary>
public sealed class InvalidPluginArchiveException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
///     Архив плагина ToadTools-{версия}.zip: {год}/ToadTools.addin и {год}/ToadTools/** — раскладка папки publish
///     сборки. Распаковывается во временную папку staging\{версия}, откуда плагин ставится после закрытия Revit.
/// </summary>
public static class PluginArchive
{
    public const string AddinFile = "ToadTools.addin";
    public const string PluginFolder = "ToadTools";

    private static readonly Regex YearName = new(@"^\d{4}$");

    /// <summary>
    ///     Проверяет архив целиком, распаковывает в staging\{версия}.tmp и переименовывает в staging\{версия}.
    ///     Другие версии в staging удаляются: ждёт установки только самая новая.
    /// </summary>
    public static string Extract(string zipPath, string stagingDir, Version version)
    {
        var target = Path.Combine(stagingDir, version.ToString());
        var temporary = target + ".tmp";
        Directory.CreateDirectory(stagingDir);
        DeleteDirectory(temporary);

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var entries = archive.Entries.Select(entry => (Entry: entry, Path: Validate(entry.FullName))).ToArray();

            foreach (var (entry, relative) in entries)
            {
                var destination = Path.Combine(temporary, relative.Replace('/', Path.DirectorySeparatorChar));
                if (relative.EndsWith("/"))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, true);
            }
        }
        catch (InvalidDataException exception)
        {
            DeleteDirectory(temporary);
            throw new InvalidPluginArchiveException($"Архив плагина повреждён: {exception.Message}", exception);
        }
        catch
        {
            DeleteDirectory(temporary);
            throw;
        }

        if (!Directory.Exists(temporary) || Years(temporary).Count == 0)
        {
            DeleteDirectory(temporary);
            throw new InvalidPluginArchiveException("В архиве нет ни одной версии Revit с ToadTools.addin и папкой ToadTools");
        }

        DeleteDirectory(target);
        Directory.Move(temporary, target);

        foreach (var other in Directory.GetDirectories(stagingDir))
        {
            if (!string.Equals(other, target, StringComparison.OrdinalIgnoreCase)) DeleteDirectory(other);
        }

        return target;
    }

    /// <summary>Годы Revit, для которых в распакованной версии есть ToadTools.addin и папка ToadTools.</summary>
    public static IReadOnlyList<int> Years(string stagedDir) =>
        Directory.GetDirectories(stagedDir)
            .Select(Path.GetFileName)
            .Where(name => YearName.IsMatch(name!))
            .Where(name => File.Exists(Path.Combine(stagedDir, name!, AddinFile)) &&
                           Directory.Exists(Path.Combine(stagedDir, name!, PluginFolder)))
            .Select(name => int.Parse(name!))
            .OrderBy(year => year)
            .ToArray();

    /// <summary>Относительный путь записи через «/»; путь за пределы архива или не в папке года — исключение.</summary>
    private static string Validate(string fullName)
    {
        var path = fullName.Replace('\\', '/');
        var segments = path.Split('/');
        if (path.StartsWith("/") || path.Contains(":") || segments.Any(segment => segment == ".."))
            throw new InvalidPluginArchiveException($"Недопустимый путь в архиве плагина: {fullName}");
        if (!YearName.IsMatch(segments[0]) || segments.Length < 2)
            throw new InvalidPluginArchiveException($"В архиве плагина лишнее вне папок версий Revit: {fullName}");
        return path;
    }

    internal static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}
