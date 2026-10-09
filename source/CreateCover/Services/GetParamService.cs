using Autodesk.Revit.DB.Structure;
using CreateCover.Models;

namespace CreateCover.Services;

public class GetParamService
{
    private Document _doc;

    public GetParamService(Document doc)
    {
        _doc = doc;
    }

    public IReadOnlyList<ParameterDescriptor> GetFromElement(Element element)
    {
        var dict = new Dictionary<string, ParameterDescriptor>();

        Collect(element.Parameters, true, dict);

        if (element is FamilyInstance fi && fi.Symbol != null)
            Collect(fi.Symbol.Parameters, false, dict);

        return dict.Values.OrderBy(x => x.Name).ToList();
    }

    public IReadOnlyList<ParameterDescriptor> GetFromSymbol(FamilySymbol? symbol)
    {
        var dict = new Dictionary<string, ParameterDescriptor>();

        if (symbol != null)
        {
            foreach (Parameter p in symbol.Parameters)
            {
                if (p?.Definition == null || p.IsReadOnly)
                    continue;

                dict[p.Definition.Name] = Create(p, false);
            }

            var instanceParams = GetInstanceParamsFromSymbol(symbol);
            foreach (var p in instanceParams)
            {
                if (p.Name != null && !dict.ContainsKey(p.Name))
                    dict[p.Name] = p;
            }
        }

        return dict.Values
            .OrderBy(p => p.Name)
            .ToList();
    }
    
    public IReadOnlyList<ParameterDescriptor> GetInstanceParamsFromSymbol(FamilySymbol? symbol)
    {
        var result = new List<ParameterDescriptor>();

        using (var tx = new Transaction(_doc, "Temp instance for params"))
        {
            tx.Start();

            if (symbol != null && !symbol.IsActive)
                symbol.Activate();

            var inst = _doc.Create.NewFamilyInstance(
                XYZ.Zero,
                symbol,
                StructuralType.NonStructural);

            foreach (Parameter p in inst.Parameters)
            {
                if (p?.Definition == null || p.IsReadOnly)
                    continue;

                result.Add(new ParameterDescriptor
                {
                    Name = p.Definition.Name,
                    Id = p.Id,
                    StorageType = p.StorageType,
                    IsInstance = true,
                    IsShared = p.IsShared
                });
            }

            tx.RollBack();
        }

        return result
            .OrderBy(p => p.Name)
            .ToList();
    }


    private void Collect(
        ParameterSet set,
        bool isInstance,
        Dictionary<string, ParameterDescriptor> dict)
    {
        foreach (Parameter p in set)
        {
            if (p?.Definition == null || p.IsReadOnly)
                continue;

            var name = p.Definition.Name;
            if (dict.ContainsKey(name))
                continue;

            dict[name] = Create(p, isInstance);
        }
    }

    private ParameterDescriptor Create(Parameter p, bool isInstance)
    {
        return new ParameterDescriptor
        {
            Name = p.Definition.Name,
            Id = p.Id,
            StorageType = p.StorageType,
            IsInstance = isInstance,
            IsShared = p.IsShared
        };
    }
}