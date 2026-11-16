using System;
using System.Collections.Generic;
using System.Linq;
using WixToolset.Dtf.WindowsInstaller;

namespace Installer.UI;

/// <summary>What the installer is asked to do when it starts.</summary>
public enum SetupMode
{
    /// <summary>ToadTools is not installed.</summary>
    Install,

    /// <summary>An older ToadTools program, or the old add-in package, is installed; this package replaces it.</summary>
    Upgrade,

    /// <summary>This version is already installed: repair or remove it.</summary>
    Maintenance,

    /// <summary>Removal started from "Programs and Features" or <c>msiexec /x</c>.</summary>
    Remove
}

/// <summary>Everything the windows need from the MSI session, read once while the session handle is valid.</summary>
public sealed class SetupInfo
{
    /// <summary>The old add-in package (4.0.x and earlier): the program replaces it.</summary>
    public const string LegacyPluginUpgradeCode = "{460B3098-3D8B-44D4-B7CE-729124B81606}";

    /// <summary>Version of the ToadTools program in this package.</summary>
    public string Version { get; set; } = "";

    /// <summary>Version of the add-in released with this package: the program downloads it.</summary>
    public string PluginVersion { get; set; } = "";

    public string PreviousVersion { get; set; } = "";

    /// <summary>The old add-in package is installed: its files in the Revit folders are removed during the upgrade.</summary>
    public bool ReplacesLegacyPlugin { get; set; }

    public SetupMode Mode { get; set; }

    /// <summary>Up to three lines of the changelog of the add-in version.</summary>
    public IReadOnlyList<string> Highlights { get; set; } = Array.Empty<string>();

    /// <summary>Revit versions installed on the computer.</summary>
    public IReadOnlyList<int> RevitYears { get; set; } = Array.Empty<int>();

    /// <summary>The .msi file, to start it again after a failure.</summary>
    public string Package { get; set; } = "";

    public string ReleaseUrl => "https://github.com/Alexpivo394/ToadTools/releases/tag/" + PluginVersion;
    public const string HelpUrl = "https://github.com/Alexpivo394/ToadTools/wiki";
    public const string ChangelogUrl = "https://github.com/Alexpivo394/ToadTools/blob/master/Changelog.md";

    /// <summary>TOAD_HIGHLIGHTS is Base64 of UTF-8: the package code page has no Cyrillic letters.</summary>
    private static string Decode(string base64)
    {
        if (string.IsNullOrEmpty(base64)) return "";
        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            return "";
        }
    }

    /// <summary>Reads the session: versions, mode, installed Revit versions.</summary>
    public static SetupInfo Read(Session session)
    {
        var info = new SetupInfo
        {
            Version = session["TOAD_VERSION"],
            PluginVersion = session["TOAD_PLUGIN_VERSION"],
            Highlights = Decode(session["TOAD_HIGHLIGHTS"]).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Take(3).ToArray(),
            RevitYears = Enumerable.Range(2020, 11).Where(RevitDetector.IsInstalled).ToArray(),
            Package = session["OriginalDatabase"]
        };

        if (string.Equals(session["REMOVE"], "ALL", StringComparison.OrdinalIgnoreCase))
        {
            info.Mode = SetupMode.Remove;
            return info;
        }

        if (!string.IsNullOrEmpty(session["Installed"]))
        {
            info.Mode = SetupMode.Maintenance;
            return info;
        }

        var previous = Related(session["UpgradeCode"], session["ProductCode"]);
        var legacy = Related(LegacyPluginUpgradeCode, session["ProductCode"]);
        info.ReplacesLegacyPlugin = legacy != null;
        info.Mode = previous == null && legacy == null ? SetupMode.Install : SetupMode.Upgrade;
        info.PreviousVersion = previous?.ToString(3) ?? legacy?.ToString(3) ?? "";
        return info;
    }

    private static Version? Related(string upgradeCode, string productCode) =>
        ProductInstallation.GetRelatedProducts(upgradeCode)
            .Where(p => p.ProductCode != productCode && p.IsInstalled)
            .Select(p => p.ProductVersion)
            .Where(v => v != null)
            .OrderByDescending(v => v)
            .FirstOrDefault();
}
