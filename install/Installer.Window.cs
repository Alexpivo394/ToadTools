using System.Text.RegularExpressions;
using System.Xml.Linq;
using WixSharp;

namespace Installer;

/// <summary>
///     The installer window (install/Installer.UI): an MSI embedded UI on .NET Framework with the ToadDesign look.
///     WixSharp.Core cannot pack a .NET Framework assembly, so Installer.UI packs itself (WixToolset.Dtf.CustomAction)
///     and the package is put into the MsiEmbeddedUI table here.
/// </summary>
public static partial class Window
{
    /// <summary>The packed window, built by Installer.UI.csproj (a project reference of the installer builder).</summary>
    public const string Package = @"install\Installer.UI\bin\Release\ToadTools.Installer.UI.CA.dll";

    /// <summary>Adds the embedded UI and the properties the window reads.</summary>
    public static void Attach(Project project)
    {
        if (!System.IO.File.Exists(Package))
            throw new FileNotFoundException($"The installer window is not built: {Package}. Build install/Installer.UI first.");

        var package = Path.GetFullPath(Package);
        project.WixSourceGenerated += document =>
        {
            var ns = document.Root!.Name.Namespace;
            var product = document.Root.Element(ns + "Package")!;
            product.Add(new XElement(ns + "UI",
                new XElement(ns + "EmbeddedUI", new XAttribute("Id", "ToadToolsWindow"), new XAttribute("SourceFile", package))));
        };
    }

    /// <summary>The properties of one package: version, changelog highlights, where add-ins go, the other package.</summary>
    public static Property[] Properties(string version, string addinsFolder, string otherPackage) =>
    [
        new("TOAD_VERSION", version),
        new("TOAD_HIGHLIGHTS", Highlights(version)),
        new("TOAD_ADDINS", addinsFolder),
        new("TOAD_OTHER_MSI", otherPackage),
        // A verbose log next to %TEMP%\MSI*.LOG: "Журнал установки" on the error page opens it
        new("MsiLogging", "voicewarmup")
    ];

    /// <summary>Up to three bullet points of the version's section in Changelog.md, separated by "|", without links.</summary>
    public static string Highlights(string version)
    {
        if (!System.IO.File.Exists("Changelog.md")) return "";
        var lines = System.IO.File.ReadAllLines("Changelog.md");
        var start = Array.FindIndex(lines, line => Regex.IsMatch(line, $@"^# {Regex.Escape(version)}(\s|$)"));
        if (start < 0) return "";

        var points = lines.Skip(start + 1)
            .TakeWhile(line => !line.StartsWith("# "))
            .Where(line => line.StartsWith("- "))
            .Select(line => MarkdownLink().Replace(line[2..], "$1").Replace("`", "").Replace("|", "/").Trim())
            .Take(3);
        return string.Join("|", points);
    }

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")]
    private static partial Regex MarkdownLink();
}
