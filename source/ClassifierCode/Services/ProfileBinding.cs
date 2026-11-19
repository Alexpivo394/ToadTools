using Autodesk.Revit.DB.ExtensibleStorage;
using ClassifierCode.Engine.Library;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Services;

/// <summary>
///     Профиль и режим, которыми заполнялась модель, — в Extensible Storage на отдельном DataStorage.
///     Сам профиль в модели не хранится: только ссылка, чтобы при следующем запуске выбрать его же.
/// </summary>
public static class ProfileBinding
{
    private static readonly Guid SchemaGuid = new("3b0f6d2a-8c41-4f5e-b7a9-1d2e3c4f5a60");
    private const string SchemaName = "ToadClassifierProfile";

    private static Schema GetSchema()
    {
        var existing = Schema.Lookup(SchemaGuid);
        if (existing != null) return existing;

        var builder = new SchemaBuilder(SchemaGuid);
        builder.SetSchemaName(SchemaName);
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField("ProfileId", typeof(string));
        builder.AddSimpleField("ProfileName", typeof(string));
        builder.AddSimpleField("ProfileVersion", typeof(string));
        builder.AddSimpleField("Mode", typeof(string));
        return builder.Finish();
    }

    public static ModelBinding? Read(Document doc)
    {
        var schema = Schema.Lookup(SchemaGuid);
        if (schema == null) return null;

        var storage = FindStorage(doc, schema);
        var entity = storage?.GetEntity(schema);
        if (entity == null || !entity.IsValid()) return null;
        if (!Guid.TryParse(entity.Get<string>("ProfileId"), out var id)) return null;
        return new ModelBinding(id, entity.Get<string>("ProfileName") ?? string.Empty, entity.Get<string>("ProfileVersion") ?? string.Empty,
            entity.Get<string>("Mode") ?? string.Empty);
    }

    /// <summary>
    ///     Записать привязку внутри открытой транзакции. Пишет, только если она изменилась: в совместной модели правка
    ///     занятого или устаревшего элемента откатила бы при фиксации всё заполнение. null — записано или не нужно,
    ///     иначе — заметка для отчёта, почему не записано.
    /// </summary>
    public static string? TryWrite(Document doc, ClassifierProfile profile, string mode)
    {
        var current = Read(doc);
        if (current != null && current.Id == profile.Id && current.Name == profile.Name && current.Version == profile.Version && current.Mode == mode)
            return null;

        var schema = GetSchema();
        var existing = FindStorage(doc, schema);
        if (existing != null && doc.IsWorkshared &&
            (WorksharingUtils.GetCheckoutStatus(doc, existing.Id) == CheckoutStatus.OwnedByOtherUser ||
             WorksharingUtils.GetModelUpdatesStatus(doc, existing.Id) is ModelUpdatesStatus.UpdatedInCentral or ModelUpdatesStatus.DeletedInCentral))
            return "Профиль и раздел не запомнены в модели: данные заняты другим пользователем или устарели — синхронизируйтесь.";

        var storage = existing ?? DataStorage.Create(doc);
        var entity = new Entity(schema);
        entity.Set("ProfileId", profile.Id.ToString());
        entity.Set("ProfileName", profile.Name);
        entity.Set("ProfileVersion", profile.Version);
        entity.Set("Mode", mode);
        storage.SetEntity(entity);
        return null;
    }

    private static DataStorage? FindStorage(Document doc, Schema schema) =>
        new FilteredElementCollector(doc).OfClass(typeof(DataStorage)).Cast<DataStorage>()
            .FirstOrDefault(s => s.GetEntitySchemaGuids().Contains(schema.GUID));
}
