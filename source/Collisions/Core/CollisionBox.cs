namespace Collisions.Core;

/// <summary>Габарит в мм.</summary>
public sealed record Box3(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ);

/// <summary>
///     Секущий куб вида «ToadClash»: 2 × 2 × 2 м с точкой коллизии в центре. По элементам куб не растёт — длинные трубы и
///     воздуховоды раздували его до 20 м; шире человек растянет его сам.
/// </summary>
public static class CollisionBox
{
    public const double Half = 1000;

    /// <summary>Куб с центром в точке коллизии.</summary>
    /// <param name="pointMm">Точка коллизии.</param>
    public static Box3 Around((double X, double Y, double Z) pointMm) =>
        new(pointMm.X - Half, pointMm.Y - Half, pointMm.Z - Half, pointMm.X + Half, pointMm.Y + Half, pointMm.Z + Half);
}
