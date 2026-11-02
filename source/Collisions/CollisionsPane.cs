using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
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
    private static bool _reloadQueued;

    /// <summary>Регистрация панели и подписка на смену документа. Вызывается один раз из <c>OnStartup</c>.</summary>
    public static void Register(UIControlledApplication application)
    {
        var api = new ClashApi(ToadFlowClient.Shared);
        var thumbnails = new ThumbnailCache(ThumbnailCache.DefaultFolder, api.ImageAsync);
        var mainWindow = application.MainWindowHandle;
        _viewModel = new PaneViewModel(api, new RevitBridge(), ConnectionStore.Shared, new ModelChoices(ModelChoices.DefaultPath), thumbnails,
            new PaneSession(), () => ConnectionDialog.Show(mainWindow), new CardDialogs(mainWindow));
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
        application.DockableFrameVisibilityChanged += OnFrameVisibilityChanged;
        application.ControlledApplication.DocumentClosing += OnDocumentClosing;
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
        if (Same(_document, args.Document)) return;
        if (sender is UIApplication application && !IsShown(application))
        {
            _document = null;
            return;
        }

        _document = args.Document;
        Reload();
    }

    /// <summary>
    ///     Панель показана не кнопкой ленты — из меню «Интерфейс пользователя» Revit или восстановлена при запуске: она
    ///     обновляется так же.
    /// </summary>
    private static void OnFrameVisibilityChanged(object? sender, DockableFrameVisibilityChangedEventArgs args)
    {
        if (args.PaneId?.Guid != Id.Guid || !args.DockableFrameShown) return;
        try
        {
            _document = (sender as UIApplication)?.ActiveUIDocument?.Document;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            _document = null;
        }

        Reload();
    }

    /// <summary>Закрывается документ, чьи коллизии показаны: список очищается, к закрытому документу панель больше не обращается.</summary>
    private static void OnDocumentClosing(object? sender, DocumentClosingEventArgs args)
    {
        if (!Same(_document, args.Document)) return;
        _document = null;
        if (_viewModel is not { } viewModel || _dispatcher is not { } dispatcher) return;
        dispatcher.BeginInvoke(new Action(viewModel.DocumentClosed));
    }

    /// <summary>
    ///     Тот же документ. Revit на каждый вызов отдаёт новую обёртку того же документа: сравнивать по ссылке нельзя, только
    ///     Equals; обёртка закрытого документа может бросить — тогда документы разные.
    /// </summary>
    private static bool Same(Document? known, Document? other)
    {
        if (known is null || other is null) return false;
        try
        {
            return known.Equals(other);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return false;
        }
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

    /// <summary>
    ///     Модель панели живёт в UI-потоке панели: туда же отправляется и обновление. Несколько поводов подряд (кнопка ленты и
    ///     показ панели) дают одно обновление.
    /// </summary>
    private static void Reload()
    {
        if (_viewModel is not { } viewModel || _dispatcher is not { } dispatcher || _reloadQueued) return;
        _reloadQueued = true;
        dispatcher.BeginInvoke(new Action(() =>
        {
            _reloadQueued = false;
            _ = viewModel.ActivateAsync(); // не бросает: ошибки шага показываются в панели
        }));
    }
}
