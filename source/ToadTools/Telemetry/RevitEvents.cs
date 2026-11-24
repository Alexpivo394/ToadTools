using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using ToadTools.UI.Services;

namespace ToadTools.Telemetry;

/// <summary>
///     Пары событий Revit → <see cref="OperationTimer" />. Обработчики работают только с памятью: путь документа, начало и конец
///     операции. Открытия и загрузки связей идут в потоке Revit строго вложенно, поэтому пара узнаёт друг друга по стопке, а не
///     по пути: путь в «открывается» и «открыт» у локальной копии разный.
/// </summary>
internal sealed class RevitEvents(OperationTimer timer)
{
    private readonly Stack<OpenToken> _opens = new();
    private readonly Stack<object> _links = new();

    private sealed class OpenToken(bool family)
    {
        public bool Family { get; } = family;
    }

    public void Subscribe(ControlledApplication application)
    {
        application.DocumentOpening += OnOpening;
        application.DocumentOpened += OnOpened;
        application.LinkedResourceOpening += OnLinkOpening;
        application.LinkedResourceOpened += OnLinkOpened;
        application.DocumentSynchronizingWithCentral += OnSyncing;
        application.DocumentSynchronizedWithCentral += OnSynced;
        application.DocumentSaving += OnSaving;
        application.DocumentSaved += OnSaved;
        application.DocumentSavingAs += OnSavingAs;
        application.DocumentSavedAs += OnSavedAs;
#if REVIT2021_OR_GREATER
        application.DocumentReloadingLatest += OnReloading;
        application.DocumentReloadedLatest += OnReloaded;
#endif
    }

    public void Unsubscribe(ControlledApplication application)
    {
        application.DocumentOpening -= OnOpening;
        application.DocumentOpened -= OnOpened;
        application.LinkedResourceOpening -= OnLinkOpening;
        application.LinkedResourceOpened -= OnLinkOpened;
        application.DocumentSynchronizingWithCentral -= OnSyncing;
        application.DocumentSynchronizedWithCentral -= OnSynced;
        application.DocumentSaving -= OnSaving;
        application.DocumentSaved -= OnSaved;
        application.DocumentSavingAs -= OnSavingAs;
        application.DocumentSavedAs -= OnSavedAs;
#if REVIT2021_OR_GREATER
        application.DocumentReloadingLatest -= OnReloading;
        application.DocumentReloadedLatest -= OnReloaded;
#endif
    }

    private void OnOpening(object? sender, DocumentOpeningEventArgs e) => Safe("открытие", () =>
    {
        var token = new OpenToken(e.DocumentType == DocumentType.Family);
        _opens.Push(token);
        timer.Begin(token, OperationKind.Open, e.PathName);
    });

    private void OnOpened(object? sender, DocumentOpenedEventArgs e) => Safe("открытие", () =>
    {
        if (_opens.Count == 0) return;
        var token = _opens.Pop();
        var document = e.Document;
        // Семейства не считаем; документ связи, открытый как отдельный документ, считается своим событием связи.
        if (token.Family || document is { IsFamilyDocument: true } || document is { IsLinked: true })
        {
            timer.Discard(token);
            return;
        }

        timer.End(token, Outcome(e.Status), document is null ? null : Model(document), localFile: document?.PathName);
    });

    private void OnLinkOpening(object? sender, LinkedResourceOpeningEventArgs e) => Safe("связь", () =>
    {
        var token = new object();
        _links.Push(token);
        if (IsRevitLink(e.ResourceType)) timer.Begin(token, OperationKind.Link, e.LinkedResourcePathName);
    });

    private void OnLinkOpened(object? sender, LinkedResourceOpenedEventArgs e) => Safe("связь", () =>
    {
        if (_links.Count == 0) return;
        timer.End(_links.Pop(), Outcome(e.Status));
    });

    private void OnSyncing(object? sender, DocumentSynchronizingWithCentralEventArgs e) =>
        Safe("синхронизация", () => timer.Begin(Key("sync", e.Document), OperationKind.Sync, e.Document.PathName));

    private void OnSynced(object? sender, DocumentSynchronizedWithCentralEventArgs e) =>
        Safe("синхронизация", () => timer.End(Key("sync", e.Document), Outcome(e.Status), Model(e.Document), localFile: e.Document.PathName));

    private void OnSaving(object? sender, DocumentSavingEventArgs e) => Safe("сохранение", () =>
    {
        if (!e.Document.IsFamilyDocument) timer.Begin(Key("save", e.Document), OperationKind.Save, e.Document.PathName);
    });

    private void OnSaved(object? sender, DocumentSavedEventArgs e) =>
        Safe("сохранение", () => timer.End(Key("save", e.Document), Outcome(e.Status), Model(e.Document), localFile: e.Document.PathName));

    private void OnSavingAs(object? sender, DocumentSavingAsEventArgs e) => Safe("сохранение", () =>
    {
        if (!e.Document.IsFamilyDocument) timer.Begin(Key("save", e.Document), OperationKind.Save, e.PathName);
    });

    private void OnSavedAs(object? sender, DocumentSavedAsEventArgs e) =>
        Safe("сохранение", () => timer.End(Key("save", e.Document), Outcome(e.Status), Model(e.Document), localFile: e.Document.PathName));

#if REVIT2021_OR_GREATER
    private void OnReloading(object? sender, DocumentReloadingLatestEventArgs e) =>
        Safe("обновление", () => timer.Begin(Key("reload", e.Document), OperationKind.Reload, e.Document.PathName));

    private void OnReloaded(object? sender, DocumentReloadedLatestEventArgs e) =>
        Safe("обновление", () => timer.End(Key("reload", e.Document), Outcome(e.Status), Model(e.Document)));
#endif

    /// <summary>Пара событий одного документа: вид операции и путь документа (за время операции он не меняется).</summary>
    private static string Key(string kind, Document document) => kind + "|" + document.PathName;

    /// <summary>Модель для записи: у совместной — путь хранилища, а не локальной копии.</summary>
    private static ModelRef Model(Document document)
    {
        string? central = null;
        try
        {
            if (document.IsWorkshared && document.GetWorksharingCentralModelPath() is { } path)
                central = ModelPathUtils.ConvertModelPathToUserVisiblePath(path);
        }
        catch (Exception)
        {
            // Путь хранилища не прочитался — считаем по файлу.
        }

        return ModelRef.From(central, document.PathName);
    }

    private static bool IsRevitLink(ExternalResourceType type) =>
        type.Equals(ExternalResourceTypes.BuiltInExternalResourceTypes.RevitLink);

    private static OperationOutcome Outcome(RevitAPIEventStatus status) => status switch
    {
        RevitAPIEventStatus.Succeeded => OperationOutcome.Succeeded,
        RevitAPIEventStatus.Cancelled => OperationOutcome.Cancelled,
        _ => OperationOutcome.Failed
    };

    /// <summary>Сбой учёта не уходит в Revit и не показывает окон — только запись в журнал ToadTools.</summary>
    private static void Safe(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            ErrorReport.Log($"Аналитика ToadFlow: {what}", exception);
        }
    }
}
