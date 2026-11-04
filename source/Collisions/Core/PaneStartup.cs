using System.IO;

namespace Collisions.Core;

/// <summary>
///     Запуск панели «Коллизии»: сбой регистрации (ресурс XAML, конфликт сборок с другим аддином, повторная регистрация) не
///     должен оборвать запуск ToadTools — лента создаётся всегда, а кнопка «Коллизии» говорит, почему панели нет.
/// </summary>
public sealed class PaneStartup(string logPath)
{
    public static string DefaultLogPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToadTools", "Clash", "startup.log");

    /// <summary>Причина, по которой панель не запустилась; null — запустилась (или ещё не запускалась).</summary>
    public string? Failure { get; private set; }

    /// <summary>Что сказать по кнопке «Коллизии», если панели нет; null — панель есть.</summary>
    public string? ToggleMessage => Failure is null ? null : $"Панель не запустилась: {Failure}";

    /// <summary>Регистрирует панель; исключение записывается в журнал и запоминается, наружу не выходит.</summary>
    public bool TryRegister(Action register)
    {
        try
        {
            register();
            Failure = null;
            return true;
        }
        catch (Exception exception)
        {
            Failure = exception.Message;
            Log(exception);
            return false;
        }
    }

    private void Log(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} Панель «Коллизии» не зарегистрирована:{Environment.NewLine}{exception}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // журнал недоступен — причина всё равно видна по кнопке
        }
    }
}
