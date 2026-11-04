namespace Collisions.Core;

/// <summary>Вид «ToadClash» сейчас: есть ли он и его секущий куб в мм общей системы (null — куба нет или выключен).</summary>
public sealed record ViewState(bool Exists, Box3? SectionBoxMm);

/// <summary>Что поменять в модели, чтобы показать коллизию; ничего — без транзакции, в журнале отмен ничего не появится.</summary>
public sealed record NavigationPlan(bool CreateView, bool SetSectionBox)
{
    public bool NeedsTransaction => CreateView || SetSectionBox;
}

/// <summary>
///     Решает, нужна ли транзакция перехода: вид есть и куб уже нужный (с допуском 1 мм) — модель не трогается. Повторный
///     щелчок по той же коллизии не плодит записи «ToadClash: показать коллизию» и работает в модели только для чтения.
/// </summary>
public static class NavigationPlanner
{
    public const double ToleranceMm = 1;

    public static NavigationPlan Plan(ViewState current, Box3 target)
    {
        if (!current.Exists) return new NavigationPlan(CreateView: true, SetSectionBox: true);
        return new NavigationPlan(CreateView: false, SetSectionBox: current.SectionBoxMm is not { } box || !Same(box, target));
    }

    private static bool Same(Box3 a, Box3 b) =>
        Near(a.MinX, b.MinX) && Near(a.MinY, b.MinY) && Near(a.MinZ, b.MinZ) && Near(a.MaxX, b.MaxX) && Near(a.MaxY, b.MaxY) && Near(a.MaxZ, b.MaxZ);

    private static bool Near(double a, double b) => Math.Abs(a - b) <= ToleranceMm;
}
