namespace ToadTools.Desktop.Core;

/// <summary>Простой файловый лог: logs\ToadTools-yyyy-MM-dd.log, файлы старше 14 дней удаляются. Ошибки записи глотаются.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _folder;

    public static void Configure(string logsDir)
    {
        lock (Gate)
        {
            _folder = logsDir;
            try
            {
                Directory.CreateDirectory(logsDir);
                foreach (var file in Directory.GetFiles(logsDir, "ToadTools-*.log"))
                {
                    if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-14)) File.Delete(file);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception == null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        lock (Gate)
        {
            if (_folder == null) return;
            try
            {
                var file = Path.Combine(_folder, $"ToadTools-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
