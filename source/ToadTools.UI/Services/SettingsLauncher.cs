namespace ToadTools.UI.Services;

/// <summary>Раздел окна «Настройки» ToadTools.</summary>
public enum SettingsSection
{
    ToadFlow,
    Appearance,
    Log,
    Cache,
    About
}

/// <summary>
///     Открывает окно «Настройки» из любого инструмента. Само окно живёт в ToadTools.Connection, на которую инструменты
///     и ToadTools.UI не ссылаются, поэтому приложение ToadTools при запуске записывает в <see cref="Handler" />, как
///     его показать.
/// </summary>
public static class SettingsLauncher
{
    /// <summary>Показывает окно на разделе; задаёт приложение ToadTools при запуске.</summary>
    public static Action<SettingsSection>? Handler { get; set; }

    /// <summary>Открывает «Настройки» на разделе; до регистрации обработчика ничего не делает.</summary>
    public static void Open(SettingsSection section = SettingsSection.ToadFlow) => Handler?.Invoke(section);
}
