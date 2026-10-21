using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ToadExporter.Core;
using ToadTools.UI.RevitServer;

namespace ToadExporter.ViewModels.Tree;

/// <summary>Дерево моделей источника с галочками RVT/NWC, поиском и счётчиками отмеченного.</summary>
public sealed partial class ModelTreeViewModel : ObservableObject
{
    [ObservableProperty] private bool _nwcEnabled = true;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private int _rvtCount;
    [ObservableProperty] private int _nwcCount;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasError))] private string? _error;
    [ObservableProperty] private bool _isLoading;

    public ObservableCollection<TreeNodeViewModel> Roots { get; } = [];

    public bool HasError => Error is not null;

    internal ITreeSource? Source { get; private set; }

    public async Task LoadAsync(ITreeSource source)
    {
        Source = source;
        Roots.Clear();
        Error = null;
        IsLoading = true;
        try
        {
            var entries = await source.GetChildrenAsync(source.RootPath, CancellationToken.None);
            // Пока шла загрузка, могли выбрать другой источник — тогда этот результат уже не нужен.
            if (!ReferenceEquals(Source, source)) return;
            foreach (var entry in entries) Roots.Add(new TreeNodeViewModel(this, null, entry));
            ApplyFilter();
        }
        catch (SourceException exception)
        {
            if (ReferenceEquals(Source, source)) Error = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(Source, source))
            {
                IsLoading = false;
                Recount();
            }
        }
    }

    /// <summary>Отметить или снять всё дерево; null — этот формат не трогать.</summary>
    public async Task SetAllAsync(bool? rvt, bool? nwc)
    {
        foreach (var root in Roots.ToList())
        {
            if (rvt is { } r) await root.SetRvtAsync(r);
            if (nwc is { } n) await root.SetNwcAsync(n);
        }
    }

    public IReadOnlyList<ModelJob> CollectJobs(bool isServer) =>
        AllNodes()
            .Where(node => !node.IsFolder && (node.Rvt == true || node.Nwc == true))
            .Select(node => new ModelJob(node.Name, node.Entry.ModelPath, isServer, node.Rvt == true, node.Nwc == true))
            .ToList();

    public IReadOnlyList<CheckedModel> CheckedModels() =>
        AllNodes()
            .Where(node => !node.IsFolder && (node.Rvt == true || node.Nwc == true))
            .Select(node => new CheckedModel(node.Entry.FolderPath, node.Entry.ModelPath, node.Rvt == true, node.Nwc == true))
            .ToList();

    /// <summary>
    ///     Заменяет отметки дерева сохранёнными: снимает всё, догружает и раскрывает папки с сохранёнными моделями
    ///     и отмечает их. Возвращает модели, которых в источнике больше нет.
    /// </summary>
    public async Task<IReadOnlyList<CheckedModel>> ApplyCheckedAsync(IReadOnlyList<CheckedModel> models)
    {
        await SetAllAsync(false, false);

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in Roots.ToList()) await ApplyCheckedAsync(root, models, found);

        Recount();
        return models.Where(model => !found.Contains(model.ModelPath)).ToList();
    }

    private async Task ApplyCheckedAsync(TreeNodeViewModel node, IReadOnlyList<CheckedModel> models, HashSet<string> found)
    {
        if (!node.IsFolder)
        {
            var model = models.FirstOrDefault(m => string.Equals(m.ModelPath, node.Entry.ModelPath, StringComparison.OrdinalIgnoreCase));
            if (model is null) return;

            found.Add(model.ModelPath);
            if (model.Rvt) node.ApplyChecked(ExportFormat.Rvt);
            if (model.Nwc && NwcEnabled) node.ApplyChecked(ExportFormat.Nwc);
            return;
        }

        if (!models.Any(model => IsWithin(model.FolderPath, node.Entry.FolderPath))) return;

        await node.EnsureLoadedAsync();
        if (!node.IsLoaded) return;

        node.IsExpanded = true;
        foreach (var child in node.Children.Where(c => !c.IsPlaceholder).ToList())
            await ApplyCheckedAsync(child, models, found);
    }

    /// <summary>Папка <paramref name="path" /> — сама <paramref name="folder" /> или вложена в неё.</summary>
    private static bool IsWithin(string path, string folder)
    {
        if (folder.Length == 0) return true;
        if (!path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) return false;
        return path.Length == folder.Length || path[folder.Length] is '/' or '\\';
    }

    internal void Recount()
    {
        var models = AllNodes().Where(node => !node.IsFolder).ToList();
        RvtCount = models.Count(node => node.Rvt == true);
        NwcCount = models.Count(node => node.Nwc == true);
    }

    partial void OnNwcEnabledChanged(bool value)
    {
        if (value) return;
        foreach (var root in Roots) root.ApplyDown(ExportFormat.Nwc, false);
        Recount();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var text = SearchText.Trim();
        foreach (var root in Roots) root.ApplyFilter(text);
    }

    private IEnumerable<TreeNodeViewModel> AllNodes() =>
        Roots.SelectMany(root => root.Descendants().Prepend(root)).Where(node => !node.IsPlaceholder);
}
