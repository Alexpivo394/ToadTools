using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using WixToolset.Dtf.WindowsInstaller;

namespace Installer.UI;

/// <summary>What the installer is asked to do when it starts.</summary>
public enum SetupMode
{
    /// <summary>ToadTools is not installed.</summary>
    Install,

    /// <summary>An older ToadTools is installed; this package replaces it.</summary>
    Upgrade,

    /// <summary>This version is already installed: change, repair or remove it.</summary>
    Maintenance,

    /// <summary>Removal started from "Programs and Features" or <c>msiexec /x</c>.</summary>
    Remove
}

/// <summary>A Revit version the package carries the add-in for.</summary>
public sealed class RevitTarget
{
    public RevitTarget(int year, string feature, bool found, bool installed)
    {
        Year = year;
        Feature = feature;
        Found = found;
        Installed = installed;
    }

    public int Year { get; }

    /// <summary>Id of the MSI feature with the add-in files for this version: <c>Revit2024</c>.</summary>
    public string Feature { get; }

    /// <summary>Revit of this version is installed on the computer.</summary>
    public bool Found { get; }

    /// <summary>ToadTools is already installed for this version.</summary>
    public bool Installed { get; }
}

/// <summary>Everything the windows need from the MSI session, read once while the session handle is valid.</summary>
public sealed class SetupInfo
{
    public string Version { get; set; } = "";
    public string PreviousVersion { get; set; } = "";
    public SetupMode Mode { get; set; }
    public bool PerMachine { get; set; }

    /// <summary>File name of the other package (all users / current user), for the hint on the first page.</summary>
    public string OtherPackage { get; set; } = "";

    /// <summary>Up to three lines of the changelog of this version.</summary>
    public IReadOnlyList<string> Highlights { get; set; } = Array.Empty<string>();

    public IReadOnlyList<RevitTarget> Targets { get; set; } = Array.Empty<RevitTarget>();

    /// <summary>The .msi file, to start it again after a failure.</summary>
    public string Package { get; set; } = "";

    public string ReleaseUrl => "https://github.com/Alexpivo394/ToadTools/releases/tag/" + Version;
    public const string HelpUrl = "https://github.com/Alexpivo394/ToadTools/wiki";
    public const string ChangelogUrl = "https://github.com/Alexpivo394/ToadTools/blob/master/Changelog.md";

    private static readonly Regex FeatureId = new(@"^Revit(\d{4})$");

    /// <summary>Reads the session: product version, mode, Revit versions of the package and their state.</summary>
    public static SetupInfo Read(Session session)
    {
        var addins = Environment.ExpandEnvironmentVariables(session["TOAD_ADDINS"]);
        var targets = new List<RevitTarget>();
        using (var view = session.Database.OpenView("SELECT `Feature` FROM `Feature`"))
        {
            view.Execute();
            foreach (var record in view)
            {
                using (record)
                {
                    var feature = record.GetString(1);
                    var match = FeatureId.Match(feature);
                    if (!match.Success) continue;
                    var year = int.Parse(match.Groups[1].Value);
                    var installed = addins.Length > 0 && File.Exists(Path.Combine(addins, year.ToString(), "ToadTools.addin"));
                    targets.Add(new RevitTarget(year, feature, RevitDetector.IsInstalled(year), installed));
                }
            }
        }

        var info = new SetupInfo
        {
            Version = session["TOAD_VERSION"],
            PerMachine = session["ALLUSERS"] == "1",
            OtherPackage = session["TOAD_OTHER_MSI"],
            Highlights = session["TOAD_HIGHLIGHTS"].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Take(3).ToArray(),
            Targets = targets.OrderBy(t => t.Year).ToArray(),
            Package = session["OriginalDatabase"]
        };

        if (string.Equals(session["REMOVE"], "ALL", StringComparison.OrdinalIgnoreCase))
        {
            info.Mode = SetupMode.Remove;
        }
        else if (!string.IsNullOrEmpty(session["Installed"]))
        {
            info.Mode = SetupMode.Maintenance;
        }
        else
        {
            var previous = ProductInstallation.GetRelatedProducts(session["UpgradeCode"])
                .Where(p => p.ProductCode != session["ProductCode"] && p.IsInstalled)
                .Select(p => p.ProductVersion)
                .Where(v => v != null)
                .OrderByDescending(v => v)
                .FirstOrDefault();
            info.Mode = previous == null ? SetupMode.Install : SetupMode.Upgrade;
            info.PreviousVersion = previous == null ? "" : previous.ToString(3);
        }

        return info;
    }
}
