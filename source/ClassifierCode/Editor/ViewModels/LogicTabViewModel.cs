using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Editor.ViewModels;

/// <summary>Правила режима в JSON: набор правил типов (общий для режимов, которые его берут) и правила раздела режима.</summary>
public sealed class LogicFragment
{
    public List<RuleDef> TypeRules { get; set; } = [];
    public List<RuleDef> DisciplineRules { get; set; } = [];
}

/// <summary>Вкладка «Логика»: порядок шагов распознавания — читаемое описание и правка JSON с проверкой.</summary>
public sealed partial class LogicTabViewModel : ObservableObject
{
    private readonly ProfileEditorViewModel _owner;

    [ObservableProperty] private ModeDef? _selectedMode;
    [ObservableProperty] private string _jsonText = string.Empty;
    [ObservableProperty] private string? _applyError;
    [ObservableProperty] private IReadOnlyList<string> _typeRulesDescription = [];
    [ObservableProperty] private IReadOnlyList<string> _disciplineRulesDescription = [];
    [ObservableProperty] private string? _sharedNote;

    public LogicTabViewModel(ProfileEditorViewModel owner)
    {
        _owner = owner;
        SelectedMode = Modes.FirstOrDefault();
    }

    private ClassifierProfile P => _owner.Profile;
    public IReadOnlyList<ModeDef> Modes => P.Modes;

    partial void OnSelectedModeChanged(ModeDef? value) => Load();

    private void Load()
    {
        ApplyError = null;
        var mode = SelectedMode;
        if (mode == null)
        {
            JsonText = string.Empty;
            TypeRulesDescription = [];
            DisciplineRulesDescription = [];
            return;
        }

        var typeRules = P.RuleSets.TryGetValue(mode.TypeRules, out var rules) ? rules : [];
        JsonText = ProfileSerializer.WriteFragment(new LogicFragment { TypeRules = typeRules, DisciplineRules = mode.DisciplineRules });
        TypeRulesDescription = typeRules.SelectMany(r => RuleDescriber.Describe(r, P, isTypeStage: true)).ToList();
        DisciplineRulesDescription = mode.DisciplineRules.SelectMany(r => RuleDescriber.Describe(r, P, isTypeStage: false))
            .Concat([mode.DefaultDiscipline != null
                ? "Ни одно правило не дало раздел → «" + (P.Disciplines.FirstOrDefault(d => d.Key == mode.DefaultDiscipline)?.Title ?? mode.DefaultDiscipline) + "»"
                : "Ни одно правило не дало раздел → не классифицировано"]).ToList();
        var users = P.Modes.Where(m => m != mode && m.TypeRules == mode.TypeRules).Select(m => m.Title).ToList();
        SharedNote = users.Count > 0 ? $"Правила типов «{mode.TypeRules}» общие с режимами: {string.Join(", ", users)}." : null;
    }

    [RelayCommand]
    private void Reset() => Load();

    /// <summary>Применить JSON: не применяется, если он не читается или правила ссылаются на то, чего нет.</summary>
    [RelayCommand]
    private void Apply()
    {
        var mode = SelectedMode;
        if (mode == null) return;

        LogicFragment fragment;
        try
        {
            fragment = ProfileSerializer.ReadFragment<LogicFragment>(JsonText);
        }
        catch (ProfileReadException exception)
        {
            ApplyError = exception.Message;
            return;
        }

        var trial = ProfileSerializer.Clone(P);
        var trialMode = trial.Modes[P.Modes.IndexOf(mode)];
        trial.RuleSets[trialMode.TypeRules] = fragment.TypeRules;
        trialMode.DisciplineRules = fragment.DisciplineRules;
        var (_, issues) = ProfileCompiler.Compile(trial);
        var own = issues.Where(i => i.Path.StartsWith("Наборы правил › " + mode.TypeRules) ||
                                    i.Path.StartsWith("Режимы › " + mode.Key + " › правила раздела")).ToList();
        if (own.Count > 0)
        {
            ApplyError = string.Join(Environment.NewLine, own.Take(10));
            return;
        }

        P.RuleSets[mode.TypeRules] = fragment.TypeRules;
        mode.DisciplineRules = fragment.DisciplineRules;
        _owner.MarkChanged();
        Load();
    }
}
