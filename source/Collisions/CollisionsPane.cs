using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Collisions.Core;
using Collisions.Revit;
using Collisions.ViewModels;
using Collisions.Views;
using Nice3point.Revit.Toolkit.Decorators;
using ToadTools.Connection;

namespace Collisions;

/// <summary>Закрепляемая панель «Коллизии ToadFlow»: регистрируется при запуске Revit, кнопка ленты показывает и скрывает её.</summary>
public static class CollisionsPane
{
    /// <summary>Постоянный идентификатор: по нему Revit помнит положение панели между запусками.</summary>
    public static readonly DockablePaneId Id = new(new Guid("6f1f4c3e-2b7a-4f0e-9d2c-5a8b1e7c4d90"));

    public const string Title = "Коллизии ToadFlow";

    private static PaneViewModel? _viewModel;
    private static Dispatcher? _dispatcher;
    private static Document? _document;

    /// <summary>Регистрация панели и подписка на смену документа. Вызывается один раз из <c>OnStartup</c>.</summary>
    public static void Register(UIControlledApplication application)
    {
        var api = new ClashApi(ToadFlowClient.Shared);
        var thumbnails = new ThumbnailCache(ThumbnailCache.DefaultFolder, api.ImageAsync);
        var mainWindow = application.MainWindowHandle;
        _viewModel = new PaneViewModel(api, new RevitBridge(), ConnectionStore.Shared, new ModelChoices(ModelChoices.DefaultPath), thumbnails,
            new PaneSession(), () => ConnectionDialog.Show(mainWindow));
        var view = new CollisionsPaneView(_viewModel);
        _dispatcher = view.Dispatcher;

        DockablePaneProvider
            .Register(application, Id.Guid, Title)
            .SetConfiguration(data =>
            {
                data.FrameworkElement = view;
                data.InitialState = new DockablePaneState
                {
                    DockPosition = DockPosition.Right,
                    MinimumWidth = 320
                };
            });

        application.ViewActivated += OnViewActivated;
        ConnectionStore.Shared.Changed += (_, _) => Reload(); // вход и выход — из окна подключения или переноса ParamChecker в фоне
        _ = Task.Run(() => thumbnails.Prune(TimeSpan.FromDays(30)));
    }

    /// <summary>Кнопка ленты: показать панель (и обновить её) или скрыть.</summary>
    public static void Toggle(UIApplication application)
    {
        var pane = application.GetDockablePane(Id);
        if (pane.IsShown())
        {
            pane.Hide();
            return;
        }

        pane.Show();
        _document = application.ActiveUIDocument?.Document;
        Reload();
    }

    /// <summary>
    ///     Другой документ стал активным — модель определяется заново. Переключение видов того же документа ничего не
    ///     меняет; скрытая панель не ходит в сеть, её обновит кнопка ленты.
    /// </summary>
    private static void OnViewActivated(object? sender, ViewActivatedEventArgs args)
    {
        if (ReferenceEquals(args.Document, _document)) return;
        if (sender is UIApplication application && !IsShown(application))
        {
            _document = null;
            return;
        }

        _document = args.Document;
        Reload();
    }

    private static bool IsShown(UIApplication application)
    {
        try
        {
            return application.GetDockablePane(Id).IsShown();
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Модель панели живёт в UI-потоке панели: туда же отправляется и обновление.</summary>
    private static void Reload()
    {
        if (_viewModel is not { } viewModel || _dispatcher is not { } dispatcher) return;
        dispatcher.BeginInvoke(new Action(() => _ = viewModel.ActivateAsync()));
    }
}
