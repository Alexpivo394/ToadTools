using ToadDesign;
using ToadTools.UI.Services;

namespace ToadTools.UI.Controls;

/// <summary>
///     Кнопка-шестерёнка «Настройки ToadTools» для заголовка окна (<c>TitleBarContent</c>) и шапки панели: открывает
///     настройки на разделе «Вид».
/// </summary>
public sealed class SettingsButton : ToadDesign.Button
{
    public SettingsButton()
    {
        // Неявный стиль ToadDesign задан для типа toad:Button и наследника сам не находит.
        SetResourceReference(StyleProperty, typeof(ToadDesign.Button));
        Icon = Symbol.Settings;
        Variant = ButtonVariant.Subtle;
        ToolTip = "Настройки ToadTools";
    }

    protected override void OnClick()
    {
        base.OnClick();
        try
        {
            SettingsLauncher.Open(SettingsSection.Appearance);
        }
        catch (Exception exception)
        {
            // Обработчик щелчка в окне Revit: необработанное исключение уронило бы Revit.
            ErrorReport.Show("Настройки ToadTools", exception);
        }
    }
}
