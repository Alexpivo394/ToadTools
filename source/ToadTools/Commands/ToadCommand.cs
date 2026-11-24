using System.Diagnostics;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;
using ToadTools.Telemetry;
using ToadTools.UI.Services;

namespace ToadTools.Commands;

/// <summary>
///     Команда ленты ToadTools. Сбой, которого инструмент не обработал сам, не уходит в Revit (его окно причины не
///     показывает), а показывается окном ToadTools и пишется в журнал <see cref="ErrorReport.LogPath" />; изменения
///     команды в модели при этом откатываются. Каждый запуск учитывается аналитикой ToadFlow: исход и длительность.
/// </summary>
public abstract class ToadCommand : ExternalCommand
{
    public sealed override void Execute()
    {
        var watch = Stopwatch.StartNew();
        var outcome = CommandOutcome.Succeeded;
        try
        {
            Run();
            outcome = Result switch
            {
                Result.Failed => CommandOutcome.Failed,
                Result.Cancelled => CommandOutcome.Cancelled,
                _ => CommandOutcome.Succeeded
            };
        }
        catch (OperationCanceledException)
        {
            Result = Result.Cancelled;
            outcome = CommandOutcome.Cancelled;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            // Человек нажал Esc при выборе элементов.
            Result = Result.Cancelled;
            outcome = CommandOutcome.Cancelled;
        }
        catch (Exception exception)
        {
            ErrorReport.Show(GetType().Name.Replace("Command", string.Empty), exception);
            Result = Result.Failed;
            outcome = CommandOutcome.Failed;
        }
        finally
        {
            // Учёт не меняет результата команды: TelemetryHost сам ловит свои сбои.
            TelemetryHost.ToadToolsCommand(GetType(), outcome, watch.ElapsedMilliseconds);
        }
    }

    /// <summary>Работа команды.</summary>
    protected abstract void Run();
}
