namespace ToadTools.Desktop.Core.State;

/// <summary>Что программа уже сделала (%LOCALAPPDATA%\ToadTools\state.json).</summary>
public sealed class UpdateState
{
    /// <summary>ETag последнего ответа GitHub: следующий запрос с ним не тратит лимит, если ничего не поменялось.</summary>
    public string? ETag { get; set; }

    public DateTime? LastCheckUtc { get; set; }

    /// <summary>Версия плагина, разложенная в Addins.</summary>
    public string? DeployedPluginVersion { get; set; }

    /// <summary>Версия, скачанная и распакованная в staging, ждёт закрытия Revit.</summary>
    public string? StagedPluginVersion { get; set; }

    /// <summary>Скачанный и проверенный MSI новой версии программы.</summary>
    public string? DownloadedAppMsi { get; set; }

    /// <summary>«Что нового» последнего релиза: показывается и без сети.</summary>
    public string? ReleaseNotes { get; set; }
}

public sealed class StateStore(string file) : JsonFileStore<UpdateState>(file);
