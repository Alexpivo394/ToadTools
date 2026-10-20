using System.Windows.Interop;
using Autodesk.Revit.UI;
using ToadExporter.Services;
using ToadExporter.ViewModels;
using ToadExporter.Views;

namespace ToadExporter;

/// <summary>
///     Открывает окно выгрузки. Окно модальное и открыто из команды, поэтому выгрузка по кнопке идёт в контексте Revit API.
/// </summary>
public static class ToadExporterLauncher
{
    public static void Run(UIApplication application)
    {
        var viewModel = new ToadExporterViewModel(application, new SettingsStore());
        var view = new ToadExporterView(viewModel);
        new WindowInteropHelper(view).Owner = application.MainWindowHandle;
        view.ShowDialog();
    }
}
