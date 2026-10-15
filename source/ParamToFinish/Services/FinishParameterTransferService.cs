using Autodesk.Revit.DB;
using Nice3point.Revit.Toolkit;
using ParamToFinish.Models;
using ToadTools.UI.Progress;
using Wpf.Ui.Controls;

namespace ParamToFinish.Services;

public class FinishParameterTransferService : IFinishParameterTransferService
{
    private readonly RevitParameterService _parameterService;
    private readonly WallGeometryService _geometryService;
    private readonly WallTouchService _touchService;
    private const double SpatialSearchMargin = 1.0;
    private const double SpatialCellSize = 30.0;

    public FinishParameterTransferService()
    {
        _parameterService = new RevitParameterService();
        _geometryService = new WallGeometryService();
        _touchService = new WallTouchService(_geometryService);
    }

    public void Transfer(ParameterDescriptor? selectedWallParameter, ParameterDescriptor? selectedFinishParameter,
        bool allModel, string filter)
    {
        var document = RevitContext.ActiveDocument!;

        if (string.IsNullOrWhiteSpace(selectedWallParameter?.Name))
            throw new ArgumentException(nameof(selectedWallParameter));

        if (string.IsNullOrWhiteSpace(selectedFinishParameter?.Name))
            throw new ArgumentException(nameof(selectedFinishParameter));

        var wallParameterName = selectedWallParameter?.Name!;
        var finishParameterName = selectedFinishParameter?.Name!;
        List<Wall> walls;

        if (allModel)
        {
            walls = new FilteredElementCollector(document)
                .OfClass(typeof(Wall))
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .ToList();
        }
        else
        {
            var activeView = document.ActiveView;

            if (activeView == null || activeView.IsTemplate)
                return;

            walls = new FilteredElementCollector(document, activeView.Id)
                .OfClass(typeof(Wall))
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .ToList();
        }

        using var progress = ToadProgress.Show("Основание отделки",
            new ProgressOptions { Icon = SymbolRegular.Wallpaper24 });

        var finishWalls = new List<WallInfo>();
        var mainWalls = new List<WallInfo>();

        foreach (var wall in progress.Track(walls, "Анализ стен"))
        {
            var sourceValue = _parameterService.GetParameterValue(wall, wallParameterName);

            var wallInfo = CreateWallInfo(wall, sourceValue);

            var isFinish =
                !string.IsNullOrWhiteSpace(sourceValue) &&
                sourceValue?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

            if (isFinish)
                finishWalls.Add(wallInfo);
            else
                mainWalls.Add(wallInfo);
        }

        if (finishWalls.Count == 0 || mainWalls.Count == 0) return;

        var spatialIndex = new WallSpatialIndex(mainWalls, SpatialCellSize);

        var updates = new List<ParameterUpdate>();

        foreach (var finishWall in progress.Track(finishWalls, "Поиск основания для отделки"))
        {
            var candidates = finishWall.Box != null ? spatialIndex.Query(finishWall.Box) : mainWalls;

            var mainWall = _touchService.FindMainWall(finishWall, candidates);

            if (mainWall == null) continue;

            if (string.IsNullOrWhiteSpace(mainWall.SourceValue)) continue;

            if (mainWall.SourceValue != null)
                updates.Add(new ParameterUpdate(finishWall.Wall, finishParameterName, mainWall.SourceValue));
        }

        if (updates.Count == 0) return;

        using var transaction = new Transaction(document, "Transfer finish parameters");

        transaction.Start();

        foreach (var update in progress.Track(updates, "Запись параметров"))
        {
            _parameterService.SetParameterValue(update.Element, update.ParameterName, update.Value);
        }

        transaction.Commit();
    }

    private WallInfo CreateWallInfo(Wall wall, string? sourceValue)
    {
        Curve? centerCurve = null;

        if (_geometryService.TryGetWallLocationCurve(wall, out var curve))
            centerCurve = curve;

        var box = Box3D.FromWall(wall, SpatialSearchMargin, centerCurve);

        return new WallInfo(wall, sourceValue, centerCurve, box);
    }
}
