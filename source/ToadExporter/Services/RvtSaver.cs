using System.IO;
using Autodesk.Revit.DB;
using Nice3point.Revit.Toolkit;
using ToadExporter.Core;
using ToadTools.UI.Progress;

namespace ToadExporter.Services;

/// <summary>
///     Готовит открытую (отсоединённую) модель к передаче и сохраняет её. Отсоединённую модель совместной работы
///     Revit сохраняет только файлом хранилища, поэтому она сохраняется во временную папку, а в папку результатов
///     из неё создаётся локальная копия (<see cref="CreateLocal" />) — так же, как RevitServerTool createLocalRVT.
/// </summary>
public static class RvtSaver
{
    private const int PurgePasses = 5;

#if REVIT2024_OR_GREATER
    public static bool PurgeAvailable => true;
#else
    public static bool PurgeAvailable => false;
#endif

    /// <param name="isTransmitted">
    ///     Модель открыта из копии, помеченной как переданная. Revit снимает эту пометку только с такой модели —
    ///     для остальных <see cref="WorksharingSaveAsOptions.ClearTransmitted" /> бросает исключение.
    /// </param>
    /// <param name="filePath">Куда сохранить: модель совместной работы — во временную папку, см. <see cref="CreateLocal" />.</param>
    /// <returns>Пояснение для журнала: что удалено.</returns>
    public static string Save(Document document, string filePath, bool isTransmitted, ExportSettings settings, ToadProgress progress)
    {
        progress.ItemStatus("Подготовка RVT");
        var removed = Prepare(document, settings);

        if (progress.ShouldAbortItem) throw new OperationCanceledException();

        progress.ItemStatus("Сохранение RVT");
        var options = new SaveAsOptions { OverwriteExistingFile = true, Compact = settings.Compact, MaximumBackups = 1 };
        if (document.IsWorkshared)
        {
            options.SetWorksharingOptions(new WorksharingSaveAsOptions
            {
                SaveAsCentral = true,
                ClearTransmitted = isTransmitted,
                OpenWorksetsDefault = SimpleWorksetConfiguration.AllWorksets
            });
        }

        using (RevitApiContext.BeginFailureSuppressionScope())
        using (new RevitProgressBridge(document.Application, progress, "Сохранение RVT"))
        {
            document.SaveAs(filePath, options);
        }

        // После сохранения файлом хранилища все рабочие наборы заняты сохранившим — освобождаем,
        // чтобы в локальной копии не было чужих занятий.
        if (document.IsWorkshared)
            WorksharingUtils.RelinquishOwnership(document, new RelinquishOptions(true), new TransactWithCentralOptions());

        return removed;
    }

    /// <summary>Создаёт из закрытого файла хранилища локальную копию на месте <paramref name="filePath" />.</summary>
    public static void CreateLocal(string centralPath, string filePath)
    {
        if (File.Exists(filePath)) File.Delete(filePath);
        WorksharingUtils.CreateNewLocal(
            ModelPathUtils.ConvertUserVisiblePathToModelPath(centralPath),
            ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath));
    }

    private static string Prepare(Document document, ExportSettings settings)
    {
        var parts = new List<string>();

        using (RevitApiContext.BeginFailureSuppressionScope())
        {
            using var transaction = new Transaction(document, "ToadExporter: подготовка");
            transaction.Start();

            if (settings.DeleteRevitLinks)
                Count(parts, "связей RVT", Delete(document, Ids<RevitLinkType>(document)));

            if (settings.DeleteCad)
                Count(parts, "CAD", Delete(document, Ids<ImportInstance>(document).Concat(Ids<CADLinkType>(document)).ToList()));

            if (settings.DeletePointClouds)
                Count(parts, "облаков точек", Delete(document, Ids<PointCloudType>(document)));

#if REVIT2024_OR_GREATER
            if (settings.Purge) Count(parts, "неиспользуемого", Purge(document));
#endif

            transaction.Commit();
        }

        return parts.Count == 0 ? "" : "удалено — " + string.Join(", ", parts);
    }

    private static void Count(List<string> parts, string what, int count)
    {
        if (count > 0) parts.Add($"{what}: {count}");
    }

#if REVIT2024_OR_GREATER
    private static int Purge(Document document)
    {
        var total = 0;
        for (var pass = 0; pass < PurgePasses; pass++)
        {
            var unused = document.GetUnusedElements(new HashSet<ElementId>());
            if (unused.Count == 0) break;

            var deleted = Delete(document, unused.ToList());
            total += deleted;
            if (deleted == 0) break;
        }

        return total;
    }
#endif

    private static List<ElementId> Ids<T>(Document document) where T : Element =>
        new FilteredElementCollector(document).OfClass(typeof(T)).ToElementIds().ToList();

    /// <summary>Удаляет пачкой, а если Revit отказывает — по одному, пропуская неудаляемые.</summary>
    private static int Delete(Document document, IList<ElementId> ids)
    {
        if (ids.Count == 0) return 0;

        try
        {
            document.Delete(ids);
            return ids.Count;
        }
        catch (Exception exception) when (exception is Autodesk.Revit.Exceptions.ArgumentException
                                              or Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            var deleted = 0;
            foreach (var id in ids)
            {
                if (document.GetElement(id) is null) continue;
                try
                {
                    document.Delete(id);
                    deleted++;
                }
                catch (Exception single) when (single is Autodesk.Revit.Exceptions.ArgumentException
                                                   or Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    // Этот элемент Revit удалить не даёт — оставляем.
                }
            }

            return deleted;
        }
    }
}
