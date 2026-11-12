using Autodesk.Revit.UI;
using Collisions.Core;
using Nice3point.Revit.Toolkit.External;

namespace Collisions.Revit;

/// <summary>Панель ходит в Revit только отсюда: всё через внешние события Toolkit, сеть сюда не попадает.</summary>
public sealed class RevitBridge : IRevitBridge
{
    private readonly AsyncRequestExternalEvent<(string? CentralPath, string FileName)?> _activeDocument = new(ActiveDocument);
    private readonly AsyncRequestExternalEvent<NavigateRequest, NavigateResult> _show = new(Show);
    private readonly AsyncRequestExternalEvent<(NavigateRequest Request, CollisionElement Element), SelectState> _select = new(Select);

    public Task<(string? CentralPath, string FileName)?> ActiveDocumentAsync() => _activeDocument.RaiseAsync();

    public Task<NavigateResult> ShowAsync(NavigateRequest request) => _show.RaiseAsync(request);

    public Task<SelectState> SelectAsync(NavigateRequest request, CollisionElement element) => _select.RaiseAsync((request, element));

    /// <summary>Документ проекта в активном окне; семейство или пустой Revit — null.</summary>
    private static (string? CentralPath, string FileName)? ActiveDocument(UIApplication application) =>
        application.ActiveUIDocument?.Document is { IsFamilyDocument: false } document ? DocumentModel.Read(document) : null;

    private static NavigateResult Show(UIApplication application, NavigateRequest request) =>
        application.ActiveUIDocument is { Document.IsFamilyDocument: false } uiDocument
            ? CollisionNavigator.Show(uiDocument, request)
            : throw new InvalidOperationException("Откройте модель проекта");

    private static SelectState Select(UIApplication application, (NavigateRequest Request, CollisionElement Element) input) =>
        application.ActiveUIDocument is { Document.IsFamilyDocument: false } uiDocument
            ? CollisionNavigator.SelectOne(uiDocument, input.Request, input.Element)
            : throw new InvalidOperationException("Откройте модель проекта");
}
