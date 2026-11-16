using System.Text.RegularExpressions;
using ToadTools.Desktop.Core.Downloads;
using ToadTools.Desktop.Core.Plugin;
using ToadTools.Desktop.Core.Releases;
using ToadTools.Desktop.Core.Revit;
using ToadTools.Desktop.Core.Security;
using ToadTools.Desktop.Core.Settings;
using ToadTools.Desktop.Core.State;

namespace ToadTools.Desktop.Core.Updates;

public enum UpdateStatus
{
    Idle,
    Checking,
    Downloading,

    /// <summary>Новая версия плагина скачана и ждёт закрытия Revit (или кнопки «Установить сейчас»).</summary>
    WaitingForRevit,

    Installing,
    UpToDate,

    /// <summary>Есть новая версия плагина, но автоматическое скачивание выключено.</summary>
    UpdateAvailable,

    /// <summary>Скачан и проверен MSI новой версии программы.</summary>
    AppUpdateReady,

    Error
}

/// <summary>
///     Обновление ToadTools. Проверка: releases/latest → выбор (плагин / программа) → скачивание → проверка подписи →
///     плагин распаковывается в staging. Установка плагина (отдельная задача): ждёт, пока не останется ни одного Revit,
///     и подменяет папки в Addins; заодно ставит плагин в годы, включённые в настройках, и убирает из выключенных.
///     Распакованная версия остаётся в staging: из неё плагин ставится в год, включённый позже.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private const int FirstYear = RevitDetector.FirstYear;
    private const int LastYear = RevitDetector.LastYear;
    private const int LockedAttempts = 5;
    private static readonly Regex AppMsiName = new(@"ToadTools-App-(\d+\.\d+\.\d+)\.msi$", RegexOptions.IgnoreCase);

    private readonly AppPaths _paths;
    private readonly Func<UpdateSettings> _settings;
    private readonly StateStore _stateStore;
    private readonly Func<UpdateSettings, HttpMessageHandler> _handlerFactory;
    private readonly SignatureVerifier _verifier;
    private readonly PluginDeployer _deployer;
    private readonly RevitWatcher _revit;
    private readonly Func<IReadOnlyList<int>> _installedYears;
    private readonly Version _currentApp;
    private readonly Uri? _feed;
    private readonly TimeSpan _retryDelay;

    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly object _sync = new();
    private readonly UpdateState _state;

    private TaskCompletionSource<bool> _wake = NewSignal();
    private TaskCompletionSource<bool> _scheduleWake = NewSignal();
    private Task _reconcileTask = Task.CompletedTask;
    private bool _reconcileRunning;
    private bool _reconcileAgain;
    private bool _awaitingManualInstall;
    private bool _waiting;
    private bool _installing;
    private string? _error;
    private Version? _announcedAvailable;
    private Version? _announcedReady;

    public UpdateService(
        AppPaths paths,
        Func<UpdateSettings> settings,
        StateStore state,
        Func<UpdateSettings, HttpMessageHandler> handlerFactory,
        SignatureVerifier verifier,
        PluginDeployer deployer,
        RevitWatcher revit,
        Func<IReadOnlyList<int>> installedYears,
        Version currentApp,
        Uri? feed = null,
        TimeSpan? retryDelay = null)
    {
        _paths = paths;
        _settings = settings;
        _stateStore = state;
        _handlerFactory = handlerFactory;
        _verifier = verifier;
        _deployer = deployer;
        _revit = revit;
        _installedYears = installedYears;
        _currentApp = UpdateSelector.Normalize(currentApp);
        _feed = feed;
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(2);

        _state = state.Load();
        ReleaseNotes = _state.ReleaseNotes;
        LastCheckUtc = _state.LastCheckUtc;
        DeployedPlugin = ComputeDeployed();
        PendingPlugin = ParseVersion(_state.StagedPluginVersion);
        RestorePendingApp();
        RefreshStatus();
    }

    public UpdateStatus Status { get; private set; }
    public string? StatusText { get; private set; }
    public double? DownloadProgress { get; private set; }
    public Version? DeployedPlugin { get; private set; }

    /// <summary>Новая версия плагина: скачана и ждёт установки, или доступна, но не скачана.</summary>
    public Version? PendingPlugin { get; private set; }

    public Version? PendingApp { get; private set; }
    public string? PendingAppMsi { get; private set; }
    public string? ReleaseNotes { get; private set; }
    public DateTime? LastCheckUtc { get; private set; }

    public event EventHandler? Changed;

    /// <summary>Есть новая версия плагина, автоматическое скачивание выключено.</summary>
    public event EventHandler<Version>? PluginAvailable;

    /// <summary>Новая версия скачана, Revit открыт — поставится после его закрытия.</summary>
    public event EventHandler<Version>? PluginReady;

    public event EventHandler<Version>? PluginInstalled;
    public event EventHandler<Version>? AppUpdateReady;

    /// <summary>Работа программы: доводит прерванную установку, проверяет по расписанию до отмены.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linked.Token;
        try
        {
            _deployer.Recover();
            StartReconcile();

            var settings = _settings();
            if (settings.Interval != CheckInterval.Manual || (DeployedPlugin == null && PendingPlugin == null))
                await SafeCheckAsync(token).ConfigureAwait(false);

            while (!token.IsCancellationRequested)
            {
                var wake = _scheduleWake.Task;
                var period = _settings().Interval.ToTimeSpan();
                var delay = Timeout.InfiniteTimeSpan;
                if (period != null)
                {
                    var due = (LastCheckUtc ?? DateTime.MinValue) + period.Value - DateTime.UtcNow;
                    delay = due < TimeSpan.Zero ? TimeSpan.Zero : due;
                }

                using var stopTimer = CancellationTokenSource.CreateLinkedTokenSource(token);
                var timer = Task.Delay(delay, stopTimer.Token);
                var fired = await Task.WhenAny(timer, wake).ConfigureAwait(false) == timer;
                stopTimer.Cancel();
                if (fired && !token.IsCancellationRequested)
                    await SafeCheckAsync(token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async Task SafeCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await CheckCoreAsync(false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Error("Проверка обновлений упала", exception);
        }
    }

    /// <summary>Проверить сейчас. Пользователь нажал кнопку — скачиваем, даже если автоматическое скачивание выключено.</summary>
    public Task CheckNowAsync(bool userInitiated) => CheckCoreAsync(userInitiated, _lifetime.Token);

    private async Task CheckCoreAsync(bool userInitiated, CancellationToken cancellationToken)
    {
        await _checkGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _error = null;
            SetStatus(UpdateStatus.Checking, "Проверка обновлений…");
            var settings = _settings();
            using var handler = _handlerFactory(settings);
            var client = new GitHubReleaseClient(handler, _feed);
            var check = await client.GetLatestAsync(_state.ETag, cancellationToken).ConfigureAwait(false);

            LastCheckUtc = DateTime.UtcNow;
            lock (_sync)
            {
                _state.LastCheckUtc = LastCheckUtc;
                SaveState();
            }

            if (check.Status == ReleaseCheckStatus.Failed)
            {
                Log.Info($"Проверка обновлений: {check.Error}");
                _error = check.Error;
                return;
            }

            if (check.Status == ReleaseCheckStatus.NotModified)
            {
                StartReconcile();
                return;
            }

            var release = check.Release!;
            ReleaseNotes = release.Notes;
            var known = Max(DeployedPlugin, ParseVersion(_state.StagedPluginVersion));
            var choice = UpdateSelector.Choose(release, known, _currentApp);
            var complete = true;

            if (choice.Plugin != null)
            {
                if (settings.AutoDownload || userInitiated)
                {
                    complete &= await DownloadPluginAsync(choice, handler, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    complete = false;
                    PendingPlugin = choice.Plugin;
                    if (_announcedAvailable != choice.Plugin)
                    {
                        _announcedAvailable = choice.Plugin;
                        PluginAvailable?.Invoke(this, choice.Plugin);
                    }
                }
            }

            if (choice.App != null && choice.App != PendingApp)
                complete &= await DownloadAppAsync(choice, handler, cancellationToken).ConfigureAwait(false);

            lock (_sync)
            {
                _state.ReleaseNotes = release.Notes;
                // ETag запоминается, только когда релиз разобран до конца: иначе следующий 304 скрыл бы недоделанное
                if (complete) _state.ETag = check.ETag;
                SaveState();
            }

            StartReconcile();
        }
        finally
        {
            DownloadProgress = null;
            RefreshStatus();
            _checkGate.Release();
        }
    }

    private async Task<bool> DownloadPluginAsync(UpdateChoice choice, HttpMessageHandler handler, CancellationToken cancellationToken)
    {
        var version = choice.Plugin!;
        var zip = Path.Combine(_paths.DownloadsDir, choice.PluginZip!.Name);
        var sig = Path.Combine(_paths.DownloadsDir, choice.PluginSig!.Name);
        try
        {
            SetStatus(UpdateStatus.Downloading, $"Скачивание ToadTools {version}…");
            await DownloadAsync(handler, choice.PluginSig, sig, cancellationToken).ConfigureAwait(false);
            await DownloadAsync(handler, choice.PluginZip, zip, cancellationToken).ConfigureAwait(false);

            if (!_verifier.Verify(zip, File.ReadAllText(sig)))
            {
                _error = $"Подпись архива {choice.PluginZip.Name} не сходится — файл подменён или повреждён, не ставлю";
                Log.Error(_error);
                return false;
            }

            PluginArchive.Extract(zip, _paths.StagingDir, version);
            lock (_sync)
            {
                _state.StagedPluginVersion = version.ToString();
                SaveState();
            }

            PendingPlugin = version;
            Log.Info($"ToadTools {version} скачан и распакован");
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException
                                              or InvalidPluginArchiveException or UnauthorizedAccessException)
        {
            _error = $"Не удалось скачать ToadTools {version}: {exception.Message}";
            Log.Error(_error, exception);
            return false;
        }
        finally
        {
            TryDeleteFile(zip);
            TryDeleteFile(zip + ".part");
            TryDeleteFile(sig);
        }
    }

    private async Task<bool> DownloadAppAsync(UpdateChoice choice, HttpMessageHandler handler, CancellationToken cancellationToken)
    {
        var version = choice.App!;
        var msi = Path.Combine(_paths.DownloadsDir, choice.AppMsi!.Name);
        var sig = Path.Combine(_paths.DownloadsDir, choice.AppSig!.Name);
        try
        {
            SetStatus(UpdateStatus.Downloading, $"Скачивание программы ToadTools {version}…");
            await DownloadAsync(handler, choice.AppSig, sig, cancellationToken).ConfigureAwait(false);
            await DownloadAsync(handler, choice.AppMsi, msi, cancellationToken).ConfigureAwait(false);
            if (!_verifier.Verify(msi, File.ReadAllText(sig)))
            {
                _error = $"Подпись {choice.AppMsi.Name} не сходится — файл подменён или повреждён, не ставлю";
                Log.Error(_error);
                TryDeleteFile(msi);
                return false;
            }

            foreach (var old in Directory.GetFiles(_paths.DownloadsDir, "ToadTools-App-*.msi"))
            {
                if (!string.Equals(old, msi, StringComparison.OrdinalIgnoreCase)) TryDeleteFile(old);
            }

            lock (_sync)
            {
                _state.DownloadedAppMsi = msi;
                SaveState();
            }

            PendingApp = version;
            PendingAppMsi = msi;
            AppUpdateReady?.Invoke(this, version);
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException
                                              or UnauthorizedAccessException)
        {
            _error = $"Не удалось скачать программу ToadTools {version}: {exception.Message}";
            Log.Error(_error, exception);
            TryDeleteFile(msi);
            return false;
        }
        finally
        {
            TryDeleteFile(sig);
        }
    }

    private Task DownloadAsync(HttpMessageHandler handler, ReleaseAsset asset, string target, CancellationToken cancellationToken)
    {
        var progress = new Progress<double>(value =>
        {
            DownloadProgress = value;
            Changed?.Invoke(this, EventArgs.Empty);
        });
        return new Downloader(handler, _retryDelay).DownloadAsync(asset.DownloadUrl, target, asset.Size, progress, cancellationToken);
    }

    /// <summary>
    ///     Поставить скачанную версию сейчас (настройка «Устанавливать после закрытия Revit» выключена).
    ///     false — Revit запущен, ставить нельзя.
    /// </summary>
    public Task<bool> InstallPluginNowAsync()
    {
        if (_revit.AnyRunning()) return Task.FromResult(false);
        lock (_sync) _awaitingManualInstall = false;
        StartReconcile();
        return Task.FromResult(true);
    }

    /// <summary>Настройки поменялись: пересчитать расписание и годы Revit.</summary>
    public void SettingsChanged()
    {
        Interlocked.Exchange(ref _scheduleWake, NewSignal()).TrySetResult(true);
        StartReconcile();
    }

    /// <summary>Завершается, когда нет ни проверки, ни установки (для тестов и выхода из программы).</summary>
    public async Task WhenSettledAsync()
    {
        while (true)
        {
            Task? reconcile;
            lock (_sync) reconcile = _reconcileRunning ? _reconcileTask : null;
            if (reconcile == null && _checkGate.CurrentCount == 1) return;
            try
            {
                await (reconcile ?? Task.Delay(10)).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
            }
        }
    }

    private void StartReconcile()
    {
        lock (_sync)
        {
            if (_reconcileRunning)
            {
                _reconcileAgain = true;
                Interlocked.Exchange(ref _wake, NewSignal()).TrySetResult(true);
                return;
            }

            _reconcileRunning = true;
            _reconcileTask = Task.Run(() => ReconcileLoopAsync(_lifetime.Token));
        }
    }

    private async Task ReconcileLoopAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (_sync) _reconcileAgain = false;
            try
            {
                await ReconcileOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                _error = $"Не удалось поставить плагин: {exception.Message}";
                Log.Error("Установка плагина упала", exception);
            }

            lock (_sync)
            {
                if (!_reconcileAgain || cancellationToken.IsCancellationRequested)
                {
                    _reconcileRunning = false;
                    _waiting = _installing = false;
                    break;
                }
            }
        }

        RefreshStatus();
    }

    private sealed record Plan(Version Target, string Folder, bool IsNew, IReadOnlyList<int> Deploy, IReadOnlyList<int> Remove);

    private async Task ReconcileOnceAsync(CancellationToken cancellationToken)
    {
        var lockedAttempts = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var wake = _wake.Task;
            var plan = MakePlan();
            if (plan == null) return;

            bool manual;
            lock (_sync) manual = _awaitingManualInstall;
            if (_revit.AnyRunning() || manual)
            {
                _waiting = true;
                RefreshStatus();
                if (plan.IsNew && _announcedReady != plan.Target)
                {
                    _announcedReady = plan.Target;
                    PluginReady?.Invoke(this, plan.Target);
                }

                if (_settings().InstallWhenRevitCloses)
                {
                    lock (_sync) _awaitingManualInstall = false;
                    using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    var closed = _revit.WaitUntilNoneAsync(stop.Token);
                    await Task.WhenAny(closed, wake).ConfigureAwait(false);
                    stop.Cancel();
                    try
                    {
                        await closed.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
                else
                {
                    lock (_sync) _awaitingManualInstall = true;
                    var cancelled = NewSignal();
                    using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
                        await Task.WhenAny(wake, cancelled.Task).ConfigureAwait(false);
                }

                continue;
            }

            _waiting = false;
            _installing = true;
            RefreshStatus();
            var locked = Apply(plan);
            _installing = false;

            if (locked.Count == 0) continue;
            if (_revit.AnyRunning()) continue;

            // Файлы держит не Revit (антивирус, проводник): несколько попыток с паузой, потом ошибка
            if (++lockedAttempts >= LockedAttempts)
            {
                _error = $"Файлы плагина в Revit {string.Join(", ", locked)} заняты другой программой";
                return;
            }

            await Task.Delay(TimeSpan.FromTicks(_retryDelay.Ticks * 15), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Что сделать с плагином сейчас; null — всё на месте.</summary>
    private Plan? MakePlan()
    {
        Version? staged;
        lock (_sync) staged = ParseVersion(_state.StagedPluginVersion);
        var target = staged ?? DeployedPlugin;
        if (target == null) return null;

        var folder = Path.Combine(_paths.StagingDir, target.ToString());
        if (!Directory.Exists(folder))
        {
            if (staged != null)
            {
                Log.Error($"Папка {folder} пропала, версия {staged} будет скачана заново");
                lock (_sync)
                {
                    _state.StagedPluginVersion = null;
                    _state.ETag = null;
                    SaveState();
                }

                PendingPlugin = null;
            }

            return null;
        }

        var settings = _settings();
        var installed = _installedYears();
        var isNew = staged != null;
        var deploy = PluginArchive.Years(folder)
            .Where(year => installed.Contains(year) && settings.IsYearEnabled(year))
            .Where(year => isNew || !_deployer.IsDeployed(year))
            .ToArray();
        var remove = Enumerable.Range(FirstYear, LastYear - FirstYear + 1)
            .Where(year => !settings.IsYearEnabled(year) && _deployer.IsDeployed(year))
            .ToArray();

        if (!isNew && deploy.Length == 0 && remove.Length == 0) return null;
        return new Plan(target, folder, isNew, deploy, remove);
    }

    /// <summary>Ставит и убирает плагин по плану; возвращает годы, где файлы заняты.</summary>
    private IReadOnlyList<int> Apply(Plan plan)
    {
        var result = _deployer.Deploy(plan.Folder, plan.Deploy);
        var locked = result.Locked.ToList();
        foreach (var year in plan.Remove)
        {
            try
            {
                _deployer.Remove(year);
                Log.Info($"ToadTools убран из Revit {year} (выключен в настройках)");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                locked.Add(year);
            }
        }

        if (result.Deployed.Count > 0) Log.Info($"ToadTools {plan.Target} установлен в Revit {string.Join(", ", result.Deployed)}");
        if (locked.Count > 0 || !plan.IsNew) return locked;

        lock (_sync)
        {
            _state.DeployedPluginVersion = plan.Target.ToString();
            _state.StagedPluginVersion = null;
            SaveState();
        }

        DeployedPlugin = plan.Target;
        PendingPlugin = null;
        _error = null;
        if (result.Deployed.Count > 0) PluginInstalled?.Invoke(this, plan.Target);
        return locked;
    }

    /// <summary>
    ///     Разложенная версия: из состояния, если плагин где-то стоит (или его копия лежит в staging — Revit пока не
    ///     найден); иначе версия ToadTools.dll (установка старым MSI); иначе null — плагина нет, надо скачать.
    /// </summary>
    private Version? ComputeDeployed()
    {
        var years = Enumerable.Range(FirstYear, LastYear - FirstYear + 1).Where(_deployer.IsDeployed).ToArray();
        var recorded = ParseVersion(_state.DeployedPluginVersion);
        if (recorded != null && (years.Length > 0 || Directory.Exists(Path.Combine(_paths.StagingDir, recorded.ToString()))))
            return recorded;
        return years.Select(_deployer.DeployedVersion).Where(version => version != null).Max();
    }

    private void RestorePendingApp()
    {
        var msi = _state.DownloadedAppMsi;
        if (msi == null) return;
        var match = AppMsiName.Match(msi);
        if (File.Exists(msi) && match.Success && Version.Parse(match.Groups[1].Value) > _currentApp)
        {
            PendingApp = Version.Parse(match.Groups[1].Value);
            PendingAppMsi = msi;
            return;
        }

        // Уже установлен (или пропал): убрать
        TryDeleteFile(msi);
        _state.DownloadedAppMsi = null;
        SaveState();
    }

    private void RefreshStatus()
    {
        if (_error != null) SetStatus(UpdateStatus.Error, _error);
        else if (_installing) SetStatus(UpdateStatus.Installing, $"Установка ToadTools {PendingPlugin}…");
        else if ((_waiting || ParseVersion(_state.StagedPluginVersion) != null) && PendingPlugin != null)
            SetStatus(UpdateStatus.WaitingForRevit, _settings().InstallWhenRevitCloses
                ? $"ToadTools {PendingPlugin} установится после закрытия Revit"
                : $"ToadTools {PendingPlugin} скачан — нажмите «Установить сейчас»");
        else if (_waiting) SetStatus(UpdateStatus.WaitingForRevit, "Изменения применятся после закрытия Revit");
        else if (PendingApp != null) SetStatus(UpdateStatus.AppUpdateReady, $"Готова новая версия программы {PendingApp}");
        else if (PendingPlugin != null && ParseVersion(_state.StagedPluginVersion) == null)
            SetStatus(UpdateStatus.UpdateAvailable, $"Доступна версия ToadTools {PendingPlugin}");
        else if (LastCheckUtc != null || DeployedPlugin != null)
            SetStatus(UpdateStatus.UpToDate, DeployedPlugin != null ? $"ToadTools {DeployedPlugin}: последняя версия" : "Обновлений нет");
        else SetStatus(UpdateStatus.Idle, "Ещё не проверялось");
    }

    private void SetStatus(UpdateStatus status, string? text)
    {
        Status = status;
        StatusText = text;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SaveState()
    {
        try
        {
            _stateStore.Save(_state);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error("Не удалось сохранить состояние", exception);
        }
    }

    private static Version? ParseVersion(string? text) => Version.TryParse(text, out var version) ? version : null;

    private static Version? Max(Version? a, Version? b) => a == null ? b : b == null ? a : a > b ? a : b;

    private static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void TryDeleteFile(string file)
    {
        try
        {
            if (File.Exists(file)) File.Delete(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        Interlocked.Exchange(ref _wake, NewSignal()).TrySetResult(true);
    }
}
