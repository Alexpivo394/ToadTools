using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using ParamChecker.Models.ExportProfiles;
using ParamChecker.Services;
using ParamChecker.ViewModels.PagesViewModels;
using ParamChecker.Views.Dialogs;
using ParamChecker.Views.Pages;
using ParamChecker.Models;
using ToadTools.UI.Models;
using ToadTools.UI.Progress;
using ToadTools.UI.Services;
using Wpf.Ui.Controls;

namespace ParamChecker.ViewModels.Windows;

public sealed partial class ParamCheckerViewModel : ObservableObject
{
    private readonly CategoryService _categoryService;
    private readonly ExportService _exportService;
    private readonly Logger _logger;
    private readonly SettingsViewModel _settingsViewModel;

    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private string? _title;

    public ParamCheckerViewModel(CategoryService categoryService,  ExportService exportService,  SettingsViewModel settingsViewModel, Logger logger)
    {
        _logger = logger;
        _settingsViewModel = settingsViewModel;
        _categoryService = categoryService;
        _exportService = exportService;
    }

    public ObservableCollection<CustomNavItem> CustomNavItems { get; set; } = new();

    public Action<Page>? NavigateAction { get; set; }

    [RelayCommand]
    private void AddCustomNavItem()
    {
        var vm = new ExportProfilesViewModel(_categoryService);
        vm.ProfileName = $"Профиль {CustomNavItems.Count + 1}";
        vm.IsChecked = IsChecked;

        var page = new ExportProfiles
        {
            DataContext = vm
        };

        var item = new CustomNavItem(IsChecked, vm.ProfileName, page, vm, RemoveNavItem);
        item.OnNavigate = NavigateAction;
        CustomNavItems.Add(item);
    }

    [RelayCommand]
    private void RemoveNavItem(CustomNavItem item)
    {
        if (CustomNavItems.Contains(item))
            CustomNavItems.Remove(item);
    }

    [RelayCommand]
    private void RenameNavItem()
    {
        var dialog = new Rename(Title);
        var result = dialog.ShowDialog();
        if (result == true) Title = dialog.Title;
    }
    
    [RelayCommand]
    private void ExportProfiles()
    {
        var profiles = new List<ExportProfile>();

        foreach (var item in CustomNavItems)
        {
            if (item.ViewModelInstance is ExportProfilesViewModel vm)
            {
                var profile = vm.GetProfile();
                profile.ProfileName = vm.ProfileName;
                profiles.Add(profile);
            }
        }

        var json = JsonConvert.SerializeObject(profiles, Formatting.Indented);

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            FileName = "export_profiles.json"
        };

        if (dialog.ShowDialog() == true)
        {
            File.WriteAllText(dialog.FileName, json);
        }
    }

    [RelayCommand]
    private void ImportProfiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var profiles = JsonConvert.DeserializeObject<List<ExportProfile>>(json);

            if (profiles == null) return;

            CustomNavItems.Clear();

            foreach (var profile in profiles)
            {
                var vm = new ExportProfilesViewModel(_categoryService);
                vm.LoadFromProfile(profile);

                var page = new ExportProfiles
                {
                    DataContext = vm
                };

                var item = new CustomNavItem(vm.IsChecked, vm.ProfileName, page, vm, RemoveNavItem)
                {
                    OnNavigate = NavigateAction
                };

                CustomNavItems.Add(item);
            }
        }
        catch (Exception ex)
        {
            var dial = ToadDialogService.Show(
                "Ошибка!",
                $"Ошибка при импорте: {ex.Message}",
                DialogButtons.OK,
                DialogIcon.Error
            );
        }
    }

    [RelayCommand]
    private void StartExport()
    {

        if (!CustomNavItems.Any())
        { 
            var dial = ToadDialogService.Show(
            "Ошибка!",
            "Нет профилей для экспорта",
            DialogButtons.OK,
            DialogIcon.Error);
        }
        else
        {
            _logger.StartLog(_settingsViewModel.LogFilePath);

            // Сначала проверяем профили — чтобы сообщения об ошибках не всплывали посреди долгой выгрузки.
            var profiles = new List<ExportProfile>();
            foreach (var item in CustomNavItems)
            {
                if (item.ViewModelInstance is not ExportProfilesViewModel vm) continue;

                var profile = vm.GetProfile();
                if (profile == null) continue;
                if (profile.IsChecked == true)
                {
                    if (!profile.Models.Any())
                    {
                        var dial = ToadDialogService.Show(
                            "Ошибка!",
                            $"Добавьте хотябы одну модель в профиль {profile.ProfileName}!",
                            DialogButtons.OK,
                            DialogIcon.Error
                        );
                    }
                    else
                    {
                        if (!profile.Rules.Any())
                        {
                            var dial = ToadDialogService.Show(
                                "Ошибка!",
                                $"Добавьте хотябы одно правило в профиль {profile.ProfileName}!",
                                DialogButtons.OK,
                                DialogIcon.Error
                            );
                        }
                        else
                        {
                            profiles.Add(profile);
                        }
                    }
                }
                else
                {
                    _logger.Log($"Профиль {profile.ProfileName} не выбран для экспорта");
                }
            }

            if (profiles.Count == 0) return;

            var summary = new ExportSummary { TotalModels = profiles.Sum(profile => profile.Models.Count) };
            using (var progress = ToadProgress.Show("ParamChecker", new ProgressOptions
                   {
                       Icon = SymbolRegular.DocumentSearch24,
                       SkipText = "Пропустить модель",
                       CancelHint = "Прерываем — текущая модель будет закрыта без отчёта"
                   }))
            {
                foreach (var profile in profiles)
                {
                    if (progress.IsCancelled)
                    {
                        summary.Cancelled = true;
                        break;
                    }

                    _logger.Log($"Обрабатываем профиль {profile.ProfileName}");
                    _exportService.ExportProfile(profile, progress, summary);
                }
            }

            ShowSummary(summary);
        }
    }

    private void ShowSummary(ExportSummary summary)
    {
        var lines = new List<string>
        {
            $"Выгружено моделей: {summary.Exported.Count} из {summary.TotalModels}"
        };
        if (summary.Skipped.Count > 0)
            lines.Add("\nПропущены:\n" + string.Join("\n", summary.Skipped.Select(name => "  • " + name)));
        if (summary.Failed.Count > 0)
            lines.Add("\nОшибки:\n" + string.Join("\n", summary.Failed.Select(name => "  • " + name)));
        if (summary.Failed.Count > 0 || summary.Skipped.Count > 0)
            lines.Add("\nПодробности — в логе.");

        var hasProblems = summary.Failed.Count > 0 || summary.Cancelled;
        ToadDialogService.Show(
            summary.Cancelled ? "Экспорт прерван" : hasProblems ? "Экспорт завершён с ошибками" : "Успех!",
            string.Join("\n", lines),
            DialogButtons.OK,
            hasProblems ? DialogIcon.Warning : DialogIcon.Info
        );
    }
} 