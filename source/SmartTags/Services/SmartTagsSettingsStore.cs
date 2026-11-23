using SmartTags.Models;
using ToadTools.UI.Services;

namespace SmartTags.Services;

/// <summary>Хранит настройки окна в %AppData%\ToadTools\SmartTags\config.json; до переноса лежали в SmartTagsSettings рядом с dll.</summary>
public sealed class SmartTagsSettingsStore
{
    private readonly SettingsFile<SmartTagsFormState> _file = new("SmartTags", "SmartTagsSettings");

    /// <summary>Настройки — удобство, а не данные: при любой ошибке молча берём умолчания.</summary>
    public SmartTagsFormState Load() => _file.Load();

    public void Save(SmartTagsFormState state) => _file.Save(state);
}
