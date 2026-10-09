using Autodesk.Revit.DB;

namespace ChangeSharedFamilies.Services;

internal class NestedFamilyReplacementService
{
    private readonly Document _familyDocument;
    private readonly FamilyTypeParameterValueService _familyTypeValues;
    private readonly InstanceParameterBindingService _parameterBindings;

    public NestedFamilyReplacementService(Document familyDocument)
    {
        _familyDocument = familyDocument;
        _familyTypeValues = new FamilyTypeParameterValueService(_familyDocument);
        _parameterBindings = new InstanceParameterBindingService(_familyDocument);
    }

    public void ReplaceOldNestedFamilyWithTempFamily(
        Family oldNestedFamily,
        Family tempLoadedFamily,
        string finalFamilyName)
    {
        var oldFamilyId = oldNestedFamily.Id;
        var tempSymbolsByName = NestedFamilySymbols.GetFamilySymbolsByName(
            _familyDocument,
            tempLoadedFamily);

        if (tempSymbolsByName.Count == 0)
        {
            throw new InvalidOperationException(
                $"Во временном семействе '{tempLoadedFamily.Name}' нет ни одного типа.");
        }

        var nestedInstances = GetInstancesOfFamily(oldFamilyId);
        var bindingSnapshots = _parameterBindings.Capture(nestedInstances);
        var familyTypeValueSnapshots = _familyTypeValues.CaptureValuesPointingTo(oldNestedFamily);

        using (var transaction = new Transaction(
                   _familyDocument,
                   $"Replace nested family {finalFamilyName}"))
        {
            transaction.Start();

            NestedFamilySymbols.Activate(_familyDocument, tempSymbolsByName.Values);

            _familyTypeValues.Remap(
                familyTypeValueSnapshots,
                tempSymbolsByName);

            _familyDocument.Regenerate();

            ReplaceInstancesNotControlledByFamilyTypeLabel(
                bindingSnapshots,
                familyTypeValueSnapshots,
                tempSymbolsByName,
                tempLoadedFamily.Name);

            ReplaceInstancesWithoutCapturedBindings(
                nestedInstances,
                bindingSnapshots,
                tempSymbolsByName,
                tempLoadedFamily.Name);

            _familyDocument.Regenerate();

            _parameterBindings.Restore(bindingSnapshots);

            _familyDocument.Regenerate();

            GuardNoOldInstancesRemain(oldFamilyId, finalFamilyName);

            _familyDocument.Delete(oldFamilyId);
            tempLoadedFamily.Name = finalFamilyName;

            transaction.Commit();
        }
    }

    private List<FamilyInstance> GetInstancesOfFamily(ElementId familyId)
    {
        return new FilteredElementCollector(_familyDocument)
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .Where(x => x.Symbol != null)
            .Where(x => x.Symbol.Family.Id.Equals(familyId))
            .ToList();
    }

    private void ReplaceInstancesNotControlledByFamilyTypeLabel(
        List<NestedInstanceBindingSnapshot> bindingSnapshots,
        List<FamilyTypeValueSnapshot> familyTypeValueSnapshots,
        Dictionary<string, FamilySymbol> tempSymbolsByName,
        string tempFamilyName)
    {
        foreach (var snapshot in bindingSnapshots)
        {
            if (_parameterBindings.IsControlledByFamilyTypeLabel(
                    snapshot,
                    familyTypeValueSnapshots))
            {
                continue;
            }

            var instance = _familyDocument.GetElement(snapshot.InstanceId) as FamilyInstance;

            if (instance == null)
                continue;

            var replacementSymbol = NestedFamilySymbols.FindReplacementSymbol(
                tempSymbolsByName,
                snapshot.OldSymbolName,
                tempFamilyName);

            instance.ChangeTypeId(replacementSymbol.Id);
        }
    }

    private static void ReplaceInstancesWithoutCapturedBindings(
        List<FamilyInstance> nestedInstances,
        List<NestedInstanceBindingSnapshot> bindingSnapshots,
        Dictionary<string, FamilySymbol> tempSymbolsByName,
        string tempFamilyName)
    {
        var processedInstanceIds = new HashSet<ElementId>(
            bindingSnapshots.Select(x => x.InstanceId));

        foreach (var instance in nestedInstances)
        {
            if (processedInstanceIds.Contains(instance.Id))
                continue;

            var replacementSymbol = NestedFamilySymbols.FindReplacementSymbol(
                tempSymbolsByName,
                instance.Symbol.Name,
                tempFamilyName);

            instance.ChangeTypeId(replacementSymbol.Id);
        }
    }

    private void GuardNoOldInstancesRemain(
        ElementId oldFamilyId,
        string finalFamilyName)
    {
        var remainingOldInstances = GetInstancesOfFamily(oldFamilyId);

        if (remainingOldInstances.Count > 0)
        {
            throw new InvalidOperationException(
                $"После замены остались экземпляры старого shared-семейства '{finalFamilyName}': {remainingOldInstances.Count}.");
        }
    }
}
