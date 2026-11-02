using System.IO;

namespace Collisions.Core;

/// <summary>Вариант «кто правит» в окне «Передать…»: значение для сервера и подпись для человека.</summary>
public sealed record SideOption(CollisionAssignee Assignee, string Label);

/// <summary>
///     Подписи сторон коллизии для человека: разделом (ОВК × ОВВ — «ОВК», «ОВВ», «ОВК и ОВВ»). Коллизию внутри раздела
///     разделом не различить — подпись по модели, а если и модель одна — «сторона A / B».
/// </summary>
public static class SideNames
{
    public static string Label(CollisionRowDto collision, CollisionAssignee assignee)
    {
        var (a, b) = Names(collision);
        return assignee switch
        {
            CollisionAssignee.SideA => a ?? "сторона A",
            CollisionAssignee.SideB => b ?? "сторона B",
            CollisionAssignee.Unknown => "неизвестно",
            _ => a is null || b is null ? "обе стороны" : $"{a} и {b}"
        };
    }

    private static (string? A, string? B) Names(CollisionRowDto c)
    {
        if (Distinct(c.DisciplineA, c.DisciplineB)) return (c.DisciplineA.Trim(), c.DisciplineB.Trim());
        var (modelA, modelB) = (Path.GetFileNameWithoutExtension(c.ModelNameA ?? ""), Path.GetFileNameWithoutExtension(c.ModelNameB ?? ""));
        return Distinct(modelA, modelB) ? (modelA, modelB) : (null, null);
    }

    private static bool Distinct(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && !string.Equals(a!.Trim(), b!.Trim(), StringComparison.OrdinalIgnoreCase);
}
