using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ToadExporter.Core;
using ToadTools.UI.RevitServer;

namespace ToadExporter.ViewModels.Tree;

/// <summary>
///     Папка или модель в дереве. Галочки RVT/NWC у папки — сводка по содержимому: true — отмечено всё,
///     false — ничего, null — частично. Отметка папки сначала догружает всё её содержимое.
/// </summary>
public sealed partial class TreeNodeViewModel : ObservableObject
{
    private readonly ModelTreeViewModel _owner;
    private readonly TreeNodeViewModel? _parent;
    private Task? _loading;
    private bool? _rvt = false;
    private bool? _nwc = false;

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isVisible = true;

    internal TreeNodeViewModel(ModelTreeViewModel owner, TreeNodeViewModel? parent, SourceEntry entry)
    {
        _owner = owner;
        _parent = parent;
        Entry = entry;
        if (entry.IsFolder) Children.Add(new TreeNodeViewModel(owner, this));
    }

    /// <summary>Узел «Загрузка…».</summary>
    private TreeNodeViewModel(ModelTreeViewModel owner, TreeNodeViewModel parent)
    {
        _owner = owner;
        _parent = parent;
        Entry = new SourceEntry("Загрузка…", "", "", false);
        IsPlaceholder = true;
    }

    public SourceEntry Entry { get; }

    public string Name => Entry.Name;

    public bool IsFolder => Entry.IsFolder;

    /// <summary>Уровень вложенности — для отступа в дереве (колонки галочек при этом остаются ровными).</summary>
    public int Depth => _parent is null ? 0 : _parent.Depth + 1;

    /// <summary>Временный узел «Загрузка…», чтобы у незагруженной папки была стрелка раскрытия.</summary>
    public bool IsPlaceholder { get; }

    public bool IsLoaded { get; private set; }

    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    public bool? Rvt
    {
        get => _rvt;
        set => _ = SetRvtAsync(Toggled(_rvt, value));
    }

    public bool? Nwc
    {
        get => _nwc;
        set => _ = SetNwcAsync(Toggled(_nwc, value));
    }

    /// <summary>Клик по «частично» отмечает всё: чекбокс без третьего состояния прислал бы false.</summary>
    private static bool Toggled(bool? current, bool? clicked) => current is null || clicked == true;

    public Task SetRvtAsync(bool value) => SetAsync(ExportFormat.Rvt, value);

    public Task SetNwcAsync(bool value) => SetAsync(ExportFormat.Nwc, value);

    /// <summary>Загружает содержимое папки один раз; повторные вызовы ждут ту же загрузку.</summary>
    public Task EnsureLoadedAsync()
    {
        if (!IsFolder || IsLoaded) return Task.CompletedTask;
        return _loading ??= LoadAsync();
    }

    internal async Task EnsureLoadedRecursiveAsync()
    {
        await EnsureLoadedAsync();
        foreach (var child in Children.Where(c => c.IsFolder).ToList())
            await child.EnsureLoadedRecursiveAsync();
    }

    internal IEnumerable<TreeNodeViewModel> Descendants() =>
        Children.Where(c => !c.IsPlaceholder).SelectMany(c => c.Descendants().Prepend(c));

    internal bool? Get(ExportFormat format) => format == ExportFormat.Rvt ? _rvt : _nwc;

    /// <summary>
    ///     Ставит значение себе и всему загруженному содержимому, без пересчёта родителей. Незагруженную папку
    ///     не отмечает: что в ней, неизвестно, и выгружено из неё ничего не будет.
    /// </summary>
    internal void ApplyDown(ExportFormat format, bool value)
    {
        if (value && IsFolder && !IsLoaded) return;

        Store(format, value);
        foreach (var child in Children.Where(c => !c.IsPlaceholder))
            child.ApplyDown(format, value);
    }

    /// <summary>Отмечает модель и пересчитывает сводку папок над ней.</summary>
    internal void ApplyChecked(ExportFormat format)
    {
        Store(format, true);
        _parent?.RecomputeUp(format);
    }

    /// <summary>Пересчитывает сводку папки по детям и передаёт пересчёт вверх.</summary>
    internal void RecomputeUp(ExportFormat format)
    {
        var values = Children.Where(c => !c.IsPlaceholder).Select(c => c.Get(format)).Distinct().ToList();
        if (values.Count > 0) Store(format, values.Count == 1 ? values[0] : null);
        _parent?.RecomputeUp(format);
    }

    /// <summary>Видимость по поиску; возвращает, виден ли узел.</summary>
    internal bool ApplyFilter(string text)
    {
        if (IsPlaceholder) return true;

        var childVisible = false;
        foreach (var child in Children.Where(c => !c.IsPlaceholder))
            childVisible |= child.ApplyFilter(text);

        if (text.Length == 0)
            IsVisible = true;
        else if (IsFolder)
            IsVisible = childVisible;
        else
            IsVisible = Name.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0;

        if (text.Length > 0 && IsFolder && IsVisible) IsExpanded = true;
        return IsVisible;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value) _ = EnsureLoadedAsync();
    }

    private async Task SetAsync(ExportFormat format, bool value)
    {
        if (format == ExportFormat.Nwc && value && !_owner.NwcEnabled)
        {
            OnPropertyChanged(nameof(Nwc)); // вернуть чекбокс в прежнее состояние
            return;
        }

        if (IsFolder && value) await EnsureLoadedRecursiveAsync();

        ApplyDown(format, value);
        if (IsFolder && IsLoaded)
            RecomputeUp(format); // часть вложенных папок могла не загрузиться — сводка по тому, что отмечено
        else
            _parent?.RecomputeUp(format);

        OnPropertyChanged(format == ExportFormat.Rvt ? nameof(Rvt) : nameof(Nwc)); // чекбокс — по фактическому значению
        _owner.Recount();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var entries = await _owner.Source!.GetChildrenAsync(Entry.FolderPath, CancellationToken.None);
            Children.Clear();
            foreach (var entry in entries)
            {
                var child = new TreeNodeViewModel(_owner, this, entry);
                // Содержимое отмеченной (или снятой) целиком папки наследует её значение.
                if (_rvt is { } rvt) child.ApplyDown(ExportFormat.Rvt, rvt);
                if (_nwc is { } nwc) child.ApplyDown(ExportFormat.Nwc, nwc);
                if (_owner.SearchText.Length > 0) child.ApplyFilter(_owner.SearchText.Trim());
                Children.Add(child);
            }

            IsLoaded = true;
        }
        catch (SourceException exception)
        {
            _owner.Error = exception.Message;
            _loading = null; // можно попробовать ещё раз, раскрыв папку снова
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Store(ExportFormat format, bool? value)
    {
        if (format == ExportFormat.Rvt)
            SetProperty(ref _rvt, value, nameof(Rvt));
        else
            SetProperty(ref _nwc, value, nameof(Nwc));
    }
}

public enum ExportFormat
{
    Rvt,
    Nwc
}
