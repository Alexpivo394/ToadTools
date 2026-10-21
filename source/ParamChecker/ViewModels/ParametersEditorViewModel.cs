using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ParamChecker.ViewModels;

/// <summary>
///     Окно параметров правила. Имена подсказываются из ФОП, открытого проекта и системных параметров — модели для этого
///     открывать не нужно. Список можно вставить из буфера: столбец из Excel или строки из текста.
/// </summary>
public sealed partial class ParametersEditorViewModel : ObservableObject
{
    private static readonly char[] Separators = ['\r', '\n', '\t', ';'];

    private readonly CheckerContext _context;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AddCommand))] private string _input = "";

    public ParametersEditorViewModel(CheckerContext context, string ruleTitle, IEnumerable<string> parameters)
    {
        _context = context;
        Title = ruleTitle.Trim().Length == 0 ? "Параметры правила" : $"Параметры правила «{ruleTitle.Trim()}»";
        foreach (var name in parameters) Parameters.Add(new ParameterItemViewModel(this, name, !context.Parameters.IsSuspicious(name)));
    }

    public string Title { get; }

    public ObservableCollection<ParameterItemViewModel> Parameters { get; } = [];

    public IReadOnlyList<string> ParameterNames => _context.Parameters.Names;

    public List<string> ToModel() => Parameters.Select(p => p.Name).ToList();

    private bool CanAdd() => Input.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        AddNames(Input);
        Input = "";
    }

    [RelayCommand]
    private void PasteList()
    {
        string? text = null;
        try
        {
            if (Clipboard.ContainsText()) text = Clipboard.GetText();
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            _context.Windows.Show("Буфер обмена пуст. Скопируйте столбец имён из Excel или строки из текста — каждое имя с новой строки.",
                ToadTools.UI.Models.DialogButtons.OK, ToadTools.UI.Models.DialogIcon.Info);
            return;
        }

        AddNames(text!);
    }

    [RelayCommand]
    private void Clear() => Parameters.Clear();

    internal void Move(ParameterItemViewModel item, int offset)
    {
        var index = Parameters.IndexOf(item);
        var target = index + offset;
        if (target >= 0 && target < Parameters.Count) Parameters.Move(index, target);
    }

    internal void Remove(ParameterItemViewModel item) => Parameters.Remove(item);

    private void AddNames(string text)
    {
        var existing = new HashSet<string>(Parameters.Select(p => p.Name), StringComparer.CurrentCultureIgnoreCase);
        foreach (var name in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()))
        {
            if (name.Length > 0 && existing.Add(name)) Parameters.Add(new ParameterItemViewModel(this, name, !_context.Parameters.IsSuspicious(name)));
        }
    }
}

public sealed partial class ParameterItemViewModel(ParametersEditorViewModel owner, string name, bool isKnown) : ObservableObject
{
    public string Name { get; } = name;

    /// <summary>Имя есть в ФОП, проекте или среди системных параметров — или сверять не с чем.</summary>
    public bool IsKnown { get; } = isKnown;

    [RelayCommand]
    private void MoveUp() => owner.Move(this, -1);

    [RelayCommand]
    private void MoveDown() => owner.Move(this, 1);

    [RelayCommand]
    private void Remove() => owner.Remove(this);
}
