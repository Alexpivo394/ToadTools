using Autodesk.Revit.DB;

namespace CableTrays;

internal static class TrayGraphics
{
    /// <summary>Чёрный цвет — базовая покраска перед раскраской по признаку.</summary>
    public static OverrideGraphicSettings Black()
    {
        var color = new Color(0, 0, 0);
        var ogs = new OverrideGraphicSettings();
        ogs.SetProjectionLineColor(color);
        ogs.SetCutForegroundPatternColor(color);
        ogs.SetSurfaceForegroundPatternColor(color);
        ogs.SetCutLineColor(color);
        return ogs;
    }

    public static OverrideGraphicSettings Colored(byte r, byte g, byte b)
    {
        var color = new Color(r, g, b);
        var ogs = new OverrideGraphicSettings();
        ogs.SetProjectionLineColor(color);
        ogs.SetCutForegroundPatternColor(color);
        ogs.SetCutLineColor(color);
        return ogs;
    }

    /// <summary>Проверяет, что на виде можно переопределять графику, иначе показывает предупреждение.</summary>
    public static bool CheckView(View view)
    {
        if (view.AreGraphicsOverridesAllowed()) return true;

        TrayReport.ShowWarning($"На виде «{view.Name}» нельзя переопределять графику. Откройте план, разрез или 3D-вид.");
        return false;
    }
}
