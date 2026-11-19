using System.IO;
using ToadDesign;
using ClassifierCode.Editor.ViewModels;
using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Kit;
using ClassifierCode.Engine.Library;
using ClassifierCode.Engine.Profile;
using ClassifierCode.Models;
using ClassifierCode.Services;
using ClassifierCode.ViewModels;
using ClassifierCode.Views.Editor;
using ToadTools.UI.Models;
using ToadTools.UI.Services;

namespace ClassifierCode.Views;

public sealed partial class ClassifierCodeView
{
    private readonly ClassifierCodeViewModel _viewModel;

    public ClassifierCodeView(ClassifierCodeViewModel viewModel)
    {
        InitializeComponent();

        ThemePreference.Apply();

        _viewModel = viewModel;
        DataContext = viewModel;

        viewModel.Completed += OnCompleted;
        viewModel.ConfigureRequested += (_, issue) => OpenEditor(issue, null);
        viewModel.ImportRequested += (_, _) => ImportProfile();
        viewModel.FoldersRequested += (_, _) => EditFolders();
        viewModel.AiExportRequested += (_, _) => OpenAiExport();
    }

    private void OpenAiExport()
    {
        var kit = KitFolder.InstalledKit(Path.GetDirectoryName(typeof(ClassifierCodeView).Assembly.Location)!);
        var viewModel = new AiExportViewModel(RevitContext.UiApplication, _viewModel.Settings, _viewModel.Library, kit);
        new AiExportView(viewModel) { Owner = this }.ShowDialog();
    }

    /// <summary>Редактор выбранного профиля; после закрытия список профилей перечитывается.</summary>
    private void OpenEditor(ProfileIssue? issue, IReadOnlyList<ElementId>? inspect)
    {
        var entry = _viewModel.SelectedProfile;
        if (entry?.Profile == null)
        {
            ToadDialogService.Show("Профиль не открыт", entry?.Error ?? "Выберите профиль.", DialogButtons.OK, DialogIcon.Error);
            return;
        }

        var editor = CreateEditor(entry, _viewModel.Library, _viewModel.SelectedSection?.Key, issue, inspect, _viewModel.Settings);
        new ProfileEditorView(editor) { Owner = this }.ShowDialog();
        _viewModel.Reload(editor.SavedProfileId);
    }

    internal static ProfileEditorViewModel CreateEditor(ProfileEntry entry, ProfileLibrary library, string? mode, ProfileIssue? issue,
        IReadOnlyList<ElementId>? inspect, ClassifierSettings settings)
    {
        var host = new RevitEditorHost(RevitContext.ActiveUiDocument!);
        var editor = new ProfileEditorViewModel(entry, library, host, new ToadDialogs(),
            ProfileEditorView.Debouncer(System.Windows.Threading.Dispatcher.CurrentDispatcher),
            settings, ClassifierSettings.DefaultPath); // тот же экземпляр, что у окна запуска: его сохранение не затрёт «Все настройки»
        editor.Check.SelectMode(mode);
        if (issue != null) editor.GoToIssue(issue);
        if (inspect != null)
        {
            editor.SelectedTab = editor.Tabs.First(t => t.Content == editor.Check);
            editor.Check.Inspect(inspect.Select(RevitCompat.IdValue));
        }

        return editor;
    }

    private void ImportProfile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Импорт профиля", Filter = "Профиль классификатора (*.classifier.json)|*.classifier.json|Все файлы (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var entry = _viewModel.Library.Import(dialog.FileName, existing => ToadDialogService.Show("Профиль уже есть",
                $"Профиль «{existing.Profile!.Name}» {existing.Profile.Version} уже есть в вашей папке. Заменить его?\n\n«Нет» — импортировать отдельной копией.",
                DialogButtons.YesNo, DialogIcon.Warning) == "Yes");
            _viewModel.Reload(entry.Profile?.Id);
        }
        catch (Exception exception) when (exception is ProfileReadException or ProfileSaveException or IOException or UnauthorizedAccessException)
        {
            ToadDialogService.Show("Профиль не импортирован", exception.Message, DialogButtons.OK, DialogIcon.Error);
        }
    }

    private void EditFolders()
    {
        var view = new ProfileFoldersView(_viewModel.SharedFolders) { Owner = this };
        if (view.ShowDialog() == true) _viewModel.SetSharedFolders(view.Folders);
    }

    private void OnCompleted(object? sender, ClassifierReport report)
    {
        Close();

        var entry = _viewModel.SelectedProfile;
        var mode = _viewModel.SelectedSection?.Key;
        var library = _viewModel.Library;
        var settings = _viewModel.Settings;
        var reportViewModel = new ClassifierReportViewModel(report);
        var reportView = new ClassifierReportView(reportViewModel);
        reportViewModel.InspectRequested += (_, ids) =>
        {
            if (entry?.Profile == null) return;
            var editor = CreateEditor(entry, library, mode, null, ids, settings);
            new ProfileEditorView(editor) { Owner = reportView }.ShowDialog();
        };
        reportView.ShowDialog();
    }
}
