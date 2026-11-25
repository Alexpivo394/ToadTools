using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace Families.Revit;

/// <summary>
///     Из какой итерации загружено семейство проекта — вторая запись Extensible Storage рядом с меткой ядра (<c>FamilyStamps</c>): её
///     схему менять нельзя. Хеш итерацию не определяет — у только что созданной итерации он тот же, что у «Основной». Нет записи или
///     <see cref="Guid.Empty" /> — «Основная».
/// </summary>
public static class IterationStamps
{
    /// <summary>Постоянный: проекты хранят данные под ним.</summary>
    public static readonly Guid SchemaId = new("4f2c9a71-8d3e-4b6a-a1c5-7e0d93b2f816");

    private const string IterationField = "IterationId";

    /// <summary>Пишет итерацию; транзакция открыта вызывающим.</summary>
    public static void Write(Family family, Guid? iterationId)
    {
        var schema = Schema();
        var entity = new Entity(schema);
        entity.Set(schema.GetField(IterationField), iterationId ?? Guid.Empty);
        family.SetEntity(entity);
    }

    /// <summary>Итерация семейства; null — «Основная» или записи нет.</summary>
    public static Guid? Read(Family family)
    {
        if (Autodesk.Revit.DB.ExtensibleStorage.Schema.Lookup(SchemaId) is not { } schema) return null;
        var entity = family.GetEntity(schema);
        if (entity is null || !entity.IsValid()) return null;
        var id = entity.Get<Guid>(schema.GetField(IterationField));
        return id == Guid.Empty ? null : id;
    }

    private static Schema Schema()
    {
        if (Autodesk.Revit.DB.ExtensibleStorage.Schema.Lookup(SchemaId) is { } existing) return existing;
        var builder = new SchemaBuilder(SchemaId);
        builder.SetSchemaName("ToadFlowFamilyIteration");
        builder.SetDocumentation("Library iteration this family was loaded from");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.SetVendorId("Alexpivo394");
        builder.AddSimpleField(IterationField, typeof(Guid));
        return builder.Finish();
    }
}
