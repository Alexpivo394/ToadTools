namespace Collisions.Core;

/// <summary>Габарит в мм.</summary>
public sealed record Box3(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ);

/// <summary>Секущий куб вида «ToadClash»: вокруг точки коллизии, по умолчанию 3 × 3 × 3 м.</summary>
public static class CollisionBox
{
    public const double DefaultHalf = 1500;
    public const double MaxHalf = 10_000;

    /// <summary>
    ///     Куб с центром в точке коллизии. Если элементы выходят за 3 м, сторона растёт до их дальней грани, но не больше 20 м.
    /// </summary>
    /// <param name="pointMm">Точка коллизии.</param>
    /// <param name="elementsMm">Общий габарит обоих элементов в той же системе; null — неизвестен.</param>
    public static Box3 Around((double X, double Y, double Z) pointMm, Box3? elementsMm)
    {
        double Half(double point, double? min, double? max)
        {
            var half = DefaultHalf;
            if (min is { } low) half = Math.Max(half, point - low);
            if (max is { } high) half = Math.Max(half, high - point);
            return Math.Min(half, MaxHalf);
        }

        var hx = Half(pointMm.X, elementsMm?.MinX, elementsMm?.MaxX);
        var hy = Half(pointMm.Y, elementsMm?.MinY, elementsMm?.MaxY);
        var hz = Half(pointMm.Z, elementsMm?.MinZ, elementsMm?.MaxZ);
        return new Box3(pointMm.X - hx, pointMm.Y - hy, pointMm.Z - hz, pointMm.X + hx, pointMm.Y + hy, pointMm.Z + hz);
    }
}
