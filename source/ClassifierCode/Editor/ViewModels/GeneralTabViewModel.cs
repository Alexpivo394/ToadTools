using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Editor.ViewModels;

/// <summary>Строка «поле позиции → параметр».</summary>
public sealed partial class OutputRowViewModel(OutputField output, Action changed) : ObservableObject
{
    public OutputField Source { get; } = output;

    public string Field
    {
        get => Source.Field;
        set => Set(() => Source.Field = value.Trim());
    }

    public string Parameter
    {
        get => Source.Parameter;
        set => Set(() => Source.Parameter = value.Trim());
    }

    /// <summary>GUID общего параметра; пусто — поиск по имени.</summary>
    public string GuidText
    {
        get => Source.Guid?.ToString() ?? string.Empty;
        set => Set(() => Source.Guid = Guid.TryParse(value, out var guid) ? guid : null);
    }

    public bool IsFixedField => Source.Field is "code" or "description";

    public string FieldTitle => Source.Field switch
    {
        "code" => "Код",
        "description" => "Описание",
        _ => "Колонка «" + Source.Field + "»"
    };

    private void Set(Action apply)
    {
        apply();
        OnPropertyChanged(string.Empty);
        changed();
    }
}

public sealed partial class CategoryItem(string name, string title, string group, bool isChecked, Action<CategoryItem> changed) : ObservableObject
{
    public string Name { get; } = name;
    public string Title { get; } = title;
    public string Group { get; } = group;

    [ObservableProperty] private bool _isChecked = isChecked;

    partial void OnIsCheckedChanged(bool value) => changed(this);
}

/// <summary>Вкладка «Общее»: название, автовыбор, параметры записи, категории.</summary>
public sealed partial class GeneralTabViewModel : ObservableObject
{
    private readonly ProfileEditorViewModel _owner;

    public GeneralTabViewModel(ProfileEditorViewModel owner)
    {
        _owner = owner;
        ParameterNames = owner.Host.ProjectParameterNames().OrderBy(n => n).ToList();
        Reload();
    }

    private ClassifierProfile P => _owner.Profile;

    public IReadOnlyList<string> ParameterNames { get; }
    public ObservableCollection<OutputRowViewModel> Outputs { get; } = [];
    public ObservableCollection<CategoryItem> Categories { get; } = [];

    public string Name
    {
        get => P.Name;
        set => Change(() => P.Name = value);
    }

    public string Description
    {
        get => P.Description;
        set => Change(() => P.Description = value);
    }

    public string Author
    {
        get => P.Author;
        set => Change(() => P.Author = value);
    }

    public string Version
    {
        get => P.Version;
        set => Change(() => P.Version = value);
    }

    /// <summary>Шаблоны имени файла по строке.</summary>
    public string FileNamePatternsText
    {
        get => string.Join(Environment.NewLine, P.FileNamePatterns);
        set => Change(() => P.FileNamePatterns = value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList());
    }

    /// <summary>Перечитать поля из профиля (после замены рабочей копии).</summary>
    public void Reload()
    {
        Outputs.Clear();
        foreach (var output in P.Outputs) Outputs.Add(new OutputRowViewModel(output, _owner.MarkChanged));

        Categories.Clear();
        var known = new HashSet<string>(CategoryCatalog.All.Select(c => c.Name));
        foreach (var (name, title, group) in CategoryCatalog.All)
            Categories.Add(new CategoryItem(name, title, group, P.Categories.Contains(name), OnCategoryChanged));
        foreach (var name in P.Categories.Where(c => !known.Contains(c)))
            Categories.Add(new CategoryItem(name, name, "Прочие", true, OnCategoryChanged));
        OnPropertyChanged(string.Empty);
    }

    private void OnCategoryChanged(CategoryItem item)
    {
        if (item.IsChecked && !P.Categories.Contains(item.Name)) P.Categories.Add(item.Name);
        if (!item.IsChecked) P.Categories.Remove(item.Name);
        _owner.MarkChanged();
    }

    [RelayCommand]
    private void AddOutput()
    {
        var output = new OutputField { Field = "Колонка " + (P.Outputs.Count + 1) };
        P.Outputs.Add(output);
        Outputs.Add(new OutputRowViewModel(output, _owner.MarkChanged));
        _owner.MarkChanged();
    }

    [RelayCommand]
    private void RemoveOutput(OutputRowViewModel? row)
    {
        if (row == null || row.IsFixedField) return;
        P.Outputs.Remove(row.Source);
        Outputs.Remove(row);
        _owner.MarkChanged();
    }

    private void Change(Action apply)
    {
        apply();
        OnPropertyChanged(string.Empty);
        _owner.MarkChanged();
    }
}

/// <summary>Категории, которые обычно кодируются, с русскими названиями и группами.</summary>
public static class CategoryCatalog
{
    public static readonly IReadOnlyList<(string Name, string Title, string Group)> All =
    [
        ("OST_PipeCurves", "Трубы", "Трубопроводы"),
        ("OST_FlexPipeCurves", "Гибкие трубы", "Трубопроводы"),
        ("OST_PipeFitting", "Соединительные детали трубопроводов", "Трубопроводы"),
        ("OST_PipeAccessory", "Арматура трубопроводов", "Трубопроводы"),
        ("OST_PipeInsulations", "Материалы изоляции труб", "Трубопроводы"),
        ("OST_PlumbingFixtures", "Сантехнические приборы", "Трубопроводы"),
        ("OST_Sprinklers", "Спринклеры", "Трубопроводы"),
        ("OST_DuctCurves", "Воздуховоды", "Воздуховоды"),
        ("OST_FlexDuctCurves", "Гибкие воздуховоды", "Воздуховоды"),
        ("OST_DuctFitting", "Соединительные детали воздуховодов", "Воздуховоды"),
        ("OST_DuctAccessory", "Арматура воздуховодов", "Воздуховоды"),
        ("OST_DuctTerminal", "Воздухораспределители", "Воздуховоды"),
        ("OST_DuctInsulations", "Материалы изоляции воздуховодов", "Воздуховоды"),
        ("OST_DuctLinings", "Внутренняя изоляция воздуховодов", "Воздуховоды"),
        ("OST_MechanicalEquipment", "Оборудование", "Оборудование"),
        ("OST_ElectricalEquipment", "Электрооборудование", "Оборудование"),
        ("OST_SpecialityEquipment", "Специальное оборудование", "Оборудование"),
        ("OST_Casework", "Шкафы", "Оборудование"),
        ("OST_CableTray", "Кабельные лотки", "Кабеленесущие системы"),
        ("OST_CableTrayFitting", "Соединительные детали кабельных лотков", "Кабеленесущие системы"),
        ("OST_Conduit", "Короба", "Кабеленесущие системы"),
        ("OST_ConduitFitting", "Соединительные детали коробов", "Кабеленесущие системы"),
        ("OST_ElectricalFixtures", "Электрические приборы", "Электрика и освещение"),
        ("OST_LightingFixtures", "Осветительные приборы", "Электрика и освещение"),
        ("OST_LightingDevices", "Выключатели", "Электрика и освещение"),
        ("OST_Wire", "Провода", "Электрика и освещение"),
        ("OST_FireAlarmDevices", "Пожарная сигнализация", "Слаботочные системы"),
        ("OST_SecurityDevices", "Охранная сигнализация", "Слаботочные системы"),
        ("OST_NurseCallDevices", "Вызов медсестры", "Слаботочные системы"),
        ("OST_TelephoneDevices", "Телефонные устройства", "Слаботочные системы"),
        ("OST_CommunicationDevices", "Устройства связи", "Слаботочные системы"),
        ("OST_DataDevices", "Устройства передачи данных", "Слаботочные системы"),
        ("OST_GenericModel", "Обобщённые модели", "Прочие"),
        ("OST_Furniture", "Мебель", "Прочие")
    ];
}
