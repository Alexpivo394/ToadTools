using Autodesk.Revit.DB;

namespace ChangeSharedFamilies.Services;

internal class FamilyTypeParameterValueService
{
    private readonly Document _familyDocument;

    public FamilyTypeParameterValueService(Document familyDocument)
    {
        _familyDocument = familyDocument;
    }

    public List<FamilyTypeValueSnapshot> CaptureValuesPointingTo(Family oldNestedFamily)
    {
        var result = new List<FamilyTypeValueSnapshot>();
        var familyManager = _familyDocument.FamilyManager;

        foreach (FamilyParameter familyParameter in familyManager.Parameters)
        {
            if (familyParameter == null)
                continue;

            if (familyParameter.StorageType != StorageType.ElementId)
                continue;

            foreach (FamilyType familyType in familyManager.Types)
            {
                ElementId valueId;

                try
                {
                    valueId = familyType.AsElementId(familyParameter);
                }
                catch
                {
                    continue;
                }

                if (valueId == null || valueId == ElementId.InvalidElementId)
                    continue;

                var symbol = _familyDocument.GetElement(valueId) as FamilySymbol;

                if (symbol == null)
                    continue;

                if (!symbol.Family.Id.Equals(oldNestedFamily.Id))
                    continue;

                result.Add(new FamilyTypeValueSnapshot
                {
                    FamilyTypeName = familyType.Name,
                    FamilyParameter = familyParameter,
                    OldSymbolName = symbol.Name
                });
            }
        }

        return result;
    }

    public void Remap(
        List<FamilyTypeValueSnapshot> snapshots,
        Dictionary<string, FamilySymbol> tempSymbolsByName)
    {
        if (snapshots.Count == 0)
            return;

        var familyManager = _familyDocument.FamilyManager;
        var oldCurrentType = familyManager.CurrentType;

        try
        {
            foreach (var snapshot in snapshots)
            {
                var familyType = FindFamilyTypeByName(snapshot.FamilyTypeName);

                if (familyType == null)
                    continue;

                var replacementSymbol = NestedFamilySymbols.FindReplacementSymbol(
                    tempSymbolsByName,
                    snapshot.OldSymbolName,
                    "temporary family");

                familyManager.CurrentType = familyType;

                familyManager.Set(
                    snapshot.FamilyParameter,
                    replacementSymbol.Id);
            }
        }
        finally
        {
            if (oldCurrentType != null)
                familyManager.CurrentType = oldCurrentType;
        }
    }

    private FamilyType? FindFamilyTypeByName(string? familyTypeName)
    {
        var familyManager = _familyDocument.FamilyManager;

        foreach (FamilyType? familyType in familyManager.Types)
        {
            if (string.Equals(
                    familyType?.Name,
                    familyTypeName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return familyType;
            }
        }

        return null;
    }
}
