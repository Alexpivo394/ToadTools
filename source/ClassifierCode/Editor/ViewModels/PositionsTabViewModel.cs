using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassifierCode.Engine.Import;
using ClassifierCode.Engine.Library;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Editor.ViewModels;

public sealed partial class PositionRowViewModel(Position position, Action changed, Action<string, string>? renamed = null) : ObservableObject
{
    public Position Source { get; } = position;

    /// <summary>Новый код переносится во все ссылки профиля (таблица, исключения, разделы).</summary>
    public string Code
    {
        get => Source.Code;
        set
        {
            var old = Source.Code;
            var code = value.Trim();
            if (code == old) return;
            renamed?.Invoke(old, code);
            if (Source.Code == old) Source.Code = code;
            OnPropertyChanged();
            changed();
        }
    }

    public string Description
    {
        get => Source.Description;
        set
        {
            Source.Description = value;
            OnPropertyChanged();
            changed();
        }
    }

    /// <summary>Доп. колонки строкой «Ед=шт; Масса=кг» — для отображения.</summary>
    public string ExtraText => string.Join("; ", Source.Extra.Select(e => e.Key + "=" + e.Value));
}

/// <summary>Доп. колонка импорта: поле позиции и номер колонки.</summary>
public sealed partial class ExtraColumnChoice(string field, int column, Action changed) : ObservableObject
{
    public string Field { get; } = field;

    public int Column
    {
        get => column;
        set
        {
            column = value;
            OnPropertyChanged();
            changed();
        }
    }
}

/// <summary>Предпросмотр импорта: какие колонки и с какой строки.</summary>
public sealed partial class ImportPreview : ObservableObject
{
    public ImportPreview(IReadOnlyList<string[]> rows, string source, IReadOnlyList<string>? extraFields = null)
    {
        Rows = rows;
        Source = source;
        var width = rows.Count == 0 ? 0 : rows.Max(r => r.Length);
        var header = rows.Count > 0 ? rows[0] : [];
        Columns = Enumerable.Range(0, Math.Max(width, 2))
            .Select(i => ColumnName(i) + (i < header.Length && !string.IsNullOrWhiteSpace(header[i]) ? ": " + header[i] : string.Empty)).ToList();
        (_codeColumn, _descriptionColumn, _firstRow) = PositionsImport.Guess(rows);
        foreach (var field in extraFields ?? [])
        {
            var guess = Array.FindIndex(header, h => string.Equals((h ?? string.Empty).Trim(), field, StringComparison.OrdinalIgnoreCase));
            Extras.Add(new ExtraColumnChoice(field, guess, Update));
        }

        Update();
    }

    public IReadOnlyList<string[]> Rows { get; }
    public string Source { get; }
    public IReadOnlyList<string> Columns { get; }

    /// <summary>Доп. поля позиции (из «Общее»): из какой колонки их брать; -1 — не брать.</summary>
    public List<ExtraColumnChoice> Extras { get; } = [];

    [ObservableProperty] private int _codeColumn;
    [ObservableProperty] private int _descriptionColumn;
    [ObservableProperty] private int _firstRow;
    [ObservableProperty] private bool _replace = true;
    [ObservableProperty] private IReadOnlyList<Position> _sample = [];
    [ObservableProperty] private string _summary = string.Empty;

    /// <summary>Номер первой строки для людей (с 1).</summary>
    public int FirstRowNumber
    {
        get => FirstRow + 1;
        set => FirstRow = Math.Max(0, value - 1);
    }

    partial void OnCodeColumnChanged(int value) => Update();
    partial void OnDescriptionColumnChanged(int value) => Update();

    partial void OnFirstRowChanged(int value)
    {
        OnPropertyChanged(nameof(FirstRowNumber));
        Update();
    }

    public (List<Position> Positions, List<string> Skipped) Result() =>
        PositionsImport.FromRows(Rows, CodeColumn, DescriptionColumn, FirstRow,
            Extras.Where(e => e.Column >= 0).ToDictionary(e => e.Field, e => e.Column));

    private void Update()
    {
        var (positions, skipped) = Result();
        Sample = positions.Take(8).ToList();
        Summary = $"Позиций: {positions.Count}, пропущено: {skipped.Count}";
    }

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        for (index++; index > 0; index = (index - 1) / 26) name = (char)('A' + (index - 1) % 26) + name;
        return name;
    }
}

/// <summary>Вкладка «Классификатор»: позиции с поиском, импорт из Excel и буфера.</summary>
public sealed partial class PositionsTabViewModel : ObservableObject
{
    private readonly ProfileEditorViewModel _owner;
    private List<PositionRowViewModel> _all = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private IReadOnlyList<PositionRowViewModel> _visible = [];
    [ObservableProperty] private ImportPreview? _preview;

    public PositionsTabViewModel(ProfileEditorViewModel owner)
    {
        _owner = owner;
        Reload();
    }

    public bool HasPreview => Preview != null;

    partial void OnPreviewChanged(ImportPreview? value) => OnPropertyChanged(nameof(HasPreview));

    public string CountLabel => $"Позиций: {_owner.Profile.Positions.Count}";

    public void Reload()
    {
        _all = _owner.Profile.Positions.Select(p => new PositionRowViewModel(p, _owner.MarkChanged, RenameCode)).ToList();
        Filter();
    }

    /// <summary>Код позиции и все ссылки на него — разом. Если код уже занят другой позицией, переименовывается только эта.</summary>
    private void RenameCode(string oldCode, string newCode)
    {
        if (_owner.Profile.Positions.Count(p => p.Code == oldCode) == 1)
            ProfileRefactor.Rename(_owner.Profile, KeyKind.Position, oldCode, newCode);
    }

    partial void OnSearchTextChanged(string value) => Filter();

    private void Filter()
    {
        var text = SearchText.Trim();
        Visible = text.Length == 0
            ? _all
            : _all.Where(r => r.Code.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                              r.Description.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        OnPropertyChanged(nameof(CountLabel));
    }

    [RelayCommand]
    private void Add()
    {
        var position = new Position { Code = "новый код", Description = string.Empty };
        _owner.Profile.Positions.Add(position);
        SearchText = string.Empty;
        Reload();
        _owner.MarkChanged();
    }

    [RelayCommand]
    private void Delete(PositionRowViewModel? row)
    {
        if (row == null) return;
        var references = ProfileRefactor.References(_owner.Profile, KeyKind.Position, row.Code);
        if (references.Count > 0)
        {
            var list = string.Join(Environment.NewLine, references.Take(10)) + (references.Count > 10 ? Environment.NewLine + "…" : string.Empty);
            if (_owner.Dialogs.Ask("Позиция используется", $"Позиция {row.Code} используется ({references.Count}):{Environment.NewLine}{list}{Environment.NewLine}{Environment.NewLine}Удалить всё равно?") != true)
                return;
        }

        _owner.Profile.Positions.Remove(row.Source);
        Reload();
        _owner.MarkChanged();
    }

    [RelayCommand]
    private void ImportExcel()
    {
        var rows = _owner.Host.ReadXlsx();
        if (rows != null) Preview = new ImportPreview(rows, "Excel", ExtraFields());
    }

    [RelayCommand]
    private void Paste()
    {
        var rows = PositionsImport.ParseTsv(_owner.Host.ClipboardText());
        if (rows.Count == 0)
        {
            _owner.Dialogs.Info("Буфер пуст", "Скопируйте в Excel две колонки — код и описание — и нажмите «Вставить» ещё раз.");
            return;
        }

        Preview = new ImportPreview(rows, "буфера обмена", ExtraFields());
    }

    private IReadOnlyList<string> ExtraFields() =>
        _owner.Profile.Outputs.Select(o => o.Field).Where(f => f is not ("code" or "description") && f.Length > 0).Distinct().ToList();

    [RelayCommand]
    private void CancelImport() => Preview = null;

    [RelayCommand]
    private void ApplyImport()
    {
        if (Preview == null) return;
        var (positions, skipped) = Preview.Result();
        var list = _owner.Profile.Positions;
        if (Preview.Replace)
        {
            list.Clear();
            list.AddRange(positions);
        }
        else
        {
            var existing = new HashSet<string>(list.Select(p => p.Code));
            list.AddRange(positions.Where(p => !existing.Contains(p.Code)));
        }

        Preview = null;
        Reload();
        _owner.MarkChanged();
        _owner.Dialogs.Info("Импорт классификатора", $"Загружено позиций: {positions.Count}, пропущено {skipped.Count}." +
                                                     (skipped.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, skipped.Take(10)) : string.Empty));
    }
}
