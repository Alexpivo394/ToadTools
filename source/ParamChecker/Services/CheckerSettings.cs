using System.IO;
using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

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

    /// <summary>Отправлять итоги проверенных моделей в аналитику сервиса ToadFlow.</summary>
    [ObservableProperty] private bool _toadFlowEnabled;

    /// <summary>
    ///     Адрес ToadFlow из настроек прежних версий — только для переноса в общее подключение ToadTools
    ///     (<see cref="ToadFlowSender.MigrateLegacy" />); после переноса стирается.
    /// </summary>
    [JsonProperty("ToadFlowUrl")]
    public string LegacyToadFlowUrl { get; set; } = "";

    /// <summary>Токен ToadFlow из настроек прежних версий (DPAPI) — только для переноса; после переноса стирается.</summary>
    [JsonProperty("ToadFlowTokenProtected")]
    public string LegacyToadFlowTokenProtected { get; set; } = "";

    public bool ShouldSerializeLegacyToadFlowUrl() => LegacyToadFlowUrl.Length > 0;
    public bool ShouldSerializeLegacyToadFlowTokenProtected() => LegacyToadFlowTokenProtected.Length > 0;

    /// <summary>Расшифрованный старый токен; пусто — его нет или настройки с другого компьютера.</summary>
    public string LegacyToadFlowToken()
    {
        if (LegacyToadFlowTokenProtected.Length == 0) return "";
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(LegacyToadFlowTokenProtected), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return "";
        }
    }

    public string HistoryPath() =>
        HistoryFile.Trim().Length > 0 ? HistoryFile.Trim() : Path.Combine(ReportsFolder.Trim(), "ParamChecker_База.xlsx");
}
