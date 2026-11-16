using System.Net;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Shouldly;
using ToadTools.Desktop.Core;
using ToadTools.Desktop.Core.Plugin;
using ToadTools.Desktop.Core.Revit;
using ToadTools.Desktop.Core.Security;
using ToadTools.Desktop.Core.Settings;
using ToadTools.Desktop.Core.State;
using ToadTools.Desktop.Core.Updates;
using ToadTools.Desktop.Tests.Plugin;
using ToadTools.Desktop.Tests.Revit;
using ToadTools.Desktop.Tests.Security;

namespace ToadTools.Desktop.Tests.Updates;

public class UpdateServiceTests
{
    /// <summary>Стенд: «GitHub» на фейковом HTTP, подписанный архив плагина, фейковый Revit, временные папки.</summary>
    private sealed class Bench : IDisposable
    {
        private readonly TempFolder _temp = new();
        private readonly ECDsa _key = TestKeys.Create();
        private readonly Dictionary<string, byte[]> _files = new();
        private string _tag = "4.0.3";

        public AppPaths Paths { get; }
        public UpdateSettings Settings { get; } = new();
        public FakeRevitSource Revit { get; } = new();
        public List<int> Installed { get; } = [2025];
        public bool Offline { get; set; }
        public HttpStatusCode? LatestStatus { get; set; }
        public int LatestRequests { get; private set; }

        public Bench()
        {
            Paths = new AppPaths(_temp.Combine("local"), _temp.Combine("roaming"), _temp.Combine("Addins"));
            Release("4.0.3", 2024, 2025);
        }

        /// <summary>Публикует релиз: архив плагина для годов + подпись, по желанию MSI программы.</summary>
        public void Release(string tag, int[]? years = null, string? appVersion = null, bool badSignature = false)
        {
            years ??= [2025];
            _tag = tag;
            _files.Clear();
            var zip = PluginArchiveTests.MakeZip(_temp.Path, $"src-{tag}-{Guid.NewGuid():N}.zip", years.SelectMany(y => PluginArchiveTests.Year(y, tag)).ToArray());
            Add($"ToadTools-{tag}.zip", File.ReadAllBytes(zip), badSignature);
            if (appVersion != null) Add($"ToadTools-App-{appVersion}.msi", [7, 7, 7, 7], false);
        }

        public void Release(string tag, params int[] years) => Release(tag, years, null);

        private void Add(string name, byte[] content, bool badSignature)
        {
            _files[name] = content;
            var file = _temp.Combine("sign-" + name);
            File.WriteAllBytes(file, badSignature ? [..content, 1] : content);
            _files[name + ".sig"] = System.Text.Encoding.ASCII.GetBytes(TestKeys.Sign(_key, file));
        }

        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            if (Offline) throw new HttpRequestException("offline");
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/releases/latest"))
            {
                LatestRequests++;
                if (LatestStatus is { } status) return new HttpResponseMessage(status);
                var assets = new JArray(_files.Select(file => new JObject
                {
                    ["name"] = file.Key,
                    ["size"] = file.Value.Length,
                    ["browser_download_url"] = $"https://files.test/{file.Key}"
                }));
                return FakeHandler.Json(new JObject { ["tag_name"] = _tag, ["body"] = $"notes {_tag}", ["assets"] = assets }.ToString());
            }

            var name = path.TrimStart('/');
            return _files.TryGetValue(name, out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        public UpdateService Service(Version? app = null) => new(
            Paths,
            () => Settings,
            new StateStore(Paths.StateFile),
            _ => new FakeHandler(Respond),
            new SignatureVerifier(TestKeys.PublicQ(_key)),
            new PluginDeployer(Paths),
            new RevitWatcher(Revit, TimeSpan.FromMilliseconds(20)),
            () => Installed,
            app ?? new Version(1, 0, 0),
            retryDelay: TimeSpan.Zero);

        public string Dll(int year) => Path.Combine(Paths.AddinsYear(year), "ToadTools", "ToadTools.dll");

        public void Dispose()
        {
            _key.Dispose();
            _temp.Dispose();
        }
    }

    private static async Task Settled(UpdateService service) => await service.WhenSettledAsync().WaitAsync(TimeSpan.FromSeconds(5));

    private static async Task WaitingForRevit(UpdateService service)
    {
        for (var i = 0; i < 200 && service.Status != UpdateStatus.WaitingForRevit; i++) await Task.Delay(10);
        await Task.Delay(50);
        service.Status.ShouldBe(UpdateStatus.WaitingForRevit);
    }

    [Test]
    public async Task NewRelease_NoRevit_Installs()
    {
        using var bench = new Bench();
        using var service = bench.Service();
        Version? installed = null;
        service.PluginInstalled += (_, version) => installed = version;

        await service.CheckNowAsync(userInitiated: false);
        await Settled(service);

        File.ReadAllText(bench.Dll(2025)).ShouldBe("dll 4.0.3");
        installed.ShouldBe(new Version(4, 0, 3));
        service.DeployedPlugin.ShouldBe(new Version(4, 0, 3));
        service.Status.ShouldBe(UpdateStatus.UpToDate);
        new StateStore(bench.Paths.StateFile).Load().DeployedPluginVersion.ShouldBe("4.0.3");
        service.ReleaseNotes.ShouldBe("notes 4.0.3");
    }

    [Test]
    public async Task RevitRunning_WaitsForExit_ThenInstalls()
    {
        using var bench = new Bench();
        var revit = bench.Revit.Start();
        using var service = bench.Service();
        Version? ready = null;
        service.PluginReady += (_, version) => ready = version;

        await service.CheckNowAsync(userInitiated: false);
        await Task.Delay(100);

        service.Status.ShouldBe(UpdateStatus.WaitingForRevit);
        ready.ShouldBe(new Version(4, 0, 3));
        File.Exists(bench.Dll(2025)).ShouldBeFalse();

        revit.Exit();
        await Settled(service);

        File.ReadAllText(bench.Dll(2025)).ShouldBe("dll 4.0.3");
        service.Status.ShouldBe(UpdateStatus.UpToDate);
    }

    [Test]
    public async Task BadSignature_Error_NothingStagedOrDeployed()
    {
        using var bench = new Bench();
        bench.Release("4.0.3", [2025], badSignature: true);
        using var service = bench.Service();

        await service.CheckNowAsync(userInitiated: false);
        await Settled(service);

        service.Status.ShouldBe(UpdateStatus.Error);
        service.StatusText!.ShouldContain("Подпись");
        File.Exists(bench.Dll(2025)).ShouldBeFalse();
        (Directory.Exists(bench.Paths.StagingDir) ? Directory.GetDirectories(bench.Paths.StagingDir) : []).ShouldBeEmpty();
        (Directory.Exists(bench.Paths.DownloadsDir) ? Directory.GetFiles(bench.Paths.DownloadsDir) : []).ShouldBeEmpty();
    }

    [Test]
    public async Task GitHubForbidden_Error_ThenNextCheckWorks()
    {
        using var bench = new Bench();
        bench.LatestStatus = HttpStatusCode.Forbidden;
        using var service = bench.Service();

        await service.CheckNowAsync(userInitiated: true);
        service.Status.ShouldBe(UpdateStatus.Error);

        bench.LatestStatus = null;
        await service.CheckNowAsync(userInitiated: true);
        await Settled(service);

        File.ReadAllText(bench.Dll(2025)).ShouldBe("dll 4.0.3");
    }

    [Test]
    public async Task Offline_Error_NoThrow()
    {
        using var bench = new Bench();
        bench.Offline = true;
        using var service = bench.Service();

        await service.CheckNowAsync(userInitiated: false);

        service.Status.ShouldBe(UpdateStatus.Error);
        service.StatusText!.ShouldContain("offline");
    }

    [Test]
    public async Task Restart_WithStagedVersion_InstallsWithoutNetwork()
    {
        using var bench = new Bench();
        var revit = bench.Revit.Start();
        using (var first = bench.Service())
        {
            await first.CheckNowAsync(userInitiated: false);
            await WaitingForRevit(first);
        }

        revit.Exit();
        bench.Offline = true;
        using var second = bench.Service();
        using var stop = new CancellationTokenSource();
        bench.Settings.Interval = CheckInterval.Manual;
        var run = second.RunAsync(stop.Token);
        await Task.Delay(200);
        await Settled(second);

        File.ReadAllText(bench.Dll(2025)).ShouldBe("dll 4.0.3");
        stop.Cancel();
        await run;
    }

    [Test]
    public async Task DisabledYear_NotDeployed_AndRemovedWhenTurnedOff()
    {
        using var bench = new Bench();
        bench.Installed.Add(2024);
        bench.Release("4.0.3", 2024, 2025);
        bench.Settings.RevitYears[2024] = false;
        using var service = bench.Service();

        await service.CheckNowAsync(userInitiated: false);
        await Settled(service);

        File.Exists(bench.Dll(2024)).ShouldBeFalse();
        File.Exists(bench.Dll(2025)).ShouldBeTrue();

        bench.Settings.RevitYears[2024] = true;
        service.SettingsChanged();
        await Task.Delay(100);
        await Settled(service);
        File.ReadAllText(bench.Dll(2024)).ShouldBe("dll 4.0.3");

        bench.Settings.RevitYears[2025] = false;
        service.SettingsChanged();
        await Task.Delay(100);
        await Settled(service);
        File.Exists(bench.Dll(2025)).ShouldBeFalse();
        File.Exists(Path.Combine(bench.Paths.AddinsYear(2025), "ToadTools.addin")).ShouldBeFalse();
    }

    [Test]
    public async Task NewerAppMsi_AppUpdateReady()
    {
        using var bench = new Bench();
        bench.Release("4.0.3", [2025], appVersion: "1.1.0");
        using var service = bench.Service(new Version(1, 0, 0));
        Version? app = null;
        service.AppUpdateReady += (_, version) => app = version;

        await service.CheckNowAsync(userInitiated: false);
        await Settled(service);

        app.ShouldBe(new Version(1, 1, 0));
        service.PendingApp.ShouldBe(new Version(1, 1, 0));
        File.ReadAllBytes(service.PendingAppMsi!).ShouldBe([7, 7, 7, 7]);
    }

    [Test]
    public async Task SameAppMsi_NoAppUpdate()
    {
        using var bench = new Bench();
        bench.Release("4.0.3", [2025], appVersion: "1.0.0");
        using var service = bench.Service(new Version(1, 0, 0));

        await service.CheckNowAsync(userInitiated: false);
        await Settled(service);

        service.PendingApp.ShouldBeNull();
    }

    [Test]
    public async Task AutoDownloadOff_OnlyNotifies_ManualCheckDownloads()
    {
        using var bench = new Bench();
        bench.Settings.AutoDownload = false;
        using var service = bench.Service();
        Version? available = null;
        service.PluginAvailable += (_, version) => available = version;

        await service.CheckNowAsync(userInitiated: false);
        await Settled(service);

        available.ShouldBe(new Version(4, 0, 3));
        service.PendingPlugin.ShouldBe(new Version(4, 0, 3));
        File.Exists(bench.Dll(2025)).ShouldBeFalse();

        await service.CheckNowAsync(userInitiated: true);
        await Settled(service);
        File.ReadAllText(bench.Dll(2025)).ShouldBe("dll 4.0.3");
    }

    [Test]
    public async Task InstallWhenRevitClosesOff_WaitsForInstallNow()
    {
        using var bench = new Bench();
        bench.Settings.InstallWhenRevitCloses = false;
        var revit = bench.Revit.Start();
        using var service = bench.Service();

        await service.CheckNowAsync(userInitiated: false);
        await WaitingForRevit(service);
        revit.Exit();
        await Task.Delay(150);

        File.Exists(bench.Dll(2025)).ShouldBeFalse();
        service.Status.ShouldBe(UpdateStatus.WaitingForRevit);

        (await service.InstallPluginNowAsync()).ShouldBeTrue();
        await Settled(service);
        File.ReadAllText(bench.Dll(2025)).ShouldBe("dll 4.0.3");
    }

    [Test]
    public async Task InstallNow_RevitRunning_Refused()
    {
        using var bench = new Bench();
        bench.Revit.Start();
        using var service = bench.Service();
        await service.CheckNowAsync(userInitiated: false);

        (await service.InstallPluginNowAsync()).ShouldBeFalse();
    }

    [Test]
    public async Task SecondCheck_SendsETag_NotModifiedKeepsUpToDate()
    {
        using var bench = new Bench();
        using var service = bench.Service();
        await service.CheckNowAsync(userInitiated: false);
        await Settled(service);

        bench.LatestStatus = HttpStatusCode.NotModified;
        await service.CheckNowAsync(userInitiated: false);

        service.Status.ShouldBe(UpdateStatus.UpToDate);
        bench.LatestRequests.ShouldBe(2);
    }

    [Test]
    public async Task Schedule_Manual_DoesNotCheck_UntilIntervalTurnedOn()
    {
        using var bench = new Bench();
        using (var first = bench.Service())
        {
            await first.CheckNowAsync(userInitiated: false);
            await Settled(first);
        }

        bench.Settings.Interval = CheckInterval.Manual;
        using var service = bench.Service();
        using var stop = new CancellationTokenSource();
        var run = service.RunAsync(stop.Token);
        await Task.Delay(150);
        bench.LatestRequests.ShouldBe(1);

        bench.Settings.Interval = CheckInterval.Hours1;
        new StateStore(bench.Paths.StateFile).Load().LastCheckUtc.ShouldNotBeNull();
        service.SettingsChanged();
        await Task.Delay(150);
        bench.LatestRequests.ShouldBe(1); // проверяли только что — следующая через час

        stop.Cancel();
        await run;
    }

    [Test]
    public async Task Schedule_OverdueInterval_ChecksOnStart()
    {
        using var bench = new Bench();
        bench.Settings.Interval = CheckInterval.Hours1;
        new StateStore(bench.Paths.StateFile).Save(new UpdateState { LastCheckUtc = DateTime.UtcNow.AddHours(-2) });
        using var service = bench.Service();
        using var stop = new CancellationTokenSource();

        var run = service.RunAsync(stop.Token);
        await Task.Delay(300);
        await Settled(service);

        bench.LatestRequests.ShouldBeGreaterThanOrEqualTo(1);
        File.ReadAllText(bench.Dll(2025)).ShouldBe("dll 4.0.3");
        stop.Cancel();
        await run;
    }

    [Test]
    public async Task NewerRelease_AfterInstall_UpdatesAgain()
    {
        using var bench = new Bench();
        using var service = bench.Service();
        await service.CheckNowAsync(userInitiated: false);
        await Settled(service);

        bench.Release("4.0.4", 2025);
        await service.CheckNowAsync(userInitiated: false);
        await Settled(service);

        File.ReadAllText(bench.Dll(2025)).ShouldBe("dll 4.0.4");
        Directory.GetDirectories(bench.Paths.StagingDir).Select(Path.GetFileName).ShouldBe(["4.0.4"]);
    }
}
