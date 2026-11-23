using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace ToadTools;

/// <summary>
///     Provides the application's services and manages their lifetimes
/// </summary>
public static class Host
{
    private static ServiceProvider? _services;
    private static NotificationWatcher? _notifications;

    /// <summary>
    ///     Builds the service container and starts the background work
    /// </summary>
    public static void Start()
    {
        var services = new ServiceCollection();
        services.RegisterServices();
        _services = services.BuildServiceProvider();

        _notifications = new NotificationWatcher();
        _notifications.Start();
    }

    /// <summary>
    ///     Stops the background work and disposes the services
    /// </summary>
    public static void Stop()
    {
        _notifications?.Dispose();
        _notifications = null;

        _services?.Dispose();
        _services = null;
    }

    /// <summary>
    ///     Get service of type <typeparamref name="T"/>
    /// </summary>
    /// <typeparam name="T">The type of service object to get</typeparam>
    /// <exception cref="System.InvalidOperationException">There is no service of type <typeparamref name="T"/></exception>
    public static T GetService<T>() where T : class
    {
        return _services!.GetRequiredService<T>();
    }

    /// <summary>
    ///     Creates a <see cref="FrameworkElement"/> with the scope lifetime and manages the scope lifecycle.
    /// </summary>
    /// <typeparam name="T">The type of FrameworkElement to get.</typeparam>
    /// <returns>A FrameworkElement of type T with managed scope lifecycle.</returns>
    /// <remarks>
    ///     The scope is automatically disposed when the element is unloaded or,
    ///     in the case of a Window, when it is closed.
    /// </remarks>
    /// <exception cref="System.InvalidOperationException">There is no service of type <typeparamref name="T"/></exception>
    public static T CreateScope<T>() where T : FrameworkElement
    {
        var scopeFactory = _services!.GetRequiredService<IServiceScopeFactory>();
        var scope = scopeFactory.CreateScope();

        var element = scope.ServiceProvider.GetRequiredService<T>();

        if (element is Window window)
        {
            window.Closed += (_, _) => scope.Dispose();
        }
        else
        {
            element.Unloaded += (_, _) => scope.Dispose();
        }

        return element;
    }
}
