using ClassifierCode.Models;

namespace ClassifierCode.Services;

/// <summary>
///     Запись кода и описания в экземпляр, а если параметр типовой — в тип
///     (только когда все экземпляры типа получили один и тот же код).
/// </summary>
public sealed class ClassifierParameterWriter(Document doc, bool keepExisting, ClassifierReport report)
{
    private readonly Dictionary<ElementId, List<ClassifierItem>> _typeRequests = new();

    private enum WriteResult
    {
        Written,
        Kept,
        Failed
    }

    public void Write(Element element, ClassifierItem item)
    {
        var code = element.LookupParameter(ClassifierParameters.Code);
        var description = element.LookupParameter(ClassifierParameters.Description);
        if (code != null || description != null)
        {
            if (IsBusy(element.Id))
            {
                report.Busy++;
                return;
            }

            switch (SetPair(code, description, item))
            {
                case WriteResult.Written:
                    report.WrittenInstances++;
                    report.CountCode(item, 1);
                    break;
                case WriteResult.Kept:
                    report.KeptExisting++;
                    break;
                case WriteResult.Failed:
                    report.AddFailed(element);
                    break;
            }

            return;
        }

        var typeId = element.GetTypeId();
        var type = doc.GetElement(typeId);
        if (type == null || (type.LookupParameter(ClassifierParameters.Code) == null &&
                             type.LookupParameter(ClassifierParameters.Description) == null))
        {
            report.AddMissingParameters(element);
            return;
        }

        if (!_typeRequests.TryGetValue(typeId, out var requests))
        {
            requests = [];
            _typeRequests[typeId] = requests;
        }

        requests.Add(item);
    }

    /// <summary>Записывает накопленные типовые параметры. Вызывать после обработки всех экземпляров.</summary>
    public void FlushTypes()
    {
        foreach (var pair in _typeRequests)
        {
            var type = doc.GetElement(pair.Key);
            var codes = pair.Value.Select(i => i.Code).Distinct().ToList();
            if (codes.Count > 1)
            {
                report.TypeConflicts.Add(ClassifierReport.Describe(type) + " : " + type.Name + " → " + string.Join(", ", codes));
                continue;
            }

            if (IsBusy(type.Id))
            {
                report.Busy++;
                continue;
            }

            var item = pair.Value[0];
            var code = type.LookupParameter(ClassifierParameters.Code);
            var description = type.LookupParameter(ClassifierParameters.Description);
            switch (SetPair(code, description, item))
            {
                case WriteResult.Written:
                    report.WrittenTypes++;
                    report.CountCode(item, pair.Value.Count);
                    break;
                case WriteResult.Kept:
                    report.KeptExisting++;
                    break;
                case WriteResult.Failed:
                    report.AddFailed(type);
                    break;
            }
        }
    }

    private WriteResult SetPair(Parameter? code, Parameter? description, ClassifierItem item)
    {
        if (keepExisting && code != null && !string.IsNullOrEmpty(code.AsString()))
            return WriteResult.Kept;

        var ok = Set(code, item.Code);
        ok &= Set(description, item.Description);
        return ok ? WriteResult.Written : WriteResult.Failed;
    }

    private static bool Set(Parameter? parameter, string value)
    {
        if (parameter == null)
            return true;
        if (parameter.IsReadOnly || parameter.StorageType != StorageType.String)
            return false;
        if (parameter.AsString() == value)
            return true;

        try
        {
            return parameter.Set(value);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return false;
        }
    }

    private bool IsBusy(ElementId id)
    {
        return doc.IsWorkshared &&
               WorksharingUtils.GetCheckoutStatus(doc, id) == CheckoutStatus.OwnedByOtherUser;
    }
}
