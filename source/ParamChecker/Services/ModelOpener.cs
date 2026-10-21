using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Nice3point.Revit.Toolkit;
using ParamChecker.Core;
using ParamChecker.Core.Reports;
using ToadTools.UI.Progress;

namespace ParamChecker.Services;

/// <summary>
///     Открывает модель с отсоединением, не трогая исходник. С «Не загружать связи» модель сначала копируется
///     во временную папку (<c>CopyModel</c> умеет и Revit Server, и файл), а в копии всем связям RVT
///     ставится «не загружать» — модель открывается без связей, и закрывать ради этого их рабочие наборы не нужно.
/// </summary>
public sealed class ModelOpener(UIApplication uiApplication)
{
    private readonly Autodesk.Revit.ApplicationServices.Application _application = uiApplication.Application;

    /// <param name="sourcePath">Путь к модели на Revit Server (<c>RSN://…</c>) или к файлу.</param>
    /// <param name="worksetKeyword">Рабочие наборы с этим словом в имени не открываются; пусто — открываются все.</param>
    public Document Open(string sourcePath, string worksetKeyword, bool skipLinks, TempWorkspace temp, RunLog log,
        ToadProgress progress)
    {
        var path = sourcePath.Trim();
        var name = ModelNames.FromPath(path);
        var workshared = ModelNames.IsServerPath(path);

        if (skipLinks)
        {
            progress.ItemStatus("Копирование модели");
            log.Step(name, "копирование во временную папку");
            var copy = Path.Combine(temp.Directory, ModelNames.ToFileName(name) + ".rvt");
            using (new RevitProgressBridge(_application, progress, "Копирование модели"))
            {
                _application.CopyModel(ModelPathUtils.ConvertUserVisiblePathToModelPath(path), copy, true);
            }

            if (progress.ShouldAbortItem) throw new OperationCanceledException();

            log.Step(name, $"связей отключено: {UnloadLinks(copy)}");
            path = copy;
        }

        // На Revit Server лежат только модели совместной работы, файл нужно проверить.
        if (!workshared) workshared = BasicFileInfo.Extract(path).IsWorkshared;

        var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(path);
        var options = new OpenOptions();
        if (workshared)
        {
            options.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
            options.SetOpenWorksetsConfiguration(Worksets(modelPath, worksetKeyword));
        }

        progress.ItemStatus("Открытие модели");
        log.Step(name, worksetKeyword.Trim().Length > 0 ? $"открытие без РН с «{worksetKeyword.Trim()}»" : "открытие");

        Document document;
        using (RevitApiContext.BeginFailureSuppressionScope())
        using (RevitContext.BeginDialogSuppressionScope(OnDialogBoxShowing))
        using (new RevitProgressBridge(_application, progress, "Открытие модели"))
        {
            document = _application.OpenDocumentFile(modelPath, options)
                       ?? throw new InvalidOperationException("Revit не смог открыть модель");
        }

        // Revit не дал прервать открытие и дождался его конца — закрываем уже открытую модель.
        if (progress.ShouldAbortItem)
        {
            document.Close(false);
            throw new OperationCanceledException();
        }

        log.Step(name, "открыта");
        var links = new FilteredElementCollector(document).OfClass(typeof(RevitLinkType)).ToElementIds();
        if (links.Count > 0)
            log.Step(name, $"связей загружено: {links.Count(id => RevitLinkType.IsLoaded(document, id))} из {links.Count}");

        return document;
    }

    /// <summary>
    ///     Помечает в копии все связи RVT «не загружать». Revit учитывает это только у файла с пометкой «передан»,
    ///     поэтому копия помечается переданной — копия временная, сохранять её не нужно. Возвращает число связей.
    /// </summary>
    private static int UnloadLinks(string filePath)
    {
        var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);
        var data = TransmissionData.ReadTransmissionData(modelPath);
        if (data is null) return 0;

        var count = 0;
        foreach (var id in data.GetAllExternalFileReferenceIds())
        {
            var reference = data.GetLastSavedReferenceData(id);
            if (reference.ExternalFileReferenceType != ExternalFileReferenceType.RevitLink) continue;

            data.SetDesiredReferenceData(id, reference.GetPath(), reference.PathType, false);
            count++;
        }

        if (count == 0) return 0;

        data.IsTransmitted = true;
        TransmissionData.WriteTransmissionData(modelPath, data);
        return count;
    }

    private static WorksetConfiguration Worksets(ModelPath modelPath, string keyword)
    {
        keyword = keyword.Trim();
        if (keyword.Length == 0) return new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets);

        var open = WorksharingUtils.GetUserWorksetInfo(modelPath)
            .Where(workset => workset.Name.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) < 0)
            .Select(workset => workset.Id)
            .ToList();

        var configuration = new WorksetConfiguration(WorksetConfigurationOption.CloseAllWorksets);
        configuration.Open(open);
        return configuration;
    }

    /// <summary>Обновить версию — да, отсоединить — да, остальное — ответ по умолчанию.</summary>
    private static void OnDialogBoxShowing(DialogBoxShowingEventArgs args)
    {
        switch (args.DialogId)
        {
            case "Dialog_Revit_DocUpgrade":
                args.OverrideResult((int)TaskDialogResult.Ok);
                break;
            case "TaskDialog_DetachModel":
                args.OverrideResult((int)TaskDialogResult.Yes);
                break;
            default:
                args.OverrideResult(1001);
                break;
        }
    }
}
