using System.IO;
using System.Windows.Threading;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Families.Core;
using Families.Revit;
using Families.ViewModels;
using Families.Views;
using Nice3point.Revit.Toolkit.Decorators;
using ToadTools.Connection;

namespace Families;

/// <summary>Закрепляемая панель «Семейства ToadFlow»: регистрируется при запуске Revit, кнопка ленты показывает и скрывает её.</summary>
public static class FamiliesPane
{
    /// <summary>Постоянный идентификатор: по нему Revit помнит положение панели между запусками.</summary>
    public static readonly DockablePaneId Id = new(new Guid("3b0e6d7a-91c4-4f53-a8e2-5d61c7f04b19"));

    public const string Title = "Семейства ToadFlow";

    private static readonly string LogPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToadTools", "Families", "startup.log");

    private static FamiliesPaneViewModel? _viewModel;
    private static Dispatcher? _dispatcher;
    private static string? _documentKey;
    private static bool _reloadQueued;

    /// <summary>Почему панели нет; null — зарегистрирована.</summary>
    public static string? Failure { get; private set; }

    /// <summary>
    ///     Регистрация панели и подписка на смену документа. Вызывается один раз из <c>OnStartup</c>. Не бросает: сбой пишется в журнал,
    ///     а ToadTools запускается дальше без панели.
    /// </summary>
    public static void Register(UIControlledApplication application)
    {
        try
        {
            RegisterCore(application);
        }
        catch (Exception exception)
        {
            Failure = exception.Message;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} Панель «Семейства» не зарегистрирована:{Environment.NewLine}{exception}{Environment.NewLine}");
            }
            catch (Exception)
            {
                // журнал недоступен — причина видна по кнопке
            }
        }
    }

    private static void RegisterCore(UIControlledApplication application)
    {
        var api = new FamiliesApi(ToadFlowClient.Shared);
        var previews = new PreviewCache(PreviewCache.DefaultFolder, api.PreviewAsync);
        var mainWindow = application.MainWindowHandle;
        var year = int.Parse(application.ControlledApplication.VersionNumber);
        _viewModel = new FamiliesPaneViewModel(api, new FamilyBridge(year), ConnectionStore.Shared, previews, () => ConnectionDialog.Show(mainWindow), new FamilyDialogs());
        var view = new FamiliesPaneView(_viewModel);
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
        application.ControlledApplication.DocumentClosed += OnDocumentClosed;
        ConnectionStore.Shared.Changed += (_, _) => Reload();
    }

    /// <summary>Кнопка ленты: показать панель (и обновить её) или скрыть.</summary>
    public static void Toggle(UIApplication application)
    {
        if (Failure is { } failure)
        {
            TaskDialog.Show(Title, $"Панель не запустилась: {failure}");
            return;
        }

        var pane = application.GetDockablePane(Id);
        if (pane.IsShown())
        {
            pane.Hide();
            return;
        }

        pane.Show();
        _documentKey = Key(application.ActiveUIDocument?.Document);
        Reload();
    }

    /// <summary>Другой документ стал активным — проект ли это, редактор семейства или ничего; переключение видов одного документа ничего не меняет.</summary>
    private static void OnViewActivated(object? sender, ViewActivatedEventArgs args)
    {
        var key = Key(args.Document);
        if (key == _documentKey) return;
        _documentKey = key;
        if (sender is UIApplication application && IsShown(application)) Reload();
    }

    private static void OnFrameVisibilityChanged(object? sender, DockableFrameVisibilityChangedEventArgs args)
    {
        if (args.PaneId?.Guid != Id.Guid || !args.DockableFrameShown) return;
        Reload();
    }

    private static void OnDocumentClosed(object? sender, DocumentClosedEventArgs args)
    {
        _documentKey = null;
        Reload();
    }

    private static string? Key(Autodesk.Revit.DB.Document? document)
    {
        try
        {
            return document is null ? null : $"{document.IsFamilyDocument}|{document.Title}|{document.PathName}";
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return null;
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

    /// <summary>Модель панели живёт в UI-потоке панели; несколько поводов подряд дают одно обновление.</summary>
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
