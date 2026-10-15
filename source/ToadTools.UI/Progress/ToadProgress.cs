#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using ToadTools.UI.Services;
using ToadTools.UI.ViewModels;
using ToadTools.UI.Views;
using Wpf.Ui.Appearance;

namespace ToadTools.UI.Progress;

/// <summary>
///     Окно прогресса долгой операции с кнопкой «Прервать» и (по желанию) «Пропустить» текущую задачу.
///     <para>
///         Окно живёт в собственном UI-потоке: пока поток Revit занят (открытие модели, копирование
///         элементов, большая транзакция), окно продолжает перерисовываться и реагировать на кнопки.
///         Плагин только сообщает о ходе работы и проверяет <see cref="IsCancelled" /> /
///         <see cref="IsSkipRequested" /> — ничего прокачивать не нужно.
///     </para>
///     <para>Все методы можно вызывать из потока Revit. Обязательно закрывайте через <c>using</c>.</para>
/// </summary>
public sealed class ToadProgress : IDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(5);

    private readonly object _lock = new();
    private readonly ProgressSnapshot _state = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly List<IntPtr> _disabledWindows = new();
    private readonly IntPtr _activeWindow;
    private readonly ProgressOptions _options;
    private int _version;
    private volatile bool _skipRequested;
    private Dispatcher? _dispatcher;
    private ToadProgressWindow? _window;
    private Thread? _thread;
    private bool _disposed;

    private ToadProgress(string title, ProgressOptions options)
    {
        Title = title;
        _options = options;
        _activeWindow = GetActiveWindow();
    }

    public string Title { get; }

    /// <summary>Пользователь нажал «Прервать».</summary>
    public bool IsCancelled => _cancellation.IsCancellationRequested;

    /// <summary>Отмена в виде токена — для кода, который умеет с ним работать.</summary>
    public CancellationToken Token => _cancellation.Token;

    /// <summary>Пользователь нажал «Пропустить» для текущей задачи. Сбрасывается в <see cref="BeginItem" />.</summary>
    public bool IsSkipRequested => _skipRequested;

    /// <summary>Нужно бросить текущую задачу: её пропустили или прервали всё.</summary>
    public bool ShouldAbortItem => _skipRequested || IsCancelled;

    /// <summary>
    ///     Показывает окно прогресса. Пока оно открыто, окна Revit (и окно плагина) недоступны для ввода —
    ///     как при модальном окне.
    /// </summary>
    public static ToadProgress Show(string title, ProgressOptions? options = null)
    {
        var progress = new ToadProgress(title, options ?? new ProgressOptions());
        progress.Start();
        return progress;
    }

    /// <summary>Бросает <see cref="OperationCanceledException" />, если нажата «Прервать».</summary>
    public void ThrowIfCancelled() => _cancellation.Token.ThrowIfCancellationRequested();

    /// <summary>Новый этап без счётчика (полоса «бегает»).</summary>
    public void Stage(string stage, string? detail = null)
    {
        Update(state =>
        {
            state.Stage = stage;
            state.Detail = detail ?? string.Empty;
            state.Current = 0;
            state.Total = 0;
        });
    }

    /// <summary>Текущий шаг этапа: current из total. Без detail выводится «current из total».</summary>
    public void Report(int current, int total, string? detail = null)
    {
        Update(state =>
        {
            state.Current = current;
            state.Total = total;
            state.Detail = detail ?? (total > 0 ? $"{current} из {total}" : string.Empty);
        });
    }

    /// <summary>Только строка пояснения под полосой.</summary>
    public void Detail(string? detail) => Update(state => state.Detail = detail ?? string.Empty);

    /// <summary>
    ///     Перебор коллекции с прогрессом: этап <paramref name="stage" />, «i из N» и проверка отмены
    ///     перед каждым элементом (бросает <see cref="OperationCanceledException" />).
    /// </summary>
    public IEnumerable<T> Track<T>(IEnumerable<T> items, string stage)
    {
        var list = items as IReadOnlyCollection<T> ?? items.ToList();
        Stage(stage);
        var total = list.Count;
        var index = 0;
        foreach (var item in list)
        {
            ThrowIfCancelled();
            Report(++index, total);
            yield return item;
        }

        ThrowIfCancelled();
    }

    /// <summary>
    ///     Начинает отдельную задачу (например, одну модель из списка) — она показывается карточкой
    ///     со своим временем и полосой. С <paramref name="canSkip" /> в карточке есть кнопка «Пропустить».
    /// </summary>
    public void BeginItem(string title, bool canSkip = false)
    {
        _skipRequested = false;
        Update(state =>
        {
            state.ItemVisible = true;
            state.ItemTitle = title;
            state.ItemStatus = null;
            state.ItemCurrent = 0;
            state.ItemTotal = 0;
            state.ItemCanSkip = canSkip;
            state.ItemStartedAt = DateTime.Now;
        });
    }

    /// <summary>Что сейчас происходит внутри задачи (полоса задачи «бегает»).</summary>
    public void ItemStatus(string? status)
    {
        Update(state =>
        {
            state.ItemVisible = true;
            state.ItemStatus = status;
            state.ItemCurrent = 0;
            state.ItemTotal = 0;
        });
    }

    /// <summary>Ход задачи: current из total.</summary>
    public void ItemReport(int current, int total, string? status = null)
    {
        Update(state =>
        {
            state.ItemVisible = true;
            state.ItemCurrent = current;
            state.ItemTotal = total;
            if (status != null) state.ItemStatus = status;
        });
    }

    /// <summary>Задача завершена — карточка скрывается.</summary>
    public void EndItem()
    {
        _skipRequested = false;
        Update(state =>
        {
            state.ItemVisible = false;
            state.ItemTitle = null;
            state.ItemStatus = null;
            state.ItemCanSkip = false;
        });
    }

    internal void RequestCancel() => _cancellation.Cancel();

    internal void RequestSkip() => _skipRequested = true;

    /// <summary>Копия состояния для окна; null — с прошлого раза ничего не менялось.</summary>
    internal ProgressSnapshot? TakeSnapshot(ref int seenVersion)
    {
        lock (_lock)
        {
            if (seenVersion == _version) return null;
            seenVersion = _version;
            return _state.Clone();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Сначала возвращаем Revit в рабочее состояние — что бы ни случилось с окном прогресса.
        foreach (var window in _disabledWindows)
            EnableWindow(window, true);
        _disabledWindows.Clear();

        var dispatcher = _dispatcher;
        if (dispatcher != null && !dispatcher.HasShutdownStarted)
        {
            try
            {
                dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => _window?.ForceClose()));
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
            catch
            {
                // Поток окна уже завершается.
            }
        }

        _thread?.Join(TimeSpan.FromSeconds(2));

        if (_activeWindow != IntPtr.Zero)
            SetForegroundWindow(_activeWindow);

        _cancellation.Dispose();
    }

    private void Update(Action<ProgressSnapshot> change)
    {
        lock (_lock)
        {
            change(_state);
            _version++;
        }
    }

    private void Start()
    {
        // Тему берём ту, что сейчас выбрана в окнах плагинов: окно прогресса живёт в своём потоке
        // и не может пользоваться ресурсами потока Revit.
        var theme = ThemeWatcherService.CurrentTheme;
        var anchor = _activeWindow != IntPtr.Zero ? _activeWindow : Process.GetCurrentProcess().MainWindowHandle;
        var started = new ManualResetEventSlim();

        try
        {
            _thread = new Thread(() => RunWindowThread(theme, anchor, started))
            {
                IsBackground = true,
                Name = "ToadTools progress"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            started.Wait(StartupTimeout);
        }
        catch (Exception exception)
        {
            // Без окна плагин всё равно должен отработать — просто без прогресса.
            Debug.WriteLine(exception);
        }

        DisableThreadWindows();
    }

    private void RunWindowThread(ApplicationTheme theme, IntPtr anchor, ManualResetEventSlim started)
    {
        // Исключение в фоновом потоке убило бы весь Revit — здесь не должно вылететь ничего.
        try
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _dispatcher.UnhandledException += (_, args) => args.Handled = true;

            var viewModel = new ToadProgressViewModel(this, _options);
            _window = new ToadProgressWindow(viewModel, theme, anchor, _options.ShowDelay);

            started.Set();
            Dispatcher.Run();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
        finally
        {
            // Контролы окна остались подписаны на смену темы — иначе следующая смена темы в потоке Revit
            // дёрнет объекты этого (уже завершённого) потока и упадёт.
            ThemeWatcherService.DetachThread(Dispatcher.CurrentDispatcher);
            started.Set();
        }
    }

    /// <summary>
    ///     Делает окна потока Revit недоступными для ввода, пока идёт работа. Revit сам прокачивает сообщения
    ///     во время некоторых операций (например, открытия модели) — без этого можно было бы нажать
    ///     кнопку в окне плагина посреди работы.
    /// </summary>
    private void DisableThreadWindows()
    {
        EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
        {
            if (IsWindowVisible(window) && IsWindowEnabled(window))
            {
                EnableWindow(window, false);
                _disabledWindows.Add(window);
            }

            return true;
        }, IntPtr.Zero);
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumThreadWindows(uint threadId, EnumWindowsProc callback, IntPtr parameter);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool enable);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
