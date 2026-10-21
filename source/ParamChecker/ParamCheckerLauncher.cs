using System.Windows.Interop;
using Autodesk.Revit.UI;
using ParamChecker.Services;
using ParamChecker.ViewModels;
using ParamChecker.Views;
using ToadTools.UI.Models;

namespace ParamChecker;

/// <summary>
///     Открывает окно ParamChecker. Окно модальное и открыто из команды, поэтому проверка по кнопке идёт в контексте Revit API.
/// </summary>
public static class ParamCheckerLauncher
{
    public static void Run(UIApplication application)
    {
        var store = new SettingsStore();
        var settings = store.LoadSettings();
        var profiles = store.LoadProfiles(out var error);
        // Подписи категорий и подсказки параметров берутся из открытого проекта — подойдёт и пустой.
        if (application.ActiveUIDocument?.Document is not { } document)
        {
            ToadTools.UI.Services.ToadDialogService.Show("ParamChecker", "Откройте любой проект Revit — хоть пустой: из него берутся названия категорий.",
                DialogButtons.OK, DialogIcon.Info);
            return;
        }

        var categories = new CategoryService(document);

        var windows = new WindowService();
        var context = new CheckerContext(application, settings, categories, windows);
        var view = new ParamCheckerWindow(new MainViewModel(context, store, profiles));
        windows.Owner = view;
        new WindowInteropHelper(view).Owner = application.MainWindowHandle;

        if (error is not null) view.Loaded += (_, _) => windows.Show(error, DialogButtons.OK, DialogIcon.Warning);
        view.ShowDialog();
    }
}
