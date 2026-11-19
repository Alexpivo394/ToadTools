using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Library;
using ClassifierCode.Engine.Patterns;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Editor.ViewModels;

/// <summary>Строка подсказки по синтаксису шаблонов.</summary>
public sealed record SyntaxRow(string Pattern, string Meaning);

public sealed partial class KeyItemViewModel(KeyTitle source, KeyKind kind, Action changed) : ObservableObject
{
    public KeyTitle Source { get; } = source;
    public KeyKind Kind { get; } = kind;
    public string KindTitle => Kind == KeyKind.Discipline ? "Раздел" : "Тип";
    public string Key => Source.Key;

    public string Title
    {
        get => Source.Title;
        set
        {
            Source.Title = value;
            OnPropertyChanged();
            changed();
        }
    }

    /// <summary>Позиция раздела (для подсказок и отчёта).</summary>
    public string Position
    {
        get => Source.Position ?? string.Empty;
        set
        {
            Source.Position = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            OnPropertyChanged();
            changed();
        }
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>Набор шаблонов: по шаблону в строке.</summary>
public sealed partial class PatternSetViewModel(string name, List<string> patterns, Action changed) : ObservableObject
{
    public string Name { get; } = name;

    public string PatternsText
    {
        get => string.Join(Environment.NewLine, patterns);
        set
        {
            patterns.Clear();
            patterns.AddRange(Lines(value));
            OnPropertyChanged();
            changed();
        }
    }

    internal static IEnumerable<string> Lines(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0);
}

/// <summary>Запись словаря, которая выдаёт выбранный раздел или тип.</summary>
public sealed partial class EntryViewModel(string dictionaryName, DictionaryDef dictionary, DictionaryEntryDef entry, IReadOnlyList<PatternSetViewModel> sets,
    string resultNote, Action changed) : ObservableObject
{
    public string DictionaryName { get; } = dictionaryName;
    public DictionaryEntryDef Source { get; } = entry;

    public string ModeTitle => dictionary.Mode switch
    {
        "earliest" => "побеждает слово, стоящее в тексте раньше",
        "lastToken" => "точный шифр, ищется с конца текста",
        _ => "первая подходящая запись по порядку"
    };

    /// <summary>«для труб» / «для воздуховодов», если результат зависит от сети; условия записи.</summary>
    public string ResultNote { get; } = resultNote;

    public IReadOnlyList<PatternSetViewModel> Sets { get; } = sets;

    public string PatternsText
    {
        get => string.Join(Environment.NewLine, Source.Patterns);
        set
        {
            Source.Patterns = PatternSetViewModel.Lines(value).ToList();
            OnPropertyChanged();
            changed();
        }
    }
}

/// <summary>Вкладка «Слова и шифры» (бывшая «Разделы и типы»): ключи, их слова и шифры в словарях, живая проверка текста.</summary>
public sealed partial class DictionariesTabViewModel : ObservableObject
{
    private readonly ProfileEditorViewModel _owner;

    [ObservableProperty] private KeyItemViewModel? _selectedKey;
    [ObservableProperty] private IReadOnlyList<EntryViewModel> _entries = [];
    [ObservableProperty] private string _renameTo = string.Empty;
    [ObservableProperty] private string _testText = string.Empty;
    [ObservableProperty] private IReadOnlyList<string> _matches = [];
    [ObservableProperty] private string? _addEntryDictionary;

    public DictionariesTabViewModel(ProfileEditorViewModel owner)
    {
        _owner = owner;
        Reload();
    }

    private ClassifierProfile P => _owner.Profile;
    public ObservableCollection<KeyItemViewModel> Keys { get; } = [];
    public IReadOnlyList<string> DictionaryNames => P.Dictionaries.Keys.OrderBy(k => k).ToList();

    public static IReadOnlyList<SyntaxRow> SyntaxHelp { get; } =
    [
        new("трап", "с начала слова: «Трапы», но не «атрап» (трап* — то же)"),
        new("*изоляц", "где угодно, и внутри слова: «Теплоизоляция»"),
        new("=пк", "слово целиком"),
        new("шкаф пожарн", "слова подряд: «Шкафы пожарные»"),
        new("#В1", "шифр системы: В1, 1В1, В1.1 — но не ПВ1 и не В11"),
        new("#!В1", "шифр без цифры перед ним: не 1В1"),
        new("@набор", "все шаблоны набора"),
        new("re:…", "регулярное выражение как есть")
    ];

    public void Reload()
    {
        var key = SelectedKey?.Key;
        Keys.Clear();
        foreach (var d in P.Disciplines) Keys.Add(new KeyItemViewModel(d, KeyKind.Discipline, _owner.MarkChanged));
        foreach (var t in P.Types) Keys.Add(new KeyItemViewModel(t, KeyKind.Type, _owner.MarkChanged));
        SelectedKey = Keys.FirstOrDefault(k => k.Key == key) ?? Keys.FirstOrDefault();
        OnPropertyChanged(nameof(DictionaryNames));
    }

    partial void OnSelectedKeyChanged(KeyItemViewModel? value)
    {
        RenameTo = value?.Key ?? string.Empty;
        LoadEntries();
    }

    partial void OnTestTextChanged(string value) => Check();

    private void LoadEntries()
    {
        var key = SelectedKey;
        if (key == null)
        {
            Entries = [];
            return;
        }

        var list = new List<EntryViewModel>();
        foreach (var dictionary in P.Dictionaries.OrderBy(d => d.Key))
        foreach (var entry in dictionary.Value.Entries)
        {
            var note = ResultNote(entry.Result, key);
            if (note == null) continue;
            if (entry.When.Count > 0) note += (note.Length > 0 ? "; " : string.Empty) + "есть условия записи";
            var sets = entry.Patterns.Where(p => p.StartsWith("@")).Select(p => p.Substring(1)).Distinct()
                .Where(P.PatternSets.ContainsKey).Select(name => new PatternSetViewModel(name, P.PatternSets[name], Changed)).ToList();
            list.Add(new EntryViewModel(dictionary.Key, dictionary.Value, entry, sets, note, Changed));
        }

        Entries = list;
    }

    /// <summary>null — запись не про этот ключ; иначе пометка (для труб/воздуховодов).</summary>
    private static string? ResultNote(ResultDef result, KeyItemViewModel key)
    {
        string? Value(ResultDef r) => key.Kind == KeyKind.Discipline ? r.Discipline : r.Type;
        if (Value(result) == key.Key) return string.Empty;
        var notes = new List<string>();
        if (result.Pipe != null && Value(result.Pipe) == key.Key) notes.Add("для труб и прочего");
        if (result.Air != null && Value(result.Air) == key.Key) notes.Add("для воздуховодов");
        return notes.Count > 0 ? string.Join(", ", notes) : null;
    }

    private void Changed()
    {
        _owner.MarkChanged();
        Check();
    }

    /// <summary>Какие шаблоны совпали с проверочным текстом и где.</summary>
    private void Check()
    {
        var text = TestText;
        if (string.IsNullOrWhiteSpace(text))
        {
            Matches = [];
            return;
        }

        var found = new List<string>();
        foreach (var dictionary in P.Dictionaries.OrderBy(d => d.Key))
        {
            if (dictionary.Value.Mode == "lastToken") continue;
            var subject = dictionary.Value.Normalize ? TextSourceReader.Normalize(text) : text;
            foreach (var entry in dictionary.Value.Entries)
            foreach (var pattern in entry.Patterns)
            foreach (var (member, index) in MatchedMembers(pattern, subject, []))
                found.Add($"{dictionary.Key}: {Describe(entry.Result)} — «{member}» (позиция {index})");
        }

        Matches = found.Count > 0 ? found : ["ничего не совпало"];
    }

    /// <summary>Совпавшие шаблоны; ссылку на набор раскрывает до его шаблонов.</summary>
    private IEnumerable<(string Pattern, int Index)> MatchedMembers(string pattern, string text, HashSet<string> visiting)
    {
        if (pattern.StartsWith("@") && P.PatternSets.TryGetValue(pattern.Substring(1), out var members))
        {
            if (!visiting.Add(pattern)) yield break;
            foreach (var member in members)
            foreach (var hit in MatchedMembers(member, text, visiting))
                yield return hit;
            yield break;
        }

        System.Text.RegularExpressions.Match? match = null;
        try
        {
            match = PatternCompiler.Compile(pattern, P.PatternSets).Match(text);
        }
        catch (Exception exception) when (exception is PatternException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            // Ошибку шаблона показывает список ошибок профиля.
        }

        if (match is { Success: true }) yield return (pattern, match.Index);
    }

    private string Describe(ResultDef result)
    {
        if (result.Skip != null) return "пропустить";
        if (result.Type != null) return "тип «" + (P.Types.FirstOrDefault(t => t.Key == result.Type)?.Title ?? result.Type) + "»";
        if (result.Discipline != null) return "раздел «" + (P.Disciplines.FirstOrDefault(d => d.Key == result.Discipline)?.Title ?? result.Discipline) + "»";
        if (result.Position != null) return "позиция " + result.Position;
        var parts = new List<string>();
        if (result.Pipe != null) parts.Add("трубы: " + Describe(result.Pipe));
        if (result.Air != null) parts.Add("воздуховоды: " + Describe(result.Air));
        return string.Join(", ", parts);
    }

    private string NewKey(string prefix)
    {
        var used = new HashSet<string>(P.Disciplines.Select(d => d.Key).Concat(P.Types.Select(t => t.Key)));
        var n = 1;
        while (used.Contains(prefix + n)) n++;
        return prefix + n;
    }

    [RelayCommand]
    private void AddDiscipline()
    {
        var key = new KeyTitle { Key = NewKey("discipline"), Title = "Новый раздел" };
        P.Disciplines.Add(key);
        Reload();
        SelectedKey = Keys.First(k => k.Source == key);
        _owner.MarkChanged();
    }

    [RelayCommand]
    private void AddType()
    {
        var key = new KeyTitle { Key = NewKey("type"), Title = "Новый тип" };
        P.Types.Add(key);
        Reload();
        SelectedKey = Keys.First(k => k.Source == key);
        _owner.MarkChanged();
    }

    [RelayCommand]
    private void Rename()
    {
        var key = SelectedKey;
        var newKey = RenameTo.Trim();
        if (key == null || newKey.Length == 0 || newKey == key.Key) return;
        if (P.Disciplines.Any(d => d.Key == newKey) || P.Types.Any(t => t.Key == newKey))
        {
            _owner.Dialogs.Info("Ключ занят", $"Ключ «{newKey}» уже есть — выберите другой.");
            return;
        }

        ProfileRefactor.Rename(P, key.Kind, key.Key, newKey);
        key.Refresh();
        LoadEntries();
        _owner.MarkChanged();
    }

    [RelayCommand]
    private void Delete()
    {
        var key = SelectedKey;
        if (key == null) return;
        var references = ProfileRefactor.References(P, key.Kind, key.Key);
        if (references.Count > 0 &&
            _owner.Dialogs.Ask("Ключ используется",
                $"«{key.Title}» используется ({references.Count}):{Environment.NewLine}{string.Join(Environment.NewLine, references.Take(10))}" +
                $"{Environment.NewLine}{Environment.NewLine}Удалить всё равно? Ссылки останутся и будут показаны как ошибки.") != true)
            return;

        if (key.Kind == KeyKind.Discipline) P.Disciplines.Remove(key.Source);
        else P.Types.Remove(key.Source);
        Reload();
        _owner.MarkChanged();
    }

    [RelayCommand]
    private void AddEntry()
    {
        var key = SelectedKey;
        if (key == null || AddEntryDictionary == null || !P.Dictionaries.TryGetValue(AddEntryDictionary, out var dictionary)) return;
        dictionary.Entries.Add(new DictionaryEntryDef
        {
            Result = key.Kind == KeyKind.Discipline ? new ResultDef { Discipline = key.Key } : new ResultDef { Type = key.Key }
        });
        LoadEntries();
        _owner.MarkChanged();
    }
}
