using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CreateSpaces.Models;
using CreateSpaces.Services;
using Nice3point.Revit.Toolkit;
using ToadTools.UI.Models;
using ToadTools.UI.Services;
using Microsoft.Win32;
using Newtonsoft.Json;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace CreateSpaces.ViewModels;

public partial class CreateSpacesViewModel : ObservableObject
{
    [ObservableProperty] private bool _darkTheme = true;
    [ObservableProperty] private bool _createSpaces = true;
    [ObservableProperty] private ObservableCollection<ParameterMappingModel> _models = new();
    public ObservableCollection<LinkDescriptor> LinkedModels { get; set; }
    [ObservableProperty] private LinkDescriptor? _selectedLink;
    private readonly LoadParametersService _loadParameterService;
    private RevitRoomProvider _roomProvider;
    private readonly ISpaceCreationService _spaceCreationService;
    public CreateSpacesViewModel(RevitLinkProvider linkProvider, LoadParametersService loadParameterService, RevitRoomProvider roomProvider, ISpaceCreationService spaceCreationService)
    {
        LinkedModels = new ObservableCollection<LinkDescriptor>(linkProvider.GetLinks(RevitContext.ActiveDocument!));
        Models = new ObservableCollection<ParameterMappingModel>();
        _loadParameterService = loadParameterService;
        _roomProvider = roomProvider;
        _spaceCreationService = spaceCreationService;
    }

    partial void OnDarkThemeChanged(bool value)
    {
        var newTheme = value ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ThemeWatcherService.ApplyTheme(newTheme);
    }

    partial void OnSelectedLinkChanged(LinkDescriptor? value)
    {
        if (value == null)
        {
            Models.Clear();
            return;
        }

        LoadRoomParameters(value);
    }
    
    private void LoadRoomParameters(LinkDescriptor link)
    {
        _roomProvider.Initialize(link);
        var roomParameters = _loadParameterService.GetRoomParameters();
        if (roomParameters.Count == 0)
        {
            Models.Clear();
            return;
        }
        var spaceParameters = _loadParameterService.GetSpaceParameters();
        
        UpdateModels(spaceParameters, roomParameters);
    }

    private void UpdateModels(
        IEnumerable<ParameterDescriptor> spaceParameters,
        IEnumerable<ParameterDescriptor> roomParameters)
    {
        Models = new ObservableCollection<ParameterMappingModel>(
            spaceParameters.Select(sp =>
                new ParameterMappingModel(sp, roomParameters))
        );
    }

    [RelayCommand]
    private void Start()
    {
        if (SelectedLink == null)
            return;

        var result = _spaceCreationService.CreateSpaces(
            SelectedLink,
            Models,
            CreateSpaces);
        
        var dial = ToadDialogService.Show(
            "Успех!",
            $"Создано: {result.Created}\nОбновлено: {result.Updated}",
            DialogButtons.OK,
            DialogIcon.Info
        );
    }

    [RelayCommand]
    private void Export()
    {
        if (Models == null || Models.Count == 0)
            return;

        var config = new ParameterMappingConfig();

        foreach (var model in Models)
        {
            if (model.SelectedRoomParameter == null)
                continue;

            config.Items.Add(new ParameterMappingItem
            {
                SpaceParameterName = model.SpaceParameter.Name,
                RoomParameterName = model.SelectedRoomParameter.Name
            });
        }

        if (config.Items.Count == 0)
            return;

        var dialog = new SaveFileDialog
        {
            Filter = "JSON (*.json)|*.json",
            DefaultExt = "json",
            FileName = "CreateSpacesMapping"
        };

        if (dialog.ShowDialog() != true)
            return;

        var json = JsonConvert.SerializeObject(config, Formatting.Indented);

        File.WriteAllText(dialog.FileName, json);
    }

    [RelayCommand]
    private void Import()
    {
        if (Models == null || Models.Count == 0)
            return;

        var dialog = new OpenFileDialog
        {
            Filter = "JSON (*.json)|*.json",
            DefaultExt = "json"
        };

        if (dialog.ShowDialog() != true)
            return;

        ParameterMappingConfig? config;
        try
        {
            var json = File.ReadAllText(dialog.FileName);
            config = JsonConvert.DeserializeObject<ParameterMappingConfig>(json);
        }
        catch
        {
            return;
        }

        if (config?.Items == null || config.Items.Count == 0)
            return;

        var map = config.Items
            .Where(i => i.SpaceParameterName != null)
            .GroupBy(i => i.SpaceParameterName!)
            .ToDictionary(g => g.Key, g => g.First().RoomParameterName);

        foreach (var model in Models)
        {
            if (model.SpaceParameter.Name == null || !map.TryGetValue(model.SpaceParameter.Name, out var roomParamName))
                continue;

            if (string.IsNullOrEmpty(roomParamName))
                continue;

            var target = model.RoomParameters.FirstOrDefault(r => r.Name == roomParamName);
            if (target != null)
                model.SelectedRoomParameter = target;
        }
    }
    
}

