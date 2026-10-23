using Alexpivo394.Revit.Export.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ToadExporter.Core;

/// <summary>
///     Все настройки окна. Сохраняются между запусками. Значения NWC по умолчанию — как у
///     <c>new NavisworksExportOptions()</c>, кроме выгрузки одного вида: она задаётся именем вида.
/// </summary>
public sealed partial class ExportSettings : ObservableObject
{
    // Источник
    [ObservableProperty] private SourceKind _sourceKind = SourceKind.Server;
    [ObservableProperty] private string _server = "";
    [ObservableProperty] private string _sourceFolder = "";
    [ObservableProperty] private string _outputFolder = "";

    // Открытие
    [ObservableProperty] private bool _skipLinks = true;
    [ObservableProperty] private bool _audit;
    [ObservableProperty] private string _worksetKeyword = "";

    // RVT — обработка перед сохранением
    [ObservableProperty] private bool _deleteRevitLinks = true;
    [ObservableProperty] private bool _deleteCad = true;
    [ObservableProperty] private bool _deletePointClouds;
    [ObservableProperty] private bool _purge = true;
    [ObservableProperty] private bool _compact = true;

    // NWC — основное
    [ObservableProperty] private string _nwcViewName = "Navisworks";
    [ObservableProperty] private bool _nwcSharedCoordinates = true;
    [ObservableProperty] private bool _nwcConvertElementProperties;
    [ObservableProperty] private NwcParameters _nwcParameters = NwcParameters.All;
    [ObservableProperty] private bool _nwcDivideIntoLevels = true;

    // NWC — что экспортировать
    [ObservableProperty] private bool _nwcExportLinks;
    [ObservableProperty] private bool _nwcConvertLinkedCad = true;
    [ObservableProperty] private bool _nwcExportParts;
    [ObservableProperty] private bool _nwcRoomGeometry = true;
    [ObservableProperty] private bool _nwcRoomAsAttribute = true;
    [ObservableProperty] private bool _nwcConvertLights;

    // NWC — дополнительно
    [ObservableProperty] private bool _nwcExportElementIds = true;
    [ObservableProperty] private bool _nwcExportUrls = true;
    [ObservableProperty] private bool _nwcFindMissingMaterials = true;
    [ObservableProperty] private double _nwcFacetingFactor = 1.0;

    // Окно
    [ObservableProperty] private bool _rvtExpanded = true;
    [ObservableProperty] private bool _nwcMainExpanded = true;
    [ObservableProperty] private bool _nwcContentExpanded;
    [ObservableProperty] private bool _nwcExtraExpanded;
    [ObservableProperty] private bool _darkTheme = true;

    /// <summary>
    ///     Настройки выгрузки для пакета ToadExport. Ручная выгрузка, как и раньше, открывает модель более старой версии
    ///     с обновлением — так же поступает сам Revit.
    /// </summary>
    public ExportOptions ToOptions() => new()
    {
        SkipLinks = SkipLinks,
        Audit = Audit,
        WorksetKeyword = WorksetKeyword,
        AllowUpgrade = true,
        DeleteRevitLinks = DeleteRevitLinks,
        DeleteCad = DeleteCad,
        DeletePointClouds = DeletePointClouds,
        Purge = Purge,
        Compact = Compact,
        NwcViewName = NwcViewName,
        NwcSharedCoordinates = NwcSharedCoordinates,
        NwcConvertElementProperties = NwcConvertElementProperties,
        NwcParameters = NwcParameters,
        NwcDivideIntoLevels = NwcDivideIntoLevels,
        NwcExportLinks = NwcExportLinks,
        NwcConvertLinkedCad = NwcConvertLinkedCad,
        NwcExportParts = NwcExportParts,
        NwcRoomGeometry = NwcRoomGeometry,
        NwcRoomAsAttribute = NwcRoomAsAttribute,
        NwcConvertLights = NwcConvertLights,
        NwcExportElementIds = NwcExportElementIds,
        NwcExportUrls = NwcExportUrls,
        NwcFindMissingMaterials = NwcFindMissingMaterials,
        NwcFacetingFactor = NwcFacetingFactor
    };
}

public enum SourceKind
{
    Server,
    Folder
}
