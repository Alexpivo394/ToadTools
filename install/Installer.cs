using System.Xml.Linq;
using Installer;
using WixSharp;

// Установщик программы ToadTools (трей): ставит её на пользователя в %LOCALAPPDATA%\ToadTools\app. Плагин в пакет не
// входит — программа скачивает его архивом с GitHub и раскладывает по версиям Revit сама.
// Аргументы: версия программы, папка с опубликованной программой, версия плагина (для «Что нового»).
const string projectName = "ToadTools";

if (args.Length < 3) throw new ArgumentException("Usage: Installer <app version> <published app folder> <plugin version>");
var appVersion = Versioning.CreateFromVersionStringAsync(args[0]);
var publishFolder = Path.GetFullPath(args[1]);
var pluginVersion = args[2];
if (!System.IO.File.Exists(Path.Combine(publishFolder, "ToadTools.exe")))
    throw new FileNotFoundException($"No ToadTools.exe in {publishFolder}");

// Старый MSI плагина (4.0.x и раньше) — тот же продукт для пользователя, удаляется при установке программы
const string legacyPluginUpgradeCode = "460B3098-3D8B-44D4-B7CE-729124B81606";
const string exeFileId = "ToadToolsExe";

var exe = new WixSharp.File(new Id(exeFileId), Path.Combine(publishFolder, "ToadTools.exe"),
    new FileShortcut("ToadTools", @"%ProgramMenu%") { WorkingDirectory = "INSTALLDIR" });
var project = new Project
{
    OutDir = "output",
    Name = projectName,
    Platform = Platform.x64,
    Scope = InstallScope.perUser,
    MajorUpgrade = MajorUpgrade.Default,
    GUID = new Guid("7A3E5C1D-9B42-4F6E-8D17-2C5B9E0A4F31"),
    Version = appVersion.VersionPrefix,
    OutFileName = $"ToadTools-App-{appVersion.VersionPrefix.ToString(3)}",
    ControlPanelInfo =
    {
        Manufacturer = "ToadTools",
        ProductIcon = @"install\Resources\Icons\ShellIcon.ico"
    },
    Dirs =
    [
        new InstallDir(@"%LocalAppDataFolder%\ToadTools\app",
            exe,
            new Files(Path.Combine(publishFolder, "*.*"), path => !path.EndsWith(@"\ToadTools.exe", StringComparison.OrdinalIgnoreCase)))
    ]
};

Window.Attach(project);
Window.SetProperties(project, appVersion.VersionPrefix.ToString(3), pluginVersion);

project.WixSourceGenerated += document =>
{
    var ns = document.Root!.Name.Namespace;
    var package = document.Root.Element(ns + "Package")!;

    // Удалить старый MSI плагина любой версии: его файлы в Addins теперь раскладывает программа
    package.Add(new XElement(ns + "Upgrade", new XAttribute("Id", legacyPluginUpgradeCode),
        new XElement(ns + "UpgradeVersion",
            new XAttribute("Minimum", "0.0.0"), new XAttribute("IncludeMinimum", "yes"),
            new XAttribute("Maximum", "99.0.0"), new XAttribute("IncludeMaximum", "no"),
            new XAttribute("OnlyDetect", "no"), new XAttribute("Property", "TOADLEGACYPLUGIN"))));

    // Закрыть запущенную программу до замены файлов; запустить после установки (и после тихого самообновления);
    // при удалении (но не при обновлении) убрать плагин из папок Revit
    package.Add(
        new XElement(ns + "CustomAction", new XAttribute("Id", "ToadCloseApp"), new XAttribute("FileRef", exeFileId),
            new XAttribute("ExeCommand", "--exit"), new XAttribute("Execute", "immediate"), new XAttribute("Return", "ignore")),
        new XElement(ns + "CustomAction", new XAttribute("Id", "ToadUninstall"), new XAttribute("FileRef", exeFileId),
            new XAttribute("ExeCommand", "--uninstall"), new XAttribute("Execute", "deferred"), new XAttribute("Impersonate", "yes"),
            new XAttribute("Return", "ignore")),
        new XElement(ns + "CustomAction", new XAttribute("Id", "ToadStartApp"), new XAttribute("FileRef", exeFileId),
            new XAttribute("ExeCommand", "--background"), new XAttribute("Execute", "immediate"), new XAttribute("Return", "asyncNoWait")),
        new XElement(ns + "InstallExecuteSequence",
            new XElement(ns + "Custom", new XAttribute("Action", "ToadCloseApp"), new XAttribute("Before", "InstallValidate"),
                new XAttribute("Condition", "Installed OR WIX_UPGRADE_DETECTED")),
            new XElement(ns + "Custom", new XAttribute("Action", "ToadUninstall"), new XAttribute("Before", "RemoveFiles"),
                new XAttribute("Condition", "REMOVE=\"ALL\" AND NOT UPGRADINGPRODUCTCODE")),
            new XElement(ns + "Custom", new XAttribute("Action", "ToadStartApp"), new XAttribute("After", "InstallFinalize"),
                new XAttribute("Condition", "NOT REMOVE"))));
};

if (project.BuildMsi() is not { } msi || !System.IO.File.Exists(msi))
    throw new InvalidOperationException($"WiX failed to build {project.OutFileName}.msi, see the output above");
