using System.IO;

namespace WarmSync;

public class Logger
{
    private static readonly string DefaultLogFilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WarmSyncLog.txt");

    private string _logFilePath = DefaultLogFilePath;
    
    public void StartLog(string? logFilePath)
    {
        _logFilePath = string.IsNullOrEmpty(logFilePath) ? DefaultLogFilePath : logFilePath!;
        File.WriteAllText(_logFilePath, $"Лог начат: {DateTime.Now}\n\n");
    }
    
    public void Log(string message)
    {
        string logEntry = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
        File.AppendAllText(_logFilePath, logEntry);
    }

    public void LogError(string message, Exception ex)
    {
        string logEntry = $"[{DateTime.Now:HH:mm:ss}] ОШИБКА: {message}\n{ex.Message}\n{ex.StackTrace}\n";
        File.AppendAllText(_logFilePath, logEntry);
    }
}