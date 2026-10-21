using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ParamChecker.Services;

/// <summary>Настройки ParamChecker, которые запоминаются между запусками.</summary>
public sealed partial class CheckerSettings : ObservableObject
{
    [ObservableProperty] private bool _darkTheme = true;

    /// <summary>Куда класть отчёты; в ней же — папки профилей и журналы запусков.</summary>
    [ObservableProperty] private string _reportsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ParamChecker");

    /// <summary>Открывать модели без связей RVT — из временной копии, где связи отключены.</summary>
    [ObservableProperty] private bool _skipLinks = true;

    /// <summary>В листах правил только элементы с незаполненными параметрами — отчёты по большим моделям в разы меньше.</summary>
    [ObservableProperty] private bool _problemsOnly;

    /// <summary>Дописывать итоги каждого запуска в базу для дашбордов.</summary>
    [ObservableProperty] private bool _historyEnabled = true;

    /// <summary>Файл базы; пусто — ParamChecker_База.xlsx в папке отчётов.</summary>
    [ObservableProperty] private string _historyFile = "";

    /// <summary>Последний выбранный Revit Server.</summary>
    [ObservableProperty] private string _server = "";

    public string HistoryPath() =>
        HistoryFile.Trim().Length > 0 ? HistoryFile.Trim() : Path.Combine(ReportsFolder.Trim(), "ParamChecker_База.xlsx");
}
