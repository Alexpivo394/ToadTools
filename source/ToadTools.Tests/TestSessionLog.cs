using ToadTools.UI.Services;

namespace ToadTools.Tests;

/// <summary>
/// Журнал ToadTools на время тестов — во временной папке: код, который пишет в журнал
/// (панели, клиент ToadFlow), иначе дописывал бы ошибки тестов в журнал пользователя.
/// </summary>
public static class TestSessionLog
{
    public static string Folder { get; private set; } = null!;

    public static string SettingsPath { get; private set; } = null!;

    private static string _root = null!;

    [Before(TestSession)]
    public static void Isolate()
    {
        _root = Path.Combine(Path.GetTempPath(), "ToadTools.Tests-" + Guid.NewGuid().ToString("N"));
        Folder = Path.Combine(_root, "Logs");
        SettingsPath = Path.Combine(_root, "logging.json");
        ToadLog.Folder = Folder;
        ToadLog.SettingsPath = SettingsPath;
    }

    [After(TestSession)]
    public static void Remove()
    {
        ToadLog.Flush();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
