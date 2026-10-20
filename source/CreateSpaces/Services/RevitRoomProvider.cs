using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using CreateSpaces.Models;
using Nice3point.Revit.Toolkit;

namespace CreateSpaces.Services;

public class RevitRoomProvider
{
    private readonly Document _doc = RevitContext.ActiveDocument!;
    private IReadOnlyList<Room?>?  _rooms;

    public void Initialize(LinkDescriptor link)
    {
        _rooms = Array.Empty<Room>();
        if (link == null || string.IsNullOrEmpty(link.Name)) return;

        var linkInstance = new FilteredElementCollector(_doc)
            .OfClass(typeof(RevitLinkInstance))
            .Cast<RevitLinkInstance>()
            .FirstOrDefault(x => x.GetLinkDocument()?.Title == link.Name);

        var linkedDoc = linkInstance?.GetLinkDocument();
        if (linkedDoc == null) return;

        // Неразмещённые и неокружённые помещения без точки — пространство для них не создать.
        _rooms = new FilteredElementCollector(linkedDoc)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Cast<Room>()
            .Where(room => room.Location is LocationPoint && room.Area > 0)
            .ToList<Room?>();
    }
    
    public IReadOnlyList<Room?>? GetRoomsFromLink()
    {
        return _rooms;
    }

    public bool DetectRooms()
    {
        if (_rooms!.Count == 0)
            return false;
        
        return true;
    }
}