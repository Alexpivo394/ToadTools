using System.IO;

namespace ParamChecker.Services;

/// <summary>
///     Временная папка одной модели в %TEMP%\ParamChecker: сюда копируется модель, чтобы отключить в копии связи.
///     Пока папка в работе, в ней открыт файл-замок — по нему уборка отличает брошенные папки от занятых
///     (например, проверкой в другом окне Revit).
/// </summary>
public sealed class TempWorkspace : IDisposable
{
    private const string LockName = ".lock";

    private readonly FileStream _lock;

    private TempWorkspace(string directory)
    {
        Directory = directory;
        _lock = new FileStream(Path.Combine(directory, LockName), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
    }

    public static string Root => Path.Combine(Path.GetTempPath(), "ParamChecker");

    public string Directory { get; }

    public static TempWorkspace Create() =>
        new(System.IO.Directory.CreateDirectory(Path.Combine(Root, Guid.NewGuid().ToString("N"))).FullName);

    /// <summary>Удаляет копии, оставшиеся от прошлых запусков (Revit упал или копию не удалось удалить).</summary>
    public static void CleanupUnused()
    {
        if (!System.IO.Directory.Exists(Root)) return;

        foreach (var dir in System.IO.Directory.GetDirectories(Root))
        {
            if (!IsInUse(dir)) TryDelete(dir);
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
        TryDelete(Directory);
    }

    private static bool IsInUse(string dir)
    {
        var lockPath = Path.Combine(dir, LockName);
        if (!File.Exists(lockPath)) return false;

        try
        {
            using (new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static void TryDelete(string dir)
    {
        try
        {
            System.IO.Directory.Delete(dir, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Файл ещё держит Revit — удалим при следующем запуске.
        }
    }
}
