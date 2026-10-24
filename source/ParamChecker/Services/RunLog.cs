using System.IO;
using System.Text;
using Alexpivo394.Revit.ParamCheck.Core;

namespace ParamChecker.Services;

/// <summary>
///     Журнал запуска в папке отчётов. Пишется сразу на диск, чтобы остаться, даже если Revit упадёт посреди проверки.
/// </summary>
public sealed class RunLog : ICheckLog, IDisposable
{
    private readonly StreamWriter _writer;

    private RunLog(string filePath)
    {
        FilePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        _writer = new StreamWriter(new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(true)) { AutoFlush = true };
    }

    public string FilePath { get; }

    public static RunLog Create(string folder, DateTime now) =>
        new(Path.Combine(folder, $"ParamChecker_{now:yyyy-MM-dd_HH-mm-ss}.log"));

    public void Line(string text) => _writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] {text}");

    public void Step(string model, string text) => Line($"{model}: {text}");

    public void Error(string model, string text, Exception exception)
    {
        Line($"{model}: ОШИБКА — {text}: {exception.Message}");
        _writer.WriteLine(exception);
    }

    public void Blank() => _writer.WriteLine();

    public void Dispose() => _writer.Dispose();
}
