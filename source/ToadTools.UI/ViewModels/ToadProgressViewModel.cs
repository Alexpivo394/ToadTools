#nullable enable
using System;
using System.Windows.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToadTools.UI.Progress;
using Wpf.Ui.Controls;

namespace ToadTools.UI.ViewModels;

/// <summary>
///     Модель окна прогресса. Живёт в потоке окна и раз в <see cref="Refresh" /> забирает
///     состояние, которое поток Revit записал в <see cref="ToadProgress" />.
/// </summary>
public sealed partial class ToadProgressViewModel : ObservableObject
{
    private readonly ToadProgress _progress;
    private readonly DateTime _startedAt = DateTime.Now;
    private DateTime _itemStartedAt;
    private int _seenVersion = -1;

    [ObservableProperty] private string _stage = "Подготовка";
    [ObservableProperty] private string _detail = string.Empty;
    [ObservableProperty] private bool _isIndeterminate = true;
    [ObservableProperty] private double _value;
    [ObservableProperty] private double _maximum = 1;
    [ObservableProperty] private string _percent = string.Empty;
    [ObservableProperty] private string _elapsed = "0:00";

    [ObservableProperty] private bool _itemVisible;
    [ObservableProperty] private string _itemTitle = string.Empty;
    [ObservableProperty] private string _itemStatus = string.Empty;
    [ObservableProperty] private bool _itemIndeterminate = true;
    [ObservableProperty] private double _itemValue;
    [ObservableProperty] private double _itemMaximum = 1;
    [ObservableProperty] private string _itemElapsed = string.Empty;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SkipCommand))]
    private bool _itemCanSkip;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SkipCommand))]
    private bool _isSkipPending;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(SkipCommand))]
    private bool _isCancelled;

    [ObservableProperty] private TaskbarItemProgressState _taskbarState = TaskbarItemProgressState.Indeterminate;
    [ObservableProperty] private double _taskbarValue;

    public ToadProgressViewModel(ToadProgress progress, ProgressOptions options)
    {
        _progress = progress;
        Title = progress.Title;
        Icon = options.Icon;
        CanCancel = options.CanCancel;
        CancelHint = options.CancelHint;
        SkipText = options.SkipText;
    }

    public string Title { get; }
    public SymbolRegular Icon { get; }
    public bool CanCancel { get; }
    public string CancelHint { get; }
    public string SkipText { get; }

    /// <summary>Забирает свежее состояние и обновляет таймеры. Вызывается таймером окна.</summary>
    public void Refresh()
    {
        var now = DateTime.Now;
        Elapsed = FormatElapsed(now - _startedAt);
        IsSkipPending = _progress.IsSkipRequested;

        var state = _progress.TakeSnapshot(ref _seenVersion);
        if (state != null) Apply(state);

        ItemElapsed = ItemVisible && _itemStartedAt != default ? FormatElapsed(now - _itemStartedAt) : string.Empty;
    }

    private void Apply(ProgressSnapshot state)
    {
        Stage = state.Stage;
        Detail = state.Detail;

        IsIndeterminate = state.Total <= 0;
        Maximum = Math.Max(1, state.Total);
        Value = Clamp(state.Current, state.Total);
        Percent = state.Total > 0 ? $"{Math.Floor(100.0 * Value / Maximum)} %" : string.Empty;

        ItemVisible = state.ItemVisible;
        ItemTitle = state.ItemTitle ?? string.Empty;
        ItemStatus = state.ItemStatus ?? string.Empty;
        ItemIndeterminate = state.ItemTotal <= 0;
        ItemMaximum = Math.Max(1, state.ItemTotal);
        ItemValue = Clamp(state.ItemCurrent, state.ItemTotal);
        ItemCanSkip = state.ItemCanSkip;
        _itemStartedAt = state.ItemStartedAt;

        if (!IsCancelled)
        {
            TaskbarState = IsIndeterminate ? TaskbarItemProgressState.Indeterminate : TaskbarItemProgressState.Normal;
            TaskbarValue = IsIndeterminate ? 0 : Value / Maximum;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelNow))]
    private void Cancel()
    {
        IsCancelled = true;
        TaskbarState = TaskbarItemProgressState.Paused;
        _progress.RequestCancel();
    }

    private bool CanCancelNow() => !IsCancelled;

    [RelayCommand(CanExecute = nameof(CanSkipNow))]
    private void Skip()
    {
        IsSkipPending = true;
        _progress.RequestSkip();
    }

    private bool CanSkipNow() => ItemCanSkip && !IsSkipPending && !IsCancelled;

    private static double Clamp(int current, int total) => total <= 0 ? 0 : Math.Max(0, Math.Min(current, total));

    private static string FormatElapsed(TimeSpan time)
    {
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
    }
}
