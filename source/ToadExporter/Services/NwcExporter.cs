using System.IO;
using Autodesk.Revit.DB;
using ToadExporter.Core;

namespace ToadExporter.Services;

/// <summary>Экспорт одного 3D-вида в NWC через Navisworks Exporter для Revit.</summary>
public static class NwcExporter
{
    public static bool IsAvailable() => OptionalFunctionalityUtils.IsNavisworksExporterAvailable();

    public static StepResult Export(Document document, string folder, string name, ExportSettings settings, out string? note)
    {
        var viewName = settings.NwcViewName.Trim();
        var view = new FilteredElementCollector(document)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .FirstOrDefault(view => !view.IsTemplate && string.Equals(view.Name, viewName, StringComparison.CurrentCultureIgnoreCase));

        if (view is null)
        {
            note = $"вид «{viewName}» не найден, NWC пропущен";
            return StepResult.Skipped;
        }

        // Экспортер сам не всегда перезаписывает файл — удаляем старый; занятый файл даст понятную ошибку.
        var target = Path.Combine(folder, name + ".nwc");
        if (File.Exists(target)) File.Delete(target);

        document.Export(folder, name, Options(view, settings));
        note = null;
        return StepResult.Done;
    }

    private static NavisworksExportOptions Options(View3D view, ExportSettings settings) => new()
    {
        ExportScope = NavisworksExportScope.View,
        ViewId = view.Id,
        Coordinates = settings.NwcSharedCoordinates ? NavisworksCoordinates.Shared : NavisworksCoordinates.Internal,
        ConvertElementProperties = settings.NwcConvertElementProperties,
        Parameters = settings.NwcParameters switch
        {
            NwcParameters.None => NavisworksParameters.None,
            NwcParameters.Elements => NavisworksParameters.Elements,
            _ => NavisworksParameters.All
        },
        DivideFileIntoLevels = settings.NwcDivideIntoLevels,
        ExportLinks = settings.NwcExportLinks,
        ConvertLinkedCADFormats = settings.NwcConvertLinkedCad,
        ExportParts = settings.NwcExportParts,
        ExportRoomGeometry = settings.NwcRoomGeometry,
        ExportRoomAsAttribute = settings.NwcRoomAsAttribute,
        ConvertLights = settings.NwcConvertLights,
        ExportElementIds = settings.NwcExportElementIds,
        ExportUrls = settings.NwcExportUrls,
        FindMissingMaterials = settings.NwcFindMissingMaterials,
        FacetingFactor = settings.NwcFacetingFactor
    };
}
