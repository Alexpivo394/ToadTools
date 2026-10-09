using Autodesk.Revit.DB;

namespace ChangeSharedFamilies.Services;

internal static class NestedFamilySymbols
{
    public static Dictionary<string, FamilySymbol> GetFamilySymbolsByName(
        Document document,
        Family family)
    {
        var result = new Dictionary<string, FamilySymbol>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var symbolId in family.GetFamilySymbolIds())
        {
            var symbol = document.GetElement(symbolId) as FamilySymbol;

            if (symbol == null)
                continue;

            result[symbol.Name] = symbol;
        }

        return result;
    }

    public static void Activate(
        Document document,
        IEnumerable<FamilySymbol> symbols)
    {
        var needRegenerate = false;

        foreach (var symbol in symbols)
        {
            if (symbol.IsActive)
                continue;

            symbol.Activate();
            needRegenerate = true;
        }

        if (needRegenerate)
            document.Regenerate();
    }

    public static FamilySymbol FindReplacementSymbol(
        Dictionary<string, FamilySymbol> tempSymbolsByName,
        string? oldSymbolName,
        string tempFamilyName)
    {
        if (oldSymbolName != null && tempSymbolsByName.TryGetValue(oldSymbolName, out var replacementSymbol))
            return replacementSymbol;

        if (tempSymbolsByName.Count == 1)
            return tempSymbolsByName.Values.First();

        throw new InvalidOperationException(
            $"Не найден тип '{oldSymbolName}' во временном семействе '{tempFamilyName}'.");
    }
}
