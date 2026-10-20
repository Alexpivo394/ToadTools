using System.IO;

namespace ToadExporter.Core;

/// <summary>Проверки перед запуском выгрузки — чтобы не начинать то, что заведомо не получится или испортит исходники.</summary>
public static class RunValidator
{
    public static IReadOnlyList<string> Errors(IReadOnlyList<ModelJob> jobs, ExportSettings settings)
    {
        var errors = new List<string>();

        if (jobs.Count == 0) errors.Add("Не отмечено ни одной модели.");

        var output = OutputFolder(settings, errors);

        if (jobs.Any(job => job.Nwc) && string.IsNullOrWhiteSpace(settings.NwcViewName))
            errors.Add("Укажите имя 3D-вида для NWC.");

        if (jobs.Any(job => job.Nwc) && !(settings.NwcFacetingFactor > 0))
            errors.Add("Коэффициент фасетизации NWC должен быть больше нуля.");

        if (output is not null) CheckSourceFolders(jobs, output, errors);

        return errors;
    }

    /// <summary>
    ///     Пробный файл в папке результатов: проверяет запись и ловит папку модели, совпадающую с папкой
    ///     результатов через подключённый диск, UNC-путь или junction, — по строке пути их не сравнить.
    /// </summary>
    private static void CheckSourceFolders(IReadOnlyList<ModelJob> jobs, string output, List<string> errors)
    {
        var probe = Path.Combine(output, $".toadexporter-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probe, "");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add($"Нет доступа на запись в папку результатов: {exception.Message}");
            return;
        }

        try
        {
            foreach (var job in jobs.Where(job => !job.IsServer))
            {
                var folder = Path.GetDirectoryName(Path.GetFullPath(job.SourcePath));
                if (folder is not null && (Same(folder, output) || File.Exists(Path.Combine(folder, Path.GetFileName(probe)))))
                    errors.Add($"Папка результатов совпадает с папкой модели {job.Name} — исходник был бы перезаписан.");
            }
        }
        finally
        {
            try
            {
                File.Delete(probe);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Пробный файл остался — он пустой и ничему не мешает.
            }
        }
    }

    /// <summary>Модели с одинаковым именем файла — в общей папке результатов они перезапишут друг друга.</summary>
    public static IReadOnlyList<IGrouping<string, ModelJob>> Duplicates(IReadOnlyList<ModelJob> jobs) =>
        jobs.GroupBy(job => job.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToList();

    private static string? OutputFolder(ExportSettings settings, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(settings.OutputFolder))
        {
            errors.Add("Укажите папку для результатов.");
            return null;
        }

        try
        {
            var full = Path.GetFullPath(settings.OutputFolder.Trim());
            Directory.CreateDirectory(full);
            return full;
        }
        catch (Exception exception)
        {
            errors.Add($"Не удалось создать папку для результатов: {exception.Message}");
            return null;
        }
    }

    private static bool Same(string? a, string b) =>
        a is not null && string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
