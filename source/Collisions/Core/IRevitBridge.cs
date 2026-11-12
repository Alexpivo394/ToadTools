namespace Collisions.Core;

/// <summary>Что с элементом другой модели после перехода к коллизии.</summary>
public enum OtherState
{
    /// <summary>Связь найдена, элемент выделен вместе со своим.</summary>
    Selected,

    /// <summary>Связь найдена, но эта версия Revit не выделяет элементы связей.</summary>
    InLinkNotSelectable,

    /// <summary>Модель не подгружена связью.</summary>
    NoLink,

    /// <summary>Связь есть, а элемента в ней нет.</summary>
    NotFoundInLink
}

/// <param name="PointMm">Точка коллизии в мм общей системы координат.</param>
public sealed record NavigateRequest(string OwnUniqueId, string OtherModelName, string OtherModelPath, string OtherUniqueId, (double X, double Y, double Z) PointMm);

public sealed record NavigateResult(bool OwnFound, OtherState Other, string? OtherLinkName);

/// <summary>Какой элемент коллизии выделить.</summary>
public enum CollisionElement
{
    Own,
    Other
}

/// <summary>Итог «Выбрать»: выделен ли элемент, а если нет — почему.</summary>
public enum SelectState
{
    Selected,

    /// <summary>Элемента нет в модели или в связи.</summary>
    NotFound,

    /// <summary>Модель второго элемента не подгружена связью.</summary>
    NoLink,

    /// <summary>Элемент в связи, а эта версия Revit не выделяет элементы связей.</summary>
    InLinkNotSelectable
}

/// <summary>Всё, что панели нужно от Revit. Реализация ходит в Revit через внешние события; модели панели Revit API не нужен.</summary>
public interface IRevitBridge
{
    /// <summary>Открытый документ: путь центральной модели (или файла) и имя файла центральной модели; null — документа нет.</summary>
    Task<(string? CentralPath, string FileName)?> ActiveDocumentAsync();

    /// <summary>Вид «ToadClash» с секущим кубом вокруг коллизии и выбор её элементов.</summary>
    Task<NavigateResult> ShowAsync(NavigateRequest request);

    /// <summary>Выделяет только один элемент коллизии, второй снимается с выделения; вид и куб не меняются.</summary>
    Task<SelectState> SelectAsync(NavigateRequest request, CollisionElement element);
}
