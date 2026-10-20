using Autodesk.Revit.DB.Electrical;

namespace CreateCover.Services;

public class GetFirstTrayService
{
    private Document _doc;
    
    public GetFirstTrayService(Document doc)
    {
        _doc = doc;
    }

    public Element? GetFirstTray()
    {
        var trays = new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_CableTray)
            .WhereElementIsNotElementType()
            .ToList();
        
        return trays.FirstOrDefault();
    }
}