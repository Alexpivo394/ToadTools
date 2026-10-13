using System.Globalization;

namespace SmartTags.Services;

/// <summary>
///     Блокировка 3D-вида: марки на 3D ставятся только на заблокированном виде. Стандартный вид «{3D}»
///     сначала переименовывается (без имени его не заблокировать); если заблокировать не вышло —
///     блокируется копия вида.
/// </summary>
internal static class ViewLocker
{
    public const string LockedViewName = "Умные выноски 3D";

    public static bool IsDefaultName(string name) => name.Trim().StartsWith("{3D", StringComparison.OrdinalIgnoreCase);

    /// <summary>Возвращает заблокированный вид (исходный или копию) либо null.</summary>
    public static View3D? Lock(View3D view, Action<string>? log)
    {
        if (view.IsLocked) return view;

        try
        {
            if (IsDefaultName(view.Name))
                view.Name = UniqueName(view.Document, LockedViewName);

            view.SaveOrientationAndLock();
            if (view.IsLocked)
            {
                log?.Invoke("3D-вид заблокирован: " + view.Name);
                return view;
            }
        }
        catch (Exception ex)
        {
            log?.Invoke("Не удалось заблокировать 3D-вид: " + ex.Message);
        }

        try
        {
            if (view.Document.GetElement(view.Duplicate(ViewDuplicateOption.Duplicate)) is not View3D copy) return null;

            copy.Name = UniqueName(view.Document, LockedViewName);
            copy.SaveOrientationAndLock();
            log?.Invoke($"Заблокирована копия 3D-вида: {copy.Name}");
            return copy.IsLocked ? copy : null;
        }
        catch (Exception ex)
        {
            log?.Invoke("Не удалось заблокировать копию 3D-вида: " + ex.Message);
            return null;
        }
    }

    private static string UniqueName(Document document, string baseName)
    {
        var names = new HashSet<string>(
            new FilteredElementCollector(document).OfClass(typeof(View)).Select(view => view.Name),
            StringComparer.CurrentCultureIgnoreCase);

        if (!names.Contains(baseName)) return baseName;
        for (var i = 2; i < 1000; i++)
        {
            var name = $"{baseName} {i}";
            if (!names.Contains(name)) return name;
        }

        return $"{baseName} {DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture)}";
    }
}
