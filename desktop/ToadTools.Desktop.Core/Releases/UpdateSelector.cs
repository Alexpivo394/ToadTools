using System.Text.RegularExpressions;

namespace ToadTools.Desktop.Core.Releases;

/// <summary>Что из релиза ставить: плагин (null — не нужно) и программу (null — не нужно), каждое с файлом подписи.</summary>
public sealed record UpdateChoice(
    Version? Plugin,
    ReleaseAsset? PluginZip,
    ReleaseAsset? PluginSig,
    Version? App,
    ReleaseAsset? AppMsi,
    ReleaseAsset? AppSig);

/// <summary>
///     Выбор обновлений в релизе. Плагин — версия тега и файлы ToadTools-{тег}.zip + .zip.sig. Программа — файл
///     ToadTools-App-{версия}.msi + .msi.sig (MSI лежит в каждом релизе, ставится, только когда версия выше текущей).
///     Без файла подписи часть не выбирается.
/// </summary>
public static class UpdateSelector
{
    private static readonly Regex AppMsi = new(@"^ToadTools-App-(\d+\.\d+\.\d+)\.msi$", RegexOptions.IgnoreCase);

    public static UpdateChoice Choose(ReleaseInfo release, Version? deployedPlugin, Version currentApp)
    {
        Version? plugin = null;
        ReleaseAsset? zip = null, zipSig = null;
        if (TryParseTag(release.Tag, out var tagVersion) && (deployedPlugin == null || tagVersion > Normalize(deployedPlugin)))
        {
            zip = Find(release, $"ToadTools-{tagVersion}.zip");
            zipSig = Find(release, $"ToadTools-{tagVersion}.zip.sig");
            if (zip != null && zipSig != null) plugin = tagVersion;
            else zip = zipSig = null;
        }

        Version? app = null;
        ReleaseAsset? msi = null, msiSig = null;
        foreach (var asset in release.Assets)
        {
            var match = AppMsi.Match(asset.Name);
            if (!match.Success) continue;
            var version = Version.Parse(match.Groups[1].Value);
            if (version <= Normalize(currentApp) || (app != null && version <= app)) continue;
            var sig = Find(release, asset.Name + ".sig");
            if (sig == null) continue;
            (app, msi, msiSig) = (version, asset, sig);
        }

        return new UpdateChoice(plugin, zip, zipSig, app, msi, msiSig);
    }

    /// <summary>Тег 4.0.3 или v4.0.3 → 4.0.3; prerelease-теги (4.0.3-beta) не разбираются.</summary>
    public static bool TryParseTag(string tag, out Version version)
    {
        version = new Version();
        var text = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag.Substring(1) : tag;
        if (!Regex.IsMatch(text, @"^\d+\.\d+\.\d+$") || !Version.TryParse(text, out var parsed)) return false;
        version = parsed;
        return true;
    }

    /// <summary>Версия сборки 4.0.3.0 сравнивается с тегом 4.0.3 как равная.</summary>
    public static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private static ReleaseAsset? Find(ReleaseInfo release, string name) =>
        release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, name, StringComparison.OrdinalIgnoreCase));
}
