using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using CreateSpaces.Models;
using CreateSpaces.ViewModels;
using Nice3point.Revit.Toolkit;
using ToadTools.UI.Progress;
using Wpf.Ui.Controls;

namespace CreateSpaces.Services;

public sealed class SpaceCreationService : ISpaceCreationService
{
    private readonly RevitLinkProvider _linkProvider;
    private readonly RevitRoomProvider _roomProvider;
    private Document _doc = RevitContext.ActiveDocument!;

    public SpaceCreationService(
        RevitLinkProvider linkProvider,
        RevitRoomProvider roomProvider)
    {
        _linkProvider = linkProvider;
        _roomProvider = roomProvider;
    }

    public SpaceCreationResult CreateSpaces(
        LinkDescriptor? linkDescriptor,
        IEnumerable<ParameterMappingModel> mappingModels,
        bool createSpaces)
    {
        var linkInstance = _linkProvider.GetByName(_doc, linkDescriptor?.Name);
        if (linkInstance == null)
            throw new InvalidOperationException("Link not found.");

        var linkDoc = linkInstance.GetLinkDocument();
        var transform = linkInstance.GetTransform();

        var rooms = _roomProvider.GetRoomsFromLink();

        int created = 0;
        int updated = 0;

        using var progress = ToadProgress.Show("Создание пространств",
            new ProgressOptions { Icon = SymbolRegular.Cube24 });
        using var tx = new Transaction(_doc, "Rooms → Spaces");
        tx.Start();

        if (rooms != null)
            foreach (var room in progress.Track(rooms, "Помещения → пространства"))
            {
                var location = GetLocation(room, transform);

                var existingSpace = FindSpaceAtPoint(_doc, location);

                if (existingSpace != null)
                {
                    CopyParameters(room, existingSpace, mappingModels);
                    updated++;
                    continue;
                }

                if (!createSpaces)
                    continue;

                var level = FindClosestLevel(_doc, location);
                var uv = new UV(location.X, location.Y);

                var newSpace = _doc.Create.NewSpace(level, uv);

                CopyParameters(room, newSpace, mappingModels);
                created++;
            }

        tx.Commit();

        return new SpaceCreationResult
        {
            Created = created,
            Updated = updated
        };
    }

    private static void CopyParameters(
        Room? room,
        Space? space,
        IEnumerable<ParameterMappingModel> mappingModels)
    {
        foreach (var model in mappingModels)
        {
            if (model.SelectedRoomParameter == null)
                continue;

            var source = GetParameter(room, model.SelectedRoomParameter);
            var target = GetParameter(space, model.SpaceParameter);

            if (source == null || target == null || target.IsReadOnly)
                continue;

            switch (model.SpaceParameter.StorageType)
            {
                case StorageType.String:
                    target.Set(source.AsString());
                    break;

                case StorageType.Double:
                    target.Set(source.AsDouble());
                    break;

                case StorageType.Integer:
                    target.Set(source.AsInteger());
                    break;
            }
        }
    }

    private static Parameter? GetParameter(
        Element? element,
        ParameterDescriptor descriptor)
    {
        if (descriptor.BuiltInParameter.HasValue)
        {
            var param = element?.get_Parameter(descriptor.BuiltInParameter.Value);
            if (param != null)
                return param;
        }

        return element?.LookupParameter(descriptor.Name);
    }

    private static XYZ GetLocation(Room? room, Transform transform)
    {
        var point = ((LocationPoint)room?.Location!)?.Point;
        return transform.OfPoint(point + (200 / 304.8) * XYZ.BasisZ);
    }

    private static Level FindClosestLevel(Document doc, XYZ point)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => Math.Abs(l.Elevation - point.Z))
            .First();
    }
    
    private static Space? FindSpaceAtPoint(Document doc, XYZ point)
    {
        var spaces = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_MEPSpaces)
            .WhereElementIsNotElementType()
            .Cast<Space>();

        foreach (var space in spaces)
        {
            if (space.IsPointInSpace(point))
                return space;
        }

        return null;
    }
}