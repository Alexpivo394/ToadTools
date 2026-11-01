using Autodesk.Revit.UI;
using Collisions.Core;
using Nice3point.Revit.Toolkit.External;

namespace Collisions.Revit;

/// <summary>Панель ходит в Revit только отсюда: всё через внешние события Toolkit, сеть сюда не попадает.</summary>
public sealed class RevitBridge : IRevitBridge
{
    private readonly AsyncRequestExternalEvent<(string? CentralPath, string FileName)?> _activeDocument = new(ActiveDocument);

    public Task<(string? CentralPath, string FileName)?> ActiveDocumentAsync() => _activeDocument.RaiseAsync();

    // Переход к коллизии — задача 8 плана этапа 7.
    public Task<NavigateResult> ShowAsync(NavigateRequest request) => throw new NotSupportedException("Переход к коллизии ещё не подключён");

    /// <summary>Документ проекта в активном окне; семейство или пустой Revit — null.</summary>
    internal static (string? CentralPath, string FileName)? ActiveDocument(UIApplication application) =>
        application.ActiveUIDocument?.Document is { IsFamilyDocument: false } document ? DocumentModel.Read(document) : null;
}
