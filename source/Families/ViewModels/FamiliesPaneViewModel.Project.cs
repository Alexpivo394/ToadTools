using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Families.Core;

namespace Families.ViewModels;

/// <summary>Семейство открытого проекта и его состояние относительно библиотеки.</summary>
public sealed partial class ProjectFamilyViewModel(ProjectFamily family, ProjectFamilyStatus status, int revitYear, bool coordinator = false) : ObservableObject
{
    public ProjectFamily Family { get; } = family;

    public ProjectFamilyStatus Status { get; } = status;

    public string Name => Family.Name;

    public string Category => Family.Category;

    /// <summary>Итерация, из которой загружено; пусто — «Основная».</summary>
    public string Iteration { get; } = status.IterationName ?? "";

    /// <summary>«актуально», «устарело», «не из библиотеки», «в архиве» — и что предложено.</summary>
    public string StateText { get; } = status.State switch
    {
        "Current" => status.IterationFinal ? "актуально, итерация завершена" : "актуально",
        "Outdated" => status.IterationId is null ? "устарело — в библиотеке новее" : "устарело — в итерации новее",
        "Archived" => status.IterationId is null ? "убрано из библиотеки в архив" : "итерация убрана из библиотеки",
        _ => status.CardId is null ? "не из библиотеки" : $"не из библиотеки — есть «{status.CardName}»"
    };

    /// <summary>Для цвета строки: <c>Current</c>, <c>Outdated</c>, <c>NotFromLibrary</c>, <c>Archived</c>.</summary>
    public string State => Status.State;

    /// <summary>
    ///     Можно обновить из библиотеки: устаревшее или чужое с семейством того же имени в библиотеке, и файл библиотеки не новее
    ///     запущенного Revit.
    /// </summary>
    public bool CanUpdate { get; } = status.CardId is not null && status.FullPath is not null && status.State is "Outdated" or "NotFromLibrary"
                                     && (status.RevitYear is not { } year || year <= revitYear) && (!status.CoordinatorsOnly || coordinator);

    [ObservableProperty] private bool _isChosen;
}

public sealed partial class FamiliesPaneViewModel
{
    public ObservableCollection<ProjectFamilyViewModel> ProjectItems { get; } = [];

    /// <summary>Сводка вкладки «В проекте»: сколько устарело, сколько не из библиотеки.</summary>
    [ObservableProperty] private string _projectSummary = "";

    /// <summary>Показывать только устаревшие и не из библиотеки.</summary>
    [ObservableProperty] private bool _onlyProblems = true;

    partial void OnSelectedTabChanged(int value)
    {
        if (value == 1 && IsProject && Screen == PaneScreen.Ready) _ = RefreshProject();
    }

    partial void OnOnlyProblemsChanged(bool value)
    {
        if (Screen == PaneScreen.Ready && IsProject) _ = RefreshProject();
    }

    [RelayCommand]
    private Task RefreshProject() => RunAsync(RefreshProjectCoreAsync);

    private async Task RefreshProjectCoreAsync(int version)
    {
        var families = await _revit.ProjectFamiliesAsync();
        var statuses = families.Count == 0
            ? []
            : await _api.ProjectStatusAsync(families.Select(f => new ProjectFamilyDto(f.Name, f.CardId, f.Hash, f.IterationId)).ToList(), ShowUnreviewed,
                CancellationToken.None);
        if (version != _version) return;

        var rows = families.Zip(statuses, (family, status) => new ProjectFamilyViewModel(family, status, _revit.RevitYear, Library?.CanPublish == true)).ToList();
        var outdated = rows.Count(r => r.State == "Outdated");
        var archived = rows.Count(r => r.State == "Archived");
        var foreign = rows.Count(r => r.State == "NotFromLibrary");
        var suggested = rows.Count(r => r.State == "NotFromLibrary" && r.Status.CardId is not null);
        ProjectSummary = $"Семейств в проекте: {rows.Count}. Из библиотеки актуальны: {rows.Count(r => r.State == "Current")}, устарели: {outdated}"
                         + (archived > 0 ? $", убраны в архив: {archived}" : "")
                         + $". Не из библиотеки: {foreign}" + (suggested > 0 ? $" (для {suggested} в библиотеке есть семейство с тем же именем)" : "");

        ProjectItems.Clear();
        foreach (var row in rows.Where(r => !OnlyProblems || r.State != "Current").OrderBy(r => r.State switch { "Outdated" => 0, "Archived" => 1, "NotFromLibrary" => 2, _ => 3 })
                     .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            row.IsChosen = row.State == "Outdated" && row.CanUpdate;
            ProjectItems.Add(row);
        }
    }

    /// <summary>
    ///     «Обновить выбранные»: действующая версия из библиотеки ложится поверх семейства проекта с теми же типоразмерами, что уже
    ///     есть в проекте; значения размещённых экземпляров сохраняются, на семейство ставится метка.
    /// </summary>
    [RelayCommand]
    private Task UpdateChosen() => RunAsync(async version =>
    {
        var chosen = ProjectItems.Where(i => i.IsChosen && i.CanUpdate).ToList();
        if (chosen.Count == 0)
        {
            Status = "Отметьте семейства, которые нужно обновить";
            return;
        }

        var replaced = chosen.Count(i => i.State == "NotFromLibrary");
        if (replaced > 0 && !_dialogs.Confirm(
                $"{replaced} {Texts.Plural(replaced, "семейство", "семейства", "семейств")} проекта не из библиотеки и будут заменены библиотечными с тем же именем. Продолжить?"))
            return;

        var updated = new List<string>();
        var failed = new List<string>();
        foreach (var item in chosen)
        {
            try
            {
                // Обновляется своя итерация: вариант одной модели не подменяет вариант другой.
                var outcome = await LoadFamilyAsync(item.Status.CardId!.Value, item.Name, item.Status.FullPath!, item.Status.CurrentHash, item.Family.TypeNames,
                    item.Status.IterationId);
                if (outcome is not null) updated.Add(item.Name);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                failed.Add($"{item.Name}: {exception.Message}");
            }
        }

        await RefreshProjectCoreAsync(_version);
        Status = $"Обновлено: {updated.Count}" + (failed.Count > 0 ? $". Не обновились: {string.Join("; ", failed)}" : "");
    });
}
