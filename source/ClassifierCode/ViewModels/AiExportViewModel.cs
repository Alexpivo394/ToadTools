using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Alexpivo394.Revit.Export.Sources;
using ClassifierCode.Engine.Kit;
using ClassifierCode.Engine.Library;
using ClassifierCode.Engine.Profile;
using ClassifierCode.Services;
using ToadExporter.Services;
using ToadExporter.ViewModels.Tree;
using ToadTools.UI.Models;
using ToadTools.UI.Services;
using UIApplication = Autodesk.Revit.UI.UIApplication;

namespace ClassifierCode.ViewModels;

/// <summary>
///     «Выгрузка для ИИ»: модели объекта (Revit Server, папка или открытая модель) → рабочая папка с набором для агента.
///     Окно модальное, поэтому выгрузка по кнопке идёт в контексте Revit API.
/// </summary>
public sealed partial class AiExportViewModel : ObservableObject
{
    private const string Title = "Выгрузка для ИИ";

    private readonly UIApplication _application;
    private readonly ClassifierSettings _settings;
    private readonly ProfileLibrary _library;
    private readonly string _kitSource;
    private bool _folderEdited;

    [ObservableProperty] private string _source;
    [ObservableProperty] private string _server;
    [ObservableProperty] private string _sourceFolder;
    [ObservableProperty] private string _objectName;
    [ObservableProperty] private string _folder;
    [ObservableProperty] private ProfileEntry? _baseProfile;
    [ObservableProperty] private string? _lastFolder;

    public AiExportViewModel(UIApplication application, ClassifierSettings settings, ProfileLibrary library, string kitSource)
    {
        _application = application;
        _settings = settings;
        _library = library;
        _kitSource = kitSource;
        RevitYear = application.Application.VersionNumber;
        CurrentTitle = application.ActiveUIDocument?.Document.Title ?? string.Empty;

        var saved = settings.AiExport;
        _source = saved.Source is "server" or "folder" or "current" ? saved.Source : "server";
        _server = saved.Server;
        _sourceFolder = saved.SourceFolder;
        _objectName = saved.ObjectName;
        _folder = saved.Folder.Length > 0 ? saved.Folder : KitFolder.DefaultFolder(saved.ObjectName);
        _folderEdited = saved.Folder.Length > 0 && saved.Folder != KitFolder.DefaultFolder(saved.ObjectName);

        foreach (var server in RsnConfig.ReadServers(RsnConfig.DefaultPath(RevitYear))) Servers.Add(server);
        if (_server.Length == 0 && Servers.Count > 0) _server = Servers[0];

        Profiles = library.List().Where(p => p.Profile != null).ToList();
        _baseProfile = Profiles.FirstOrDefault(p => p.Profile!.Id == saved.BaseProfileId) ?? Profiles.FirstOrDefault();

        Tree.NwcEnabled = false;
        Tree.PropertyChanged += OnTreeChanged;
        _ = ReloadAsync();
    }

    public ModelTreeViewModel Tree { get; } = new();
    public List<string> Servers { get; } = [];
    public IReadOnlyList<ProfileEntry> Profiles { get; }
    public string RevitYear { get; }
    public string CurrentTitle { get; }
    public string ServerVersionText => $"Revit Server {RevitYear}";

    public bool IsServerMode
    {
        get => Source == "server";
        set { if (value) Source = "server"; }
    }

    public bool IsFolderMode
    {
        get => Source == "folder";
        set { if (value) Source = "folder"; }
    }

    public bool IsCurrentMode
    {
        get => Source == "current";
        set { if (value) Source = "current"; }
    }

    public bool ShowsTree => !IsCurrentMode;
    public int ModelCount => IsCurrentMode ? (CurrentTitle.Length > 0 ? 1 : 0) : Tree.CollectJobs(IsServerMode).Count;
    public string ExportText => ModelCount == 0 ? "Выгрузить" : $"Выгрузить ({ModelCount})";
    public bool HasModels => Tree.Roots.Count > 0;
    public bool HasLastFolder => LastFolder != null;

    public string EmptyText => Tree.IsLoading
        ? "Загрузка…"
        : Tree.HasError
            ? ""
            : IsServerMode
                ? Server.Trim().Length == 0 ? "Выберите сервер" : "На сервере нет моделей"
                : SourceFolder.Trim().Length == 0 ? "Выберите папку с моделями" : "В папке нет моделей .rvt";

    partial void OnSourceChanged(string value)
    {
        OnPropertyChanged(nameof(IsServerMode));
        OnPropertyChanged(nameof(IsFolderMode));
        OnPropertyChanged(nameof(IsCurrentMode));
        OnPropertyChanged(nameof(ShowsTree));
        _ = ReloadAsync();
    }

    partial void OnServerChanged(string value) => _ = ReloadAsync();
    partial void OnSourceFolderChanged(string value) => _ = ReloadAsync();
    partial void OnLastFolderChanged(string? value) => OnPropertyChanged(nameof(HasLastFolder));

    /// <summary>Пока папку не меняли руками, она следует за названием объекта.</summary>
    partial void OnObjectNameChanged(string value)
    {
        if (!_folderEdited) Folder = KitFolder.DefaultFolder(value);
    }

    partial void OnFolderChanged(string value) => _folderEdited = value != KitFolder.DefaultFolder(ObjectName);

    [RelayCommand]
    private Task Reload() => ReloadAsync();

    [RelayCommand]
    private void BrowseSource()
    {
        var folder = FolderPicker.PickFolder(SourceFolder, "Папка с моделями");
        if (folder != null) SourceFolder = folder;
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var folder = FolderPicker.PickFolder(Directory.Exists(Folder) ? Folder : null, "Рабочая папка объекта");
        if (folder != null) Folder = folder;
    }

    [RelayCommand]
    private Task SelectAll() => Tree.SetAllAsync(true, null);

    [RelayCommand]
    private Task ClearAll() => Tree.SetAllAsync(false, false);

    [RelayCommand]
    private void OpenLastFolder()
    {
        if (LastFolder != null) Open(LastFolder);
    }

    [RelayCommand]
    private void Export()
    {
        var errors = new List<string>();
        if (ObjectName.Trim().Length == 0) errors.Add("Укажите название объекта.");
        if (Folder.Trim().Length == 0) errors.Add("Укажите рабочую папку.");
        if (BaseProfile?.Profile == null) errors.Add("Выберите профиль-основу.");
        if (ModelCount == 0) errors.Add(IsCurrentMode ? "Нет открытой модели." : "Отметьте модели для выгрузки.");
        if (errors.Count > 0)
        {
            ToadDialogService.Show(Title, string.Join("\n", errors), DialogButtons.OK, DialogIcon.Warning);
            return;
        }

        string folder;
        try
        {
            folder = Path.GetFullPath(Folder.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            ToadDialogService.Show(Title, $"Рабочая папка указана неверно: {exception.Message}", DialogButtons.OK, DialogIcon.Warning);
            return;
        }

        SaveState();
        var job = new AiExportJob(ObjectName.Trim(), folder, BaseProfile!.Profile!, IsCurrentMode ? [] : Tree.CollectJobs(IsServerMode),
            IsCurrentMode, _kitSource, FindProfile);

        AiExportSummary summary;
        try
        {
            summary = new AiExportRunner(_application).Run(job);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ToadDialogService.Show(Title, $"Выгрузка не выполнена: {exception.Message}", DialogButtons.OK, DialogIcon.Error);
            return;
        }

        LastFolder = folder;
        var text = $"Выгружено моделей: {summary.Exported.Count}";
        if (summary.Skipped > 0) text += $"\nПропущено: {summary.Skipped}";
        if (summary.Failed.Count > 0)
            text += $"\nС ошибкой: {summary.Failed.Count}\n" + string.Join("\n", summary.Failed.Take(8).Select(f => $"• {f.Model}: {f.Error}"));
        if (summary.Cancelled) text = "Выгрузка прервана.\n\n" + text;
        text += "\n\nДальше: положите в папку таблицу кодов заказчика (.xlsx) и запустите в ней Claude Code — подробности в README_КООРДИНАТОРУ.txt." +
                "\n\nОткрыть папку?";
        var icon = summary.Failed.Count > 0 ? DialogIcon.Warning : DialogIcon.Info;
        if (ToadDialogService.Show(Title, text, DialogButtons.YesNo, icon) == "Yes") Open(folder);
    }

    private ClassifierProfile? FindProfile(ModelBinding binding)
    {
        return KitFolder.FindFilledBy(_library.List().Where(p => p.Profile != null).Select(p => p.Profile!), binding);
    }

    public void SaveState()
    {
        _settings.AiExport = new AiExportSettings
        {
            Source = Source, Server = Server.Trim(), SourceFolder = SourceFolder.Trim(), ObjectName = ObjectName.Trim(),
            Folder = _folderEdited ? Folder.Trim() : string.Empty, BaseProfileId = BaseProfile?.Profile?.Id
        };
        _settings.Save(ClassifierSettings.DefaultPath);
    }

    private async Task ReloadAsync()
    {
        if (IsServerMode && Server.Trim().Length > 0) await Tree.LoadAsync(new RevitServerSource(Server.Trim(), RevitYear));
        else if (IsFolderMode && SourceFolder.Trim().Length > 0) await Tree.LoadAsync(new FolderSource(SourceFolder.Trim()));
        else
        {
            Tree.Roots.Clear();
            Tree.Error = null;
        }

        Refresh();
    }

    private void OnTreeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ModelTreeViewModel.RvtCount) or nameof(ModelTreeViewModel.IsLoading)) Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(ModelCount));
        OnPropertyChanged(nameof(ExportText));
        OnPropertyChanged(nameof(HasModels));
        OnPropertyChanged(nameof(EmptyText));
    }

    private static void Open(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or IOException)
        {
            ToadDialogService.Show(Title, $"Папка не открыта: {exception.Message}\n{folder}", DialogButtons.OK, DialogIcon.Warning);
        }
    }
}
