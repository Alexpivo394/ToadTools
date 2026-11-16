using Shouldly;
using ToadTools.Desktop.Core.Settings;
using ToadTools.Desktop.Core.State;

namespace ToadTools.Desktop.Tests.Settings;

public class StoresTests
{
    [Test]
    public void Settings_SaveLoad_RoundTrip()
    {
        using var temp = new TempFolder();
        var store = new SettingsStore(temp.Combine("roaming", "settings.json"));
        var settings = new UpdateSettings
        {
            Interval = CheckInterval.Hours12,
            AutoDownload = false,
            InstallWhenRevitCloses = false,
            AutoUpdateApp = false,
            Notifications = false,
            StartWithWindows = false,
            Proxy = ProxyMode.Custom,
            ProxyAddress = "http://proxy:3128"
        };
        settings.RevitYears[2024] = false;

        store.Save(settings);
        var loaded = store.Load();

        loaded.Interval.ShouldBe(CheckInterval.Hours12);
        loaded.AutoDownload.ShouldBeFalse();
        loaded.InstallWhenRevitCloses.ShouldBeFalse();
        loaded.AutoUpdateApp.ShouldBeFalse();
        loaded.Notifications.ShouldBeFalse();
        loaded.StartWithWindows.ShouldBeFalse();
        loaded.Proxy.ShouldBe(ProxyMode.Custom);
        loaded.ProxyAddress.ShouldBe("http://proxy:3128");
        loaded.IsYearEnabled(2024).ShouldBeFalse();
        File.Exists(temp.Combine("roaming", "settings.json.tmp")).ShouldBeFalse();
    }

    [Test]
    public void Settings_NoFile_Defaults()
    {
        using var temp = new TempFolder();

        var settings = new SettingsStore(temp.Combine("settings.json")).Load();

        settings.Interval.ShouldBe(CheckInterval.Hours6);
        settings.AutoDownload.ShouldBeTrue();
        settings.InstallWhenRevitCloses.ShouldBeTrue();
        settings.AutoUpdateApp.ShouldBeTrue();
        settings.Notifications.ShouldBeTrue();
        settings.StartWithWindows.ShouldBeTrue();
        settings.Proxy.ShouldBe(ProxyMode.System);
        settings.IsYearEnabled(2025).ShouldBeTrue();
    }

    [Test]
    public void Settings_BrokenJson_DefaultsAndBackup()
    {
        using var temp = new TempFolder();
        var file = temp.Combine("settings.json");
        File.WriteAllText(file, "{ broken");

        new SettingsStore(file).Load().Interval.ShouldBe(CheckInterval.Hours6);

        File.Exists(file + ".bad").ShouldBeTrue();
    }

    [Test]
    [Arguments(CheckInterval.Hours1, 1)]
    [Arguments(CheckInterval.Hours3, 3)]
    [Arguments(CheckInterval.Hours6, 6)]
    [Arguments(CheckInterval.Hours12, 12)]
    [Arguments(CheckInterval.Hours24, 24)]
    public void Interval_ToTimeSpan(CheckInterval interval, int hours) => interval.ToTimeSpan().ShouldBe(TimeSpan.FromHours(hours));

    [Test]
    public void Interval_ManualAndStartup_NoPeriod()
    {
        CheckInterval.Manual.ToTimeSpan().ShouldBeNull();
        CheckInterval.OnStartup.ToTimeSpan().ShouldBeNull();
    }

    [Test]
    public void State_SaveLoad_RoundTrip()
    {
        using var temp = new TempFolder();
        var store = new StateStore(temp.Combine("state.json"));
        var now = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);

        store.Save(new UpdateState
        {
            ETag = "\"abc\"", LastCheckUtc = now, DeployedPluginVersion = "4.0.2", StagedPluginVersion = "4.0.3",
            DownloadedAppMsi = @"C:\x.msi", ReleaseNotes = "notes"
        });
        var state = store.Load();

        state.ETag.ShouldBe("\"abc\"");
        state.LastCheckUtc.ShouldBe(now);
        state.DeployedPluginVersion.ShouldBe("4.0.2");
        state.StagedPluginVersion.ShouldBe("4.0.3");
        state.DownloadedAppMsi.ShouldBe(@"C:\x.msi");
        state.ReleaseNotes.ShouldBe("notes");
    }

    [Test]
    public void State_NoFile_Empty() => new StateStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")).Load().ETag.ShouldBeNull();

    [Test]
    [Arguments(ProxyMode.System)]
    [Arguments(ProxyMode.None)]
    [Arguments(ProxyMode.Custom)]
    public void Proxy_CreatesHandler(ProxyMode mode)
    {
        var settings = new UpdateSettings { Proxy = mode, ProxyAddress = "http://proxy:3128" };

        using var handler = (HttpClientHandler)settings.CreateHandler();

        handler.UseProxy.ShouldBe(mode != ProxyMode.None);
        if (mode == ProxyMode.Custom) handler.Proxy!.GetProxy(new Uri("https://api.github.com")).ShouldBe(new Uri("http://proxy:3128"));
    }

    [Test]
    public void Proxy_CustomWithBadAddress_FallsBackToSystem()
    {
        using var handler = (HttpClientHandler)new UpdateSettings { Proxy = ProxyMode.Custom, ProxyAddress = "::bad" }.CreateHandler();

        handler.UseProxy.ShouldBeTrue();
        handler.Proxy.ShouldBeNull();
    }
}
