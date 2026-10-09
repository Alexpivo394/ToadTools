using ClassifierCode.Models;
using ClassifierCode.Services;
using ToadTools.UI.Models;
using ToadTools.UI.Services;
using Wpf.Ui.Appearance;

namespace ClassifierCode.ViewModels;

public sealed partial class ClassifierCodeViewModel : ObservableObject
{
    private readonly ClassifierCodeService _service;

    [ObservableProperty] private bool _darkTheme = true;
    [ObservableProperty] private SectionOption? _selectedSection;
    [ObservableProperty] private RunScope _scope;
    [ObservableProperty] private bool _keepExisting;

    /// <summary>Окно нужно закрыть, заполнение выполнено — показать отчёт.</summary>
    public event EventHandler<ClassifierReport>? Completed;

    public ClassifierCodeViewModel(ClassifierCodeService service)
    {
        _service = service;

        var uiDocument = RevitContext.ActiveUiDocument!;
        DocumentTitle = uiDocument.Document.Title;
        SelectedCount = uiDocument.Selection.GetElementIds().Count;

        var detected = ClassifierCodeService.DetectSection(DocumentTitle);
        IsSectionDetected = detected != Discipline.None;
        SelectedSection = Sections.FirstOrDefault(s => s.Section == detected) ?? Sections[0];
        Scope = SelectedCount > 0 ? RunScope.Selection : RunScope.Model;
    }

    public string DocumentTitle { get; }
    public IReadOnlyList<SectionOption> Sections => SectionOption.All;

    /// <summary>Раздел предложен по шифру в имени файла.</summary>
    public bool IsSectionDetected { get; }

    public int SelectedCount { get; }
    public bool HasSelection => SelectedCount > 0;
    public string SelectionLabel => $"Выбранные элементы ({SelectedCount})";

    partial void OnDarkThemeChanged(bool value)
    {
        ThemeWatcherService.ApplyTheme(value ? ApplicationTheme.Dark : ApplicationTheme.Light);
    }

    [RelayCommand]
    private void Start()
    {
        if (SelectedSection == null)
        {
            ToadDialogService.Show("Ошибка!", "Выберите раздел модели", DialogButtons.OK, DialogIcon.Error);
            return;
        }

        ClassifierReport? report;
        try
        {
            report = _service.Run(SelectedSection, Scope, KeepExisting);
        }
        catch (Exception ex)
        {
            ToadDialogService.Show("Ошибка!", "Не удалось заполнить код по классификатору:\n" + ex.Message,
                DialogButtons.OK, DialogIcon.Error);
            return;
        }

        // Пользователь отказался добавлять параметры — остаёмся в окне.
        if (report == null)
            return;

        if (report.Total == 0)
        {
            ToadDialogService.Show(
                "Нет элементов",
                $"Не найдено элементов с параметром \"{ClassifierParameters.Code}\".",
                DialogButtons.OK,
                DialogIcon.Warning);
            return;
        }

        Completed?.Invoke(this, report);
    }
}
