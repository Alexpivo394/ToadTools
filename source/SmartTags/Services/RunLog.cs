using System.Globalization;
using System.IO;
using System.Text;

namespace SmartTags.Services;

/// <summary>Журнал запуска для разбора проблем. Пишется в файл, только если включён в окне.</summary>
public sealed class RunLog(bool enabled)
{
    private readonly StringBuilder _buffer = new();
    private readonly DateTime _started = DateTime.Now;
    private readonly object _lock = new();

    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToadTools", "Logs");

    /// <summary>Для передачи в сервисы: null, если журнал выключен (чтобы не форматировать строки зря).</summary>
    public Action<string>? Writer => enabled ? Write : null;

    public void Write(string message)
    {
        if (!enabled) return;

        lock (_lock)
        {
            _buffer.Append(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture))
                .Append("  ")
                .AppendLine(message);
        }
    }

    /// <summary>Сохраняет журнал и возвращает путь к файлу (null — журнал выключен или не записался).</summary>
    public string? Save()
    {
        if (!enabled) return null;

        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = Path.Combine(Directory,
                $"SmartTags_{_started.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.log");
            lock (_lock)
            {
                File.WriteAllText(path, _buffer.ToString(), Encoding.UTF8);
            }

            return path;
        }
        catch
        {
            return null;
        }
    }
}
