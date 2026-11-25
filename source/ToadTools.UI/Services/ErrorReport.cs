#nullable enable
using System;
using System.IO;
using ToadTools.UI.Models;

namespace ToadTools.UI.Services;

/// <summary>
///     Сбой, которого инструмент не ждал: человеку — окно с причиной, разработчику — запись со стеком в
///     <c>%LocalAppData%\ToadTools\Logs\Revit-errors.log</c>. Без этого Revit показывает собственное окно
///     «не удалось выполнить внешнюю команду», из которого причина не видна.
/// </summary>
public static class ErrorReport
{
    public static string LogPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToadTools", "Logs", "Revit-errors.log");

    /// <summary>Пишет сбой в журнал и показывает окно с причиной и путём к журналу.</summary>
    /// <param name="where">Где случилось: команда или этап запуска.</param>
    public static void Show(string where, Exception exception)
    {
        var logged = Log(where, exception);
        var details = logged ? $"\n\nПодробности записаны в журнал:\n{LogPath}" : string.Empty;
        try
        {
            ToadDialogService.Show("Ошибка ToadTools", $"{where}: {Root(exception).Message}{details}", DialogButtons.OK, DialogIcon.Error);
        }
        catch (Exception)
        {
            // Окно не показать (нет потока интерфейса, не загрузилась тема) — запись в журнале остаётся.
        }
    }

    /// <summary>Только запись в журнал, без окна.</summary>
    /// <returns><c>false</c> — журнал недоступен.</returns>
    public static bool Log(string where, Exception exception)
    {
        ToadLog.Error(where, exception);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {where}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Исходная причина: контейнер и отражение оборачивают её в свои исключения без полезного текста.</summary>
    private static Exception Root(Exception exception)
    {
        while (exception.InnerException is { } inner) exception = inner;
        return exception;
    }
}
