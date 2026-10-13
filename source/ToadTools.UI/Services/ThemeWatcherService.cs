using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ToadTools.UI.Services;

public class ThemeWatcherService
{
    private static readonly List<FrameworkElement> ObservedElements = new List<FrameworkElement>();
    private static bool _subscribed;

    public static void Initialize()
    {
        UiApplication.Current.Resources = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/ToadTools.UI;component/Styles/App.Resources.xaml", UriKind.Absolute)
        };

        // Initialize вызывается каждым окном и каждым диалогом — подписываемся один раз,
        // иначе обработчики копятся и тема применяется ко всем окнам много раз подряд.
        if (_subscribed) return;
        ApplicationThemeManager.Changed += OnApplicationThemeManagerChanged;
        _subscribed = true;
    }

    public static void ApplyTheme(ApplicationTheme theme)
    {
        ApplicationThemeManager.Apply(theme);
        UpdateBackground(theme);
    }

    private static void OnApplicationThemeManagerChanged(ApplicationTheme currentapplicationtheme, System.Windows.Media.Color systemaccent)
    {
        // Копия списка: применение темы может вызвать Loaded/Unloaded и изменить его.
        foreach (var frameworkElement in ObservedElements.ToArray())
        {
            // Закрытое окно (или элемент, убранный с экрана) больше не обновляем.
            if (PresentationSource.FromVisual(frameworkElement) is null)
            {
                ObservedElements.Remove(frameworkElement);
                continue;
            }

            try
            {
                ApplicationThemeManager.Apply(frameworkElement);
                UpdateDictionary(frameworkElement);
            }
            catch
            {
                // Одно проблемное окно не должно ронять смену темы (и команду, которая её вызвала).
                ObservedElements.Remove(frameworkElement);
            }
        }
    }

    private static void UpdateDictionary(FrameworkElement frameworkElement)
    {
        var themedResources = frameworkElement.Resources.MergedDictionaries
            .Where(dictionary => dictionary.Source.OriginalString.Contains("ToadTools.UI;", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        frameworkElement.Resources.MergedDictionaries.Insert(0, UiApplication.Current.Resources.MergedDictionaries[0]);
        frameworkElement.Resources.MergedDictionaries.Insert(1, UiApplication.Current.Resources.MergedDictionaries[1]);

        foreach (var themedResource in themedResources)
        {
            frameworkElement.Resources.MergedDictionaries.Remove(themedResource);
        }
    }
    public static void Watch(FrameworkElement frameworkElement)
    {
        ApplicationThemeManager.Apply(frameworkElement);
        frameworkElement.Loaded += OnWatchedElementLoaded;
        frameworkElement.Unloaded += OnWatchedElementUnloaded;

        // У окна Unloaded при закрытии приходит не всегда — снимаем с наблюдения и по Closed.
        if (frameworkElement is Window window)
            window.Closed += (_, _) => ObservedElements.Remove(window);
    }
    private static void OnWatchedElementLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        ObservedElements.Add(element);

        if (element.Resources.MergedDictionaries[0].Source.OriginalString != UiApplication.Current.Resources.MergedDictionaries[0].Source.OriginalString)
        {
            ApplicationThemeManager.Apply(element);
            UpdateDictionary(element);
        }
    }
    private static void OnWatchedElementUnloaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        ObservedElements.Remove(element);
    }
    private static void UpdateBackground(ApplicationTheme theme)
    {
        foreach (var window in ObservedElements.Select(Window.GetWindow).Where(w => w != null).Distinct().ToArray())
        {
            if (PresentationSource.FromVisual(window) is null) continue;

            try
            {
                WindowBackgroundManager.UpdateBackground(window, theme, WindowBackdropType.Mica);
            }
            catch
            {
                // Фон не обновился — окно останется со старым фоном, это не повод падать.
            }
        }
    }
}
