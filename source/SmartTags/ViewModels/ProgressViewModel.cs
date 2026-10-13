using System.Diagnostics;
using System.Windows.Threading;
using SmartTags.Services;

namespace SmartTags.ViewModels;

/// <summary>
///     Окно прогресса. Работа идёт в потоке Revit, поэтому окно само не перерисуется —
///     раннер регулярно вызывает <see cref="Pump" />, который прокачивает очередь сообщений WPF.
/// </summary>
public sealed partial class ProgressViewModel : ObservableObject, IRunProgress
{
    // Чаще перерисовывать незачем, а прокачка очереди не бесплатна.
    private const int PumpIntervalMs = 40;

    private readonly Stopwatch _sincePump = Stopwatch.StartNew();

    [ObservableProperty] private string _stageText = "Подготовка";
    [ObservableProperty] private string _detail = string.Empty;
    [ObservableProperty] private bool _isIndeterminate = true;
    [ObservableProperty] private double _value;
    [ObservableProperty] private double _maximum = 1;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isCancelled;

    void IRunProgress.Stage(string stage, string? detail)
    {
        StageText = stage;
        Detail = detail ?? string.Empty;
        IsIndeterminate = true;
        PumpNow();
    }

    public void Report(int current, int total, string? detail = null)
    {
        if (IsCancelled) return;

        IsIndeterminate = total <= 0;
        Maximum = Math.Max(1, total);
        Value = Math.Max(0, Math.Min(current, total));
        Detail = detail ?? $"{current} из {total}";
        Pump();
    }

    public void Pump()
    {
        if (_sincePump.ElapsedMilliseconds >= PumpIntervalMs) PumpNow();
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        IsCancelled = true;
        Detail = "Отмена… изменения в модели будут откачены";
        PumpNow();
    }

    private bool CanCancel() => !IsCancelled;

    private void PumpNow()
    {
        // Аналог DoEvents: обрабатываем всё, что в очереди выше фонового приоритета (отрисовка, ввод).
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        _sincePump.Restart();
    }
}
