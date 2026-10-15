namespace ParamChecker.Models;

/// <summary>Итог запуска экспорта: какие модели выгружены, пропущены пользователем или упали с ошибкой.</summary>
public class ExportSummary
{
    public int TotalModels { get; set; }

    /// <summary>Номер текущей модели (с единицы) среди всех выбранных профилей.</summary>
    public int CurrentModel { get; set; }

    public List<string> Exported { get; } = new();
    public List<string> Skipped { get; } = new();
    public List<string> Failed { get; } = new();
    public bool Cancelled { get; set; }
}
