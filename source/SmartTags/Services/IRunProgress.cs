namespace SmartTags.Services;

/// <summary>Окно прогресса глазами раннера: этапы, счётчик, отмена.</summary>
public interface IRunProgress
{
    bool IsCancelled { get; }

    void Stage(string stage, string? detail = null);

    void Report(int current, int total, string? detail = null);

    /// <summary>Даёт окну перерисоваться и обработать нажатие «Отмена» во время долгой работы.</summary>
    void Pump();
}
