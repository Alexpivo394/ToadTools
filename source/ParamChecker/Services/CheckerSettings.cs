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

    /// <summary>Адрес ToadFlow, например <c>http://toadflow:8080</c>.</summary>
    [ObservableProperty] private string _toadFlowUrl = "";

    /// <summary>Личный токен ToadFlow, зашифрованный для текущего пользователя Windows (DPAPI) — в файле настроек он не лежит открытым.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ToadFlowToken))]
    private string _toadFlowTokenProtected = "";

    /// <summary>Личный токен ToadFlow (из «Настройки → Токены доступа» в веб-интерфейсе).</summary>
    [JsonIgnore]
    public string ToadFlowToken
    {
        get
        {
            if (ToadFlowTokenProtected.Length == 0) return "";
            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ToadFlowTokenProtected), null, DataProtectionScope.CurrentUser));
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException)
            {
                return ""; // настройки с другого компьютера или пользователя — токен нужно ввести заново
            }
        }
        set => ToadFlowTokenProtected = string.IsNullOrWhiteSpace(value)
            ? ""
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));
    }

    public string HistoryPath() =>
        HistoryFile.Trim().Length > 0 ? HistoryFile.Trim() : Path.Combine(ReportsFolder.Trim(), "ParamChecker_База.xlsx");
}
