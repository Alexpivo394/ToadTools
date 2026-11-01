using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Collisions.Core;

namespace Collisions.Revit;

/// <summary>
///     Переход к коллизии: 3D-вид «ToadClash — {пользователь}» с секущим кубом вокруг точки и выбор элементов. Кроме этого
///     вида и выбора, в модели ничего не меняется.
/// </summary>
public static class CollisionNavigator
{
    public const string TransactionName = "ToadClash: показать коллизию";
    private const double FeetPerMm = 1 / 304.8;

    public static string ViewName => $"ToadClash — {Environment.UserName}";

    public static NavigateResult Show(UIDocument uiDocument, NavigateRequest request)
    {
        var document = uiDocument.Document;
        // Точка и габариты — в общей системе координат; сюда (общая → внутренняя) переводит полное преобразование площадки.
        var shared = document.ActiveProjectLocation.GetTotalTransform();
        var toShared = shared.Inverse;

        var own = document.GetElement(request.OwnUniqueId);
        var sameModel = string.Equals(DocumentModel.Read(document).FileName, request.OtherModelName, StringComparison.OrdinalIgnoreCase);
        var link = sameModel ? null : LinkFinder.Find(document, request.OtherModelName, request.OtherModelPath);
        var other = sameModel ? document.GetElement(request.OtherUniqueId) : link?.GetLinkDocument()?.GetElement(request.OtherUniqueId);

        var corners = new List<XYZ>();
        if (own?.get_BoundingBox(null) is { } ownBox) corners.AddRange(Corners(ownBox, toShared));
        if (other?.get_BoundingBox(null) is { } otherBox)
            corners.AddRange(Corners(otherBox, link is null ? toShared : toShared.Multiply(link.GetTotalTransform())));

        var elements = corners.Count == 0
            ? null
            : new Box3(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Min(p => p.Z), corners.Max(p => p.X), corners.Max(p => p.Y),
                corners.Max(p => p.Z));
        var cube = CollisionBox.Around(request.PointMm, elements);

        var view = PrepareView(document, cube, shared);
        uiDocument.ActiveView = view;
        if (uiDocument.GetOpenUIViews().FirstOrDefault(v => v.ViewId == view.Id) is { } uiView)
            uiView.ZoomAndCenterRectangle(shared.OfPoint(Feet(cube.MinX, cube.MinY, cube.MinZ)), shared.OfPoint(Feet(cube.MaxX, cube.MaxY, cube.MaxZ)));

        Select(uiDocument, own, other, link);

        var state = other is null
            ? link is null && !sameModel ? OtherState.NoLink : OtherState.NotFoundInLink
            : link is null ? OtherState.Selected
#if REVIT2023_OR_GREATER
            : OtherState.Selected;
#else
            : OtherState.InLinkNotSelectable;
#endif
        return new NavigateResult(own is not null, state, link?.Name);
    }

    /// <summary>
    ///     Вид «ToadClash» с кубом; одна транзакция. Вид создаётся при первом переходе: изометрия, детализация «высокая»,
    ///     стиль «тонированный»; дальше его настройки — на усмотрение человека.
    /// </summary>
    private static View3D PrepareView(Document document, Box3 cubeMm, Transform shared)
    {
        using var transaction = new Transaction(document, TransactionName);
        transaction.Start();

        var view = new FilteredElementCollector(document).OfClass(typeof(View3D)).Cast<View3D>()
            .FirstOrDefault(v => !v.IsTemplate && v.Name == ViewName);
        if (view is null)
        {
            var type = new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .First(t => t.ViewFamily == ViewFamily.ThreeDimensional);
            view = View3D.CreateIsometric(document, type.Id);
            view.Name = ViewName;
            // Шаблон, который тип вида назначает новым видам, мог бы запретить детализацию и стиль — у этого вида шаблона нет.
            view.ViewTemplateId = ElementId.InvalidElementId;
            view.DetailLevel = ViewDetailLevel.Fine;
            view.DisplayStyle = DisplayStyle.Shading;
        }

        // Куб задан в общей системе (футы), его Transform переводит в модель.
        view.SetSectionBox(new BoundingBoxXYZ
        {
            Transform = shared,
            Min = Feet(cubeMm.MinX, cubeMm.MinY, cubeMm.MinZ),
            Max = Feet(cubeMm.MaxX, cubeMm.MaxY, cubeMm.MaxZ)
        });
        view.IsSectionBoxActive = true;

        transaction.Commit();
        return view;
    }

    private static void Select(UIDocument uiDocument, Element? own, Element? other, RevitLinkInstance? link)
    {
        var ids = new List<ElementId>();
        if (own is not null) ids.Add(own.Id);
        if (other is not null && link is null) ids.Add(other.Id);
#if REVIT2023_OR_GREATER
        if (other is not null && link is not null)
        {
            var references = ids.Select(id => new Reference(uiDocument.Document.GetElement(id))).ToList();
            references.Add(new Reference(other).CreateLinkReference(link));
            uiDocument.Selection.SetReferences(references);
            return;
        }
#endif
        uiDocument.Selection.SetElementIds(ids);
    }

    /// <summary>Восемь углов габарита — в мм системы, куда ведёт <paramref name="transform" /> (габарит может быть повёрнут).</summary>
    private static IEnumerable<XYZ> Corners(BoundingBoxXYZ box, Transform transform)
    {
        var toTarget = transform.Multiply(box.Transform);
        foreach (var x in new[] { box.Min.X, box.Max.X })
        foreach (var y in new[] { box.Min.Y, box.Max.Y })
        foreach (var z in new[] { box.Min.Z, box.Max.Z })
            yield return toTarget.OfPoint(new XYZ(x, y, z)).Multiply(1 / FeetPerMm);
    }

    private static XYZ Feet(double x, double y, double z) => new(x * FeetPerMm, y * FeetPerMm, z * FeetPerMm);
}
