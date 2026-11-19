using ClassifierCode.Engine.Profile;
using ClassifierCode.Models;

namespace ClassifierCode.Services;

/// <summary>
///     Запись полей позиции в параметры профиля: в экземпляр, а если у экземпляра таких параметров нет — в тип
///     (только когда все экземпляры типа получили один и тот же код).
/// </summary>
public sealed class ClassifierParameterWriter(Document doc, bool keepExisting, ClassifierReport report, IReadOnlyList<OutputField> outputs)
{
    private readonly Dictionary<ElementId, List<Position>> _typeRequests = new();
    private readonly List<OutputField> _outputs = outputs.Where(o => !string.IsNullOrWhiteSpace(o.Parameter)).ToList();
    private readonly OutputField _code = outputs.First(o => o.Field == "code");

    private enum WriteResult
    {
        Written,
        Kept,
        Failed
    }

    public void Write(Element element, Position position)
    {
        if (HasAnyOutput(element))
        {
            if (IsBusy(element.Id))
            {
                report.Busy++;
                return;
            }

            switch (SetAll(element, position))
            {
                case WriteResult.Written:
                    report.WrittenInstances++;
                    report.CountCode(position, 1);
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
        if (type == null || !HasAnyOutput(type))
        {
            report.AddMissingParameters(element);
            return;
        }

        if (!_typeRequests.TryGetValue(typeId, out var requests))
        {
            requests = [];
            _typeRequests[typeId] = requests;
        }

        requests.Add(position);
    }

    /// <summary>Записывает накопленные типовые параметры. Вызывать после обработки всех экземпляров.</summary>
    public void FlushTypes()
    {
        foreach (var pair in _typeRequests)
        {
            var type = doc.GetElement(pair.Key);
            var codes = pair.Value.Select(p => p.Code).Distinct().ToList();
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

            var position = pair.Value[0];
            switch (SetAll(type, position))
            {
                case WriteResult.Written:
                    report.WrittenTypes++;
                    report.CountCode(position, pair.Value.Count);
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

    public static Parameter? Find(Element element, OutputField output) => OutputParameters.Find(element, output);

    private bool HasAnyOutput(Element element) => _outputs.Any(o => Find(element, o) != null);

    private WriteResult SetAll(Element element, Position position)
    {
        var code = Find(element, _code);
        if (keepExisting && code != null && !string.IsNullOrEmpty(code.AsString()))
            return WriteResult.Kept;

        var ok = true;
        foreach (var output in _outputs)
        {
            // Колонки, которой у позиции нет, не пишем: пустая строка затёрла бы значение, заполненное руками.
            if (Value(position, output.Field) is { } value) ok &= Set(Find(element, output), value);
        }
        return ok ? WriteResult.Written : WriteResult.Failed;
    }

    private static string? Value(Position position, string field) => field switch
    {
        "code" => position.Code,
        "description" => position.Description,
        _ => position.Extra.TryGetValue(field, out var value) ? value : null
    };

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
