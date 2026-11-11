using System.Collections.ObjectModel;
using ToadDesign;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using ToadTools.UI.Models;
using ToadTools.UI.Services;
using SSPlan.Models;
using SSPlan.Services;
using Settings = SSPlan.Configuration.Settings;

namespace SSPlan.ViewModels;

public partial class SSPlanViewModel(Document doc, UIDocument uidoc, View view) : ObservableObject
{
    [ObservableProperty] private bool _darkTheme = true;
    [ObservableProperty] private int _axisX = 5;
    [ObservableProperty] private int _axisY = 5;
    [ObservableProperty] private PanelItem _selectedPanel = null!;
    [ObservableProperty] private FamilyItem _selectedFamily = null!;
    
    private IWindowService? _windowService;
    private PickPanelHandler? _pickHandler;
    private ExternalEvent? _externalEvent;
    private readonly AnnotationPlacementModel _placementModel = new AnnotationPlacementModel();
    
    public ObservableCollection<PanelItem> Panels { get; } = new();
    public ObservableCollection<FamilyItem> FamilyItems { get; } = new();

    public void SetWindowService(IWindowService? windowService)
    {
        _windowService = windowService;
    }
    
    public void LoadPanels()
    {
        var panels = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
            .WhereElementIsNotElementType()
            .OfType<FamilyInstance>()
            .Where(fi =>
            {
#if REVIT2021_OR_GREATER
        var systems = fi.MEPModel?.GetElectricalSystems();
        return systems != null && systems.Any();
#else
                var systems = fi.MEPModel?.AssignedElectricalSystems;
                return systems != null && systems.Cast<ElectricalSystem>().Any();
#endif
            });


        foreach (var fi in panels)
            Panels.Add(new PanelItem(fi));
    }
    
    public void LoadFamilies()
    {
        FamilyItems.Clear();

        var families = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .OfCategory(BuiltInCategory.OST_GenericAnnotation)
            .Cast<FamilySymbol>()
            .OrderBy(fs => $"{fs.Family.Name} - {fs.Name}");

        foreach (var fs in families)
            FamilyItems.Add(new FamilyItem(fs));
    }
    
    [RelayCommand]
    private void SelectPanel()
    {

        if (_pickHandler == null)
        {
            _pickHandler = new PickPanelHandler(this, _windowService, uidoc);
            _externalEvent = ExternalEvent.Create(_pickHandler);
        }
        
        _windowService?.Hide();
        
        Task.Delay(100).ContinueWith(t =>
        {
            _externalEvent?.Raise();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    [RelayCommand]
    private void Run()
    {
        try
        {
            var result = _placementModel.PlaceAnnotations(
                doc: doc,
                view: view,
                panelItem: SelectedPanel,
                familyItem: SelectedFamily,
                offsetXmm: AxisX,
                offsetYmm: AxisY);
                
            if (result == Result.Succeeded)
            {
                string? done = ToadDialogService.Show(
                    "Готово",
                    "Схема успешно создана",
                    DialogButtons.OK,
                    DialogIcon.Info
                );
            }
        }
        catch (Exception ex)
        {
            string? error = ToadDialogService.Show(
                "Ошибка",
                $"Произошла ошибка: {ex.Message}",
                DialogButtons.OK,
                DialogIcon.Error
            );
        }
    }

    
    partial void OnDarkThemeChanged(bool value)
    {
        var newTheme = value ? ToadThemeMode.Dark : ToadThemeMode.Light;
        ToadTheme.Mode = newTheme;
    }
    
    public void LoadFromSettings(Settings settings)
    {
        DarkTheme = settings.DarkTheme;
        AxisX = settings.AxisX;
        AxisY = settings.AxisY;
    }

    public Settings ToSettings()
    {
        return new Settings
        {
            DarkTheme = DarkTheme,
            AxisX = AxisX,
            AxisY = AxisY
        };
    }
}