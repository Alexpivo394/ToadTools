#nullable enable
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Threading;

namespace ToadTools.UI.Services;

/// <summary>
///     Журнал работы ToadTools в <c>%LocalAppData%\ToadTools\Logs\ToadTools-yyyy-MM-dd.log</c>, файлы хранятся 7 дней.
///     Пишет один фоновый поток из очереди: вызывающий не ждёт диск, из любых потоков интерфейса безопасно, ничего не
///     бросает. Не писать сюда токены, заголовки, тела запросов и ответов, пароли, адреса с query.
///     <para>
///         Сбои для разработчика по-прежнему идут в <c>Revit-errors.log</c> (<c>ErrorReport</c>), этот файл
///         журнал не затрагивает.
///     </para>
/// </summary>
public static class ToadLog
{
    private const string Prefix = "ToadTools-";
    private const string Extension = ".log";
    private const int KeepDays = 7;

    private static readonly object Gate = new();
    private static readonly BlockingCollection<Entry> Queue = new();
    private static string? _settingsPath;
    private static bool? _verbose;
    private static Thread? _writer;
    private static string? _pruned;

    /// <summary>Папка журналов; та же, что у <c>ErrorReport</c>.</summary>
    public static string Folder { get; internal set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToadTools", "Logs");

    /// <summary>Файл настроек; тесты подменяют, чтобы не трогать настоящий.</summary>
    internal static string SettingsPath
    {
        get => _settingsPath ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToadTools", "logging.json");
        set
        {
            _settingsPath = value;
            _verbose = null;
        }
    }

    /// <summary>Подробный журнал; значение хранится в <c>logging.json</c> и действует сразу.</summary>
    public static bool Verbose
    {
        get => _verbose ??= LoadVerbose();
        set
        {
            _verbose = value;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, SettingsJson.Write(new LogSettings { Verbose = value }));
            }
            catch (Exception)
            {
                // Настройка — удобство: не записалась, значит действует до закрытия Revit.
            }
        }
    }

    public static void Info(string area, string message) => Add("INFO", area, message);

    /// <summary>Подробность: пишется только при включённом <see cref="Verbose" />.</summary>
    public static void Debug(string area, string message)
    {
        if (Verbose) Add("DEBUG", area, message);
    }

    public static void Error(string area, Exception exception) => Add("ERROR", area, exception.ToString());

    /// <summary>Удаляет все журналы <c>ToadTools-*.log</c>; <c>Revit-errors.log</c> остаётся.</summary>
    public static void Clear()
    {
        Flush();
        try
        {
            if (!Directory.Exists(Folder)) return;
            foreach (var file in Directory.GetFiles(Folder, Prefix + "*" + Extension))
                TryDelete(file);
        }
        catch (Exception)
        {
            // Папка недоступна — чистить нечего.
        }
    }

    /// <summary>Дописывает очередь на диск; вызывается при выходе из Revit.</summary>
    public static void Flush()
    {
        try
        {
            if (_writer is null) return;
            using var done = new ManualResetEventSlim();
            Queue.Add(new Entry(null, null, done));
            done.Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception)
        {
            // Выход из Revit важнее журнала.
        }
    }

    internal static string FileFor(DateTime day) => Prefix + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + Extension;

    /// <summary>Удаляет журналы старше 7 дней по дате в имени; чужие файлы не трогает.</summary>
    internal static void Prune(string folder, DateTime now)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            var border = now.Date.AddDays(-KeepDays);
            foreach (var file in Directory.GetFiles(folder, Prefix + "*" + Extension))
            {
                var stem = Path.GetFileNameWithoutExtension(file).Substring(Prefix.Length);
                if (DateTime.TryParseExact(stem, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day < border)
                    TryDelete(file);
            }
        }
        catch (Exception)
        {
            // Не вышло сегодня — получится при следующем запуске.
        }
    }

    private static void Add(string level, string area, string message)
    {
        try
        {
            var now = DateTime.Now;
            var line = $"{now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {area}: {message}";
            EnsureWriter();
            Queue.Add(new Entry(Path.Combine(Folder, FileFor(now)), line, null));
        }
        catch (Exception)
        {
            // Журнал не должен ломать работу.
        }
    }

    private static void EnsureWriter()
    {
        if (_writer is not null) return;
        lock (Gate)
        {
            if (_writer is not null) return;
            var thread = new Thread(Drain) { IsBackground = true, Name = "ToadLog" };
            thread.Start();
            _writer = thread;
        }
    }

    private static void Drain()
    {
        foreach (var entry in Queue.GetConsumingEnumerable())
        {
            try
            {
                if (entry.Path is not null)
                {
                    var folder = Path.GetDirectoryName(entry.Path)!;
                    Directory.CreateDirectory(folder);
                    if (!string.Equals(_pruned, folder, StringComparison.OrdinalIgnoreCase))
                    {
                        _pruned = folder;
                        Prune(folder, DateTime.Now);
                    }

                    File.AppendAllText(entry.Path, entry.Line + Environment.NewLine);
                }
            }
            catch (Exception)
            {
                // Папка занята или нет прав: строка теряется, поток живёт.
            }
            finally
            {
                entry.Done?.Set();
            }
        }
    }

    private static bool LoadVerbose()
    {
        try
        {
            return File.Exists(SettingsPath) && SettingsJson.Read<LogSettings>(File.ReadAllText(SettingsPath))?.Verbose == true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception)
        {
            // Файл занят — останется до следующей чистки.
        }
    }

    private sealed class Entry(string? path, string? line, ManualResetEventSlim? done)
    {
        public string? Path { get; } = path;
        public string? Line { get; } = line;
        public ManualResetEventSlim? Done { get; } = done;
    }
}
