using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Collisions.Core;

namespace Collisions.Revit;

/// <summary>
///     Переход к коллизии: 3D-вид «ToadClash — {пользователь Revit}» с секущим кубом 2 м вокруг точки и выбор элементов. Кроме
///     этого вида и выбора, в модели ничего не меняется; если вид уже есть и куб уже нужный — не меняется ничего.
/// </summary>
public static class CollisionNavigator
{
    public const string TransactionName = "ToadClash: показать коллизию";
    private const double FeetPerMm = 1 / 304.8;

    /// <summary>
    ///     Вид на пользователя Revit, а не Windows: при одинаковом логине Windows на разных машинах два человека иначе правили бы
    ///     один вид и получали бы ошибку занятости.
    /// </summary>
    public static string ViewName(Document document) =>
        $"ToadClash — {(string.IsNullOrWhiteSpace(document.Application.Username) ? Environment.UserName : document.Application.Username)}";

    public static NavigateResult Show(UIDocument uiDocument, NavigateRequest request)
    {
        var document = uiDocument.Document;
        var (own, other, link, sameModel) = Find(document, request);
        var cube = CollisionBox.Around(InternalMm(document, request.PointMm));

        var view = PrepareView(document, cube);
        uiDocument.ActiveView = view;
        if (uiDocument.GetOpenUIViews().FirstOrDefault(v => v.ViewId == view.Id) is { } uiView)
            uiView.ZoomAndCenterRectangle(Feet(cube.MinX, cube.MinY, cube.MinZ), Feet(cube.MaxX, cube.MaxY, cube.MaxZ));

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

    /// <summary>«Выбрать»: выделен только этот элемент коллизии, вид не меняется — среди многих систем видно, какой из двух где.</summary>
    public static SelectState SelectOne(UIDocument uiDocument, NavigateRequest request, CollisionElement element)
    {
        var (own, other, link, sameModel) = Find(uiDocument.Document, request);
        if (element == CollisionElement.Own)
        {
            if (own is null) return SelectState.NotFound;
            Select(uiDocument, own, null, null);
            return SelectState.Selected;
        }

        if (other is null) return link is null && !sameModel ? SelectState.NoLink : SelectState.NotFound;
#if !REVIT2023_OR_GREATER
        if (link is not null) return SelectState.InLinkNotSelectable;
#endif
        Select(uiDocument, null, other, link);
        return SelectState.Selected;
    }

    /// <summary>
    ///     Точка коллизии из мм общей системы — в мм внутренней системы модели. Оси внутренней системы — север проекта: куб
    ///     вдоль них стоит ровно по стенам и сторонам вида, а не повёрнут на угол истинного севера.
    /// </summary>
    private static (double X, double Y, double Z) InternalMm(Document document, (double X, double Y, double Z) sharedMm)
    {
        // Полное преобразование площадки переводит общую систему во внутреннюю.
        var point = document.ActiveProjectLocation.GetTotalTransform().OfPoint(Feet(sharedMm.X, sharedMm.Y, sharedMm.Z)).Multiply(1 / FeetPerMm);
        return (point.X, point.Y, point.Z);
    }

    /// <summary>Оба элемента коллизии: свой — в открытой модели, второй — в ней же или в связи по имени файла.</summary>
    private static (Element? Own, Element? Other, RevitLinkInstance? Link, bool SameModel) Find(Document document, NavigateRequest request)
    {
        var own = document.GetElement(request.OwnUniqueId);
        var sameModel = string.Equals(DocumentModel.Read(document).FileName, request.OtherModelName, StringComparison.OrdinalIgnoreCase);
        var link = sameModel ? null : LinkFinder.Find(document, request.OtherModelName, request.OtherModelPath);
        var other = sameModel ? document.GetElement(request.OtherUniqueId) : link?.GetLinkDocument()?.GetElement(request.OtherUniqueId);
        return (own, other, link, sameModel);
    }

    /// <summary>
    ///     Вид «ToadClash» с кубом; не больше одной транзакции, а если вид есть и куб уже нужный — ни одной. Вид создаётся при
    ///     первом переходе: изометрия, детализация «высокая», стиль «тонированный»; дальше его настройки — на усмотрение человека.
    /// </summary>
    /// <param name="cubeMm">Куб в мм внутренней системы модели.</param>
    private static View3D PrepareView(Document document, Box3 cubeMm)
    {
        var name = ViewName(document);
        var view = new FilteredElementCollector(document).OfClass(typeof(View3D)).Cast<View3D>()
            .FirstOrDefault(v => !v.IsTemplate && v.Name == name);
        var plan = NavigationPlanner.Plan(new ViewState(view is not null, view is null ? null : SectionBoxMm(view)), cubeMm);
        if (!plan.NeedsTransaction) return view!;

        // В модели только для чтения вид не создать и куб не поставить — человеку объясняется причина.
        if (document.IsReadOnly) throw new InvalidOperationException("модель открыта только для чтения — вид «ToadClash» в ней не создать");

        using var transaction = new Transaction(document, TransactionName);
        if (transaction.Start() != TransactionStatus.Started) throw new InvalidOperationException("Revit не дал изменить вид «ToadClash»");

        if (view is null)
        {
            var type = new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .First(t => t.ViewFamily == ViewFamily.ThreeDimensional);
            view = View3D.CreateIsometric(document, type.Id);
            view.Name = name;
            // Шаблон, который тип вида назначает новым видам, мог бы запретить детализацию и стиль — у этого вида шаблона нет.
            view.ViewTemplateId = ElementId.InvalidElementId;
            view.DetailLevel = ViewDetailLevel.Fine;
            view.DisplayStyle = DisplayStyle.Shading;
        }

        // Куб — по осям внутренней системы (север проекта); прежний куб, повёрнутый по истинному северу, заменяется.
        view.SetSectionBox(new BoundingBoxXYZ
        {
            Min = Feet(cubeMm.MinX, cubeMm.MinY, cubeMm.MinZ),
            Max = Feet(cubeMm.MaxX, cubeMm.MaxY, cubeMm.MaxZ)
        });
        view.IsSectionBoxActive = true;

        // Не зафиксировалось (Revit отменил из-за ошибки) — вида может не быть: активным его не делаем.
        if (transaction.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit отменил изменение вида «ToadClash»");
        return view;
    }

    /// <summary>Куб вида в мм внутренней системы; выключен или повёрнут относительно осей проекта — null: такой куб ставится заново.</summary>
    private static Box3? SectionBoxMm(View3D view)
    {
        if (!view.IsSectionBoxActive) return null;
        var box = view.GetSectionBox();
        if (!box.Transform.BasisX.IsAlmostEqualTo(XYZ.BasisX) || !box.Transform.BasisY.IsAlmostEqualTo(XYZ.BasisY)) return null;
        var corners = Corners(box).ToList();
        return new Box3(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Min(p => p.Z), corners.Max(p => p.X), corners.Max(p => p.Y),
            corners.Max(p => p.Z));
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

    /// <summary>Восемь углов габарита — в мм внутренней системы.</summary>
    private static IEnumerable<XYZ> Corners(BoundingBoxXYZ box)
    {
        var toTarget = box.Transform;
        foreach (var x in new[] { box.Min.X, box.Max.X })
        foreach (var y in new[] { box.Min.Y, box.Max.Y })
        foreach (var z in new[] { box.Min.Z, box.Max.Z })
            yield return toTarget.OfPoint(new XYZ(x, y, z)).Multiply(1 / FeetPerMm);
    }

    private static XYZ Feet(double x, double y, double z) => new(x * FeetPerMm, y * FeetPerMm, z * FeetPerMm);
}
