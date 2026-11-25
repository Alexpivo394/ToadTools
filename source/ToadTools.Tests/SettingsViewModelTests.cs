using System.ComponentModel;
using Shouldly;
using ToadTools.Connection;
using ToadTools.Connection.ViewModels;
using ToadTools.UI.Services;

namespace ToadTools.Tests;

public class SettingsViewModelTests
{
    private sealed class FakeAppearance : IThemePreference
    {
        private bool _isCompact;
        private DensityChoice _density;

        public ThemeChoice Theme { get; set; } = ThemeChoice.Dark;

        public DensityChoice Density
        {
            get => _density;
            set
            {
                _density = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Density)));
            }
        }

        public bool IsCompact
        {
            get => _isCompact;
            set
            {
                _isCompact = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCompact)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class FakeShell : ISettingsShell
    {
        public List<string> Calls { get; } = [];
        public void OpenFolder(string path) => Calls.Add("folder " + path);
        public void OpenUrl(string url) => Calls.Add("url " + url);
        public void ShowFile(string path) => Calls.Add("file " + path);
    }

    private static (SettingsViewModel Model, FakeAppearance Appearance, SettingsPaths Paths, TempFolder Temp) Create(
        Action<SettingsPaths>? prepare = null)
    {
        var temp = new TempFolder();
        var store = new ConnectionStore(temp.File("toadflow.json"));
        var client = new ToadFlowClient(store, new FakeHttp((_, _) => throw new HttpRequestException("нет сети")));
        var appearance = new FakeAppearance();
        var paths = new SettingsPaths(temp.File("AppData"), temp.File("Logs"), temp.File("images"), temp.File("Desktop"));
        prepare?.Invoke(paths);
        var model = new SettingsViewModel(new ConnectionViewModel(client, store), appearance, new FakeShell(), paths, "2024", "4.7.1");
        return (model, appearance, paths, temp);
    }

    [Test]
    public async Task Theme_Set_UpdatesPreference()
    {
        var (model, appearance, _, temp) = Create();
        using var _ = temp;
        var raised = new List<string?>();
        model.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        model.Theme = ThemeChoice.Light;

        appearance.Theme.ShouldBe(ThemeChoice.Light);
        model.Theme.ShouldBe(ThemeChoice.Light);
        raised.ShouldContain(nameof(SettingsViewModel.Theme));
        await Task.CompletedTask;
    }

    [Test]
    public async Task Density_Auto_ShowsWhatAutoChose()
    {
        var (model, appearance, _, temp) = Create();
        using var _ = temp;
        appearance.Density = DensityChoice.Auto;
        appearance.IsCompact = true;

        model.DensityHint.ShouldContain("сейчас: компактный");

        appearance.IsCompact = false;
        model.DensityHint.ShouldContain("сейчас: обычный");

        model.Density = DensityChoice.Normal;
        appearance.Density.ShouldBe(DensityChoice.Normal);
        model.DensityHint.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task ClearThumbnails_EmptiesFolder_UpdatesSize()
    {
        var (model, _, paths, temp) = Create(paths =>
        {
            Directory.CreateDirectory(paths.Thumbnails);
            File.WriteAllBytes(Path.Combine(paths.Thumbnails, "a.png"), new byte[2048]);
            File.WriteAllBytes(Path.Combine(paths.Thumbnails, "b.png"), new byte[2048]);
        });
        using var _ = temp;
        model.ThumbnailsSize.ShouldBe("4 КБ");

        model.ClearThumbnailsCommand.Execute(null);

        Directory.GetFiles(paths.Thumbnails).ShouldBeEmpty();
        model.ThumbnailsSize.ShouldBe("пусто");
        await Task.CompletedTask;
    }

    [Test]
    public async Task About_ShowsVersions()
    {
        var (model, _, _, temp) = Create();
        using var _ = temp;

        model.ToadToolsVersion.ShouldBe("4.7.1");
        model.RevitVersion.ShouldBe("2024");
        model.VersionsText().ShouldContain("ToadTools: 4.7.1");
        model.VersionsText().ShouldContain("Revit: 2024");
        model.VersionsText().ShouldContain("Сервер ToadFlow: не подключено");
        await Task.CompletedTask;
    }
}
