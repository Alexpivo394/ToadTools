using System.ComponentModel;

namespace ToadTools.UI.Services;

/// <summary>
///     Выбор темы и размера окон, к которому привязано окно «Настройки». Реализует <see cref="ThemePreference" />;
///     тесты подставляют свою.
/// </summary>
public interface IThemePreference : INotifyPropertyChanged
{
    /// <summary>Тема окон: светлая, тёмная или как в Windows.</summary>
    ThemeChoice Theme { get; set; }

    /// <summary>Размер элементов: «Авто», обычный или компактный.</summary>
    DensityChoice Density { get; set; }

    /// <summary>Компактны ли окна сейчас — в том числе что выбрало «Авто».</summary>
    bool IsCompact { get; }
}
