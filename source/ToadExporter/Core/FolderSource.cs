using System.IO;
using System.Text.RegularExpressions;

namespace ToadExporter.Core;

/// <summary>
///     Дерево папки на диске: только модели .rvt, без резервных копий Revit (папок «_backup» и файлов «имя.0001.rvt»).
/// </summary>
public sealed class FolderSource(string root) : ITreeSource
{
    private static readonly Regex BackupFile = new(@"^.+\.\d{4}\.rvt$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public string RootPath { get; } = root;

    public Task<IReadOnlyList<SourceEntry>> GetChildrenAsync(string folderPath, CancellationToken ct) =>
        Task.Run<IReadOnlyList<SourceEntry>>(() => Read(folderPath, ct), ct);

    private static List<SourceEntry> Read(string folderPath, CancellationToken ct)
    {
        if (!Directory.Exists(folderPath)) throw new SourceException($"Папка не найдена: {folderPath}");

        var folders = SafeDirectories(folderPath)
            .Where(dir => { ct.ThrowIfCancellationRequested(); return ContainsModels(dir); })
            .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
            .Select(dir => new SourceEntry(Path.GetFileName(dir), dir, "", true));

        var models = Models(folderPath)
            .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
            .Select(file => new SourceEntry(Path.GetFileName(file), folderPath, file, false));

        return folders.Concat(models).ToList();
    }

    private static bool ContainsModels(string dir) => Models(dir).Any() || SafeDirectories(dir).Any(ContainsModels);

    private static IEnumerable<string> SafeDirectories(string dir)
    {
        try
        {
            return Directory.GetDirectories(dir)
                .Where(sub => !Path.GetFileName(sub).EndsWith("_backup", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    private static IEnumerable<string> Models(string dir)
    {
        try
        {
            return Directory.GetFiles(dir, "*.rvt")
                .Where(file => !BackupFile.IsMatch(Path.GetFileName(file)));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }
}
