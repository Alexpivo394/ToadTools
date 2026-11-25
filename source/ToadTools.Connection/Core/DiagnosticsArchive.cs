using System.IO;
using System.IO.Compression;
using System.Text;

namespace ToadTools.Connection;

/// <summary>
///     Архив для разработчика: журналы, версии и файлы настроек ToadTools. Подключение к ToadFlow (файл
///     <see cref="ConnectionStore.FileName" /> и всё, где лежит токен) в архив не попадает.
/// </summary>
public static class DiagnosticsArchive
{
    /// <summary>Собирает zip: <c>Logs\*</c>, <c>versions.txt</c>, <c>Settings\*.json</c>; существующий файл заменяется.</summary>
    /// <param name="appData">Папка настроек, <c>%AppData%\ToadTools</c>: берутся только её <c>*.json</c> без вложенных папок.</param>
    /// <param name="logs">Папка журналов, <c>%LocalAppData%\ToadTools\Logs</c>.</param>
    /// <param name="versionsText">Содержимое <c>versions.txt</c>.</param>
    /// <param name="targetZip">Куда положить архив.</param>
    public static void Create(string appData, string logs, string versionsText, string targetZip)
    {
        using var stream = new FileStream(targetZip, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        if (Directory.Exists(logs))
            foreach (var file in Directory.GetFiles(logs))
                Add(archive, file, "Logs/" + Path.GetFileName(file));

        var versions = archive.CreateEntry("versions.txt");
        using (var writer = new StreamWriter(versions.Open(), new UTF8Encoding(false)))
            writer.Write(versionsText);

        if (Directory.Exists(appData))
            foreach (var file in Directory.GetFiles(appData, "*.json"))
                if (!IsConnection(file))
                    Add(archive, file, "Settings/" + Path.GetFileName(file));
    }

    /// <summary>Файл подключения или любой файл с токеном — даже если ToadFlow когда-нибудь положит его под другим именем.</summary>
    private static bool IsConnection(string file)
    {
        var name = Path.GetFileName(file);
        if (name.StartsWith("toadflow", StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            return File.ReadAllText(file).IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true; // не прочитали — не знаем, что внутри
        }
    }

    private static void Add(ZipArchive archive, string file, string entryName)
    {
        try
        {
            // Журнал может быть открыт фоновым писателем: читаем, не мешая ему.
            using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var target = archive.CreateEntry(entryName).Open();
            source.CopyTo(target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Файл занят или пропал: архив собирается без него.
        }
    }
}
