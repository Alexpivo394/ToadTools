#nullable enable
using System.Collections.ObjectModel;
using System.ComponentModel;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ParamChecker.Models.Filters;
using ParamChecker.Services;
using ParamChecker.ViewModels.Conditions;

namespace ParamChecker.ViewModels.Windows;

public partial class FilterConfigViewModel : ObservableObject
{
    private readonly CategoryService _categoryService;

    [ObservableProperty] private ObservableCollection<CategoryFilterItem> _categories = [];

    [ObservableProperty] private string _categoryFilter = "";

    [ObservableProperty] private ObservableCollection<CategoryFilterItem> _filteredCategories = [];

    [ObservableProperty] private CategoryParameterLogic _selectedItemCatOrPar;

    [ObservableProperty] private FilterParameterLogic _selectedItemParOrPar;

    [ObservableProperty] private FilterConfigModel configModel = new();

    [ObservableProperty] private ObservableCollection<ConditionViewModelBase> filterConditions = [];

    public FilterConfigViewModel(CategoryService categoryService)
    {
        _categoryService = categoryService;
        
        SelectedItemCatOrPar = CategoryParameterLogic.CategoriesAndParameters;
        SelectedItemParOrPar = FilterParameterLogic.And;

        PropertyChanged += OnPropertyChanged;
    }

    public Action<string>? OnApplyRequested { get; set; }

    public IEnumerable<CategoryParameterLogic> ItemsCatOrPar =>
        Enum.GetValues(typeof(CategoryParameterLogic)).Cast<CategoryParameterLogic>();

    public IEnumerable<FilterParameterLogic> ItemsParOrPar =>
        Enum.GetValues(typeof(FilterParameterLogic)).Cast<FilterParameterLogic>();

    public void Initialize()
    {
        LoadCategories();
    }

    private void LoadCategories()
    {
        Categories.Clear();
        foreach (var category in _categoryService.GetAllCategories())
            Categories.Add(new CategoryFilterItem
            {
                CatName = _categoryService.GetCategoryName(category),
                BuiltInCategory = category,
                CatIsSelected = false
            });

        UpdateFilteredCategories();
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CategoryFilter)) UpdateFilteredCategories();
    }

    private void UpdateFilteredCategories()
    {
        if (string.IsNullOrWhiteSpace(CategoryFilter))
        {
            FilteredCategories = new ObservableCollection<CategoryFilterItem>(Categories);
            return;
        }

        var filtered = Categories.Where(c =>
#if REVIT2025_OR_GREATER
        c.CatName != null && c.CatName.Contains(CategoryFilter, StringComparison.OrdinalIgnoreCase)
#else
                    c.CatName != null && c.CatName.IndexOf(CategoryFilter, StringComparison.OrdinalIgnoreCase) >= 0
#endif
            )
            .ToList();

        FilteredCategories = new ObservableCollection<CategoryFilterItem>(filtered);
    }

    public string GetResultJson()
    {
        var model = new FilterConfigModel
        {
            SelectedCategories = Categories.Where(c => c.CatIsSelected).Select(c => c.BuiltInCategory).ToList(),
            CategoryParameterLogic = SelectedItemCatOrPar,
            ParameterLogic = SelectedItemParOrPar,
            Conditions = FilterConditions.Select(ToConditionModel).ToList()
        };

        var settings = new JsonSerializerSettings
        {
            // Одной строкой: JSON показывается в поле правила, многострочный растягивал строку.
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore, Formatting = Formatting.None
        };

        return JsonConvert.SerializeObject(model, settings);
    }

    private ConditionModelBase ToConditionModel(ConditionViewModelBase vm)
    {
        switch (vm)
        {
            case SimpleConditionViewModel simple:
                return new SimpleConditionModel
                {
                    Type = "Simple",
                    ParameterName = simple.ParameterName,
                    Value = simple.Value,
                    SelectedLogic = simple.SelectedItemLogic
                };

            case GroupConditionViewModel group:
                return new GroupConditionModel
                {
                    Type = "Group", Children = group.Children.Select(ToConditionModel).ToList()
                };

            default:
                throw new NotSupportedException($"Unknown condition VM: {vm.GetType().Name}");
        }
    }

    [RelayCommand]
    private void AddSimpleCondition()
    {
        var simple = new SimpleConditionViewModel { RemoveSimpleRequested = vm => FilterConditions.Remove(vm) };
        FilterConditions.Add(simple);
    }

    [RelayCommand]
    private void AddGroupCondition()
    {
        var group = new GroupConditionViewModel { RemoveGroupRequested = vm => FilterConditions.Remove(vm) };
        FilterConditions.Add(group);
    }

    [RelayCommand]
    private void Apply()
    {
        var json = GetResultJson();
        OnApplyRequested?.Invoke(json);
    }

    public void LoadFromModel(FilterConfigModel model)
    {
        // 🧠 Восстановить выбранные категории
        foreach (var cat in Categories) cat.CatIsSelected = model.SelectedCategories.Contains(cat.BuiltInCategory);

        // 🧠 Восстановить логику фильтрации
        SelectedItemCatOrPar = model.CategoryParameterLogic;
        SelectedItemParOrPar = model.ParameterLogic;

        // 🧠 Восстановить условия
        FilterConditions.Clear();
        foreach (var condition in model.Conditions)
        {
            var vm = FromConditionModel(condition);
            if (vm != null) FilterConditions.Add(vm);
        }
    }

    private ConditionViewModelBase? FromConditionModel(ConditionModelBase model)
    {
        switch (model)
        {
            case SimpleConditionModel simple:
                return new SimpleConditionViewModel
                {
                    ParameterName = simple.ParameterName,
                    Value = simple.Value,
                    SelectedItemLogic = simple.SelectedLogic,
                    RemoveSimpleRequested = vm => FilterConditions.Remove(vm)
                };

            case GroupConditionModel group:
                var groupVM = new GroupConditionViewModel
                {
                    RemoveGroupRequested = vm => FilterConditions.Remove(vm)
                };

                foreach (var child in group.Children)
                {
                    var childVM = FromConditionModel(child);
                    if (childVM != null) groupVM.Children.Add(childVM);
                }

                return groupVM;

            default:
                return null;
        }
    }

    public FilterConfigModel ParseConfig(string json)
    {
        var jObject = JObject.Parse(json);

        var config = new FilterConfigModel();

        var selectedCategories = jObject["SelectedCategories"]?.ToObject<List<int>>() ?? new List<int>();
        config.SelectedCategories = selectedCategories.Select(i => (BuiltInCategory)i).ToList();

        var catLogicInt = jObject["CategoryParameterLogic"]?.ToObject<int>() ?? 0;
        config.CategoryParameterLogic = (CategoryParameterLogic)catLogicInt;

        var paramLogicInt = jObject["ParameterLogic"]?.ToObject<int>() ?? 0;
        config.ParameterLogic = (FilterParameterLogic)paramLogicInt;

        var jConditions = jObject["Conditions"] as JArray;
        if (jConditions != null)
            foreach (var jCond in jConditions)
            {
                var type = jCond["Type"]?.ToString();

                if (type == "Simple")
                {
                    var simple = new SimpleConditionModel
                    {
                        Type = "Simple",
                        ParameterName = jCond["ParameterName"]?.ToString() ?? "",
                        Value = jCond["Value"]?.ToString() ?? "",
                        SelectedLogic = (FilterLogic)(jCond["SelectedLogic"]?.ToObject<int>() ?? 0)
                    };

                    config.Conditions.Add(simple);
                }
                else if (type == "Group")
                {
                    var group = new GroupConditionModel
                    {
                        Type = "Group", Children = new List<ConditionModelBase>()
                    };

                    var children = jCond["Children"] as JArray;
                    if (children != null)
                        foreach (var child in children)
                        {
                            var childType = child["Type"]?.ToString();
                            if (childType == "Simple")
                            {
                                var childSimple = new SimpleConditionModel
                                {
                                    Type = "Simple",
                                    ParameterName = child["ParameterName"]?.ToString() ?? "",
                                    Value = child["Value"]?.ToString() ?? "",
                                    SelectedLogic = (FilterLogic)(child["SelectedLogic"]?.ToObject<int>() ?? 0)
                                };
                                group.Children.Add(childSimple);
                            }
                        }

                    config.Conditions.Add(group);
                }
            }

        return config;
    }
}