using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;
using ToadTools.UI.Services;

namespace ToadTools.Commands;

/// <summary>
///     Команда ленты ToadTools. Сбой, которого инструмент не обработал сам, не уходит в Revit (его окно причины не
///     показывает), а показывается окном ToadTools и пишется в журнал <see cref="ErrorReport.LogPath" />; изменения
///     команды в модели при этом откатываются.
/// </summary>
public abstract class ToadCommand : ExternalCommand
{
    public sealed override void Execute()
    {
        try
        {
            Run();
        }
        catch (OperationCanceledException)
        {
            Result = Result.Cancelled;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            // Человек нажал Esc при выборе элементов.
            Result = Result.Cancelled;
        }
        catch (Exception exception)
        {
            ErrorReport.Show(GetType().Name.Replace("Command", string.Empty), exception);
            Result = Result.Failed;
        }
    }

    /// <summary>Работа команды.</summary>
    protected abstract void Run();
}
