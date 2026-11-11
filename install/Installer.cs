using Installer;
using WixSharp;
using WixSharp.CommonTasks;
using WixSharp.Controls;

const string outputName = "ToadTools";
const string projectName = "ToadTools";

var versioning = Versioning.CreateFromVersionStringAsync(args[0]);
var project = new Project
{
    OutDir = "output",
    Name = projectName,
    Platform = Platform.x64,
    UI = WUI.WixUI_FeatureTree,
    MajorUpgrade = MajorUpgrade.Default,
    GUID = new Guid("460B3098-3D8B-44D4-B7CE-729124B81606"),
    BannerImage = @"install\Resources\Icons\BannerImage.png",
    BackgroundImage = @"install\Resources\Icons\BackgroundImage.png",
    Version = versioning.VersionPrefix,
    ControlPanelInfo =
    {
        Manufacturer = Environment.UserName,
        ProductIcon = @"install\Resources\Icons\ShellIcon.ico"
    }
};

var wixEntities = Generator.GenerateWixEntities(args[1..]);
project.RemoveDialogsBetween(NativeDialogs.WelcomeDlg, NativeDialogs.CustomizeDlg);
Window.Attach(project);

BuildSingleUserMsi();
BuildMultiUserUserMsi();

void BuildSingleUserMsi()
{
    project.Scope = InstallScope.perUser;
    project.OutFileName = $"{outputName}-{versioning.Version}-SingleUser";
    project.Properties = Window.Properties(versioning.Version, @"%APPDATA%\Autodesk\Revit\Addins",
        $"{outputName}-{versioning.Version}-MultiUser.msi");
    project.Dirs =
    [
        new InstallDir(@"%AppDataFolder%\Autodesk\Revit\Addins\", wixEntities)
    ];
    project.BuildMsi();
}

void BuildMultiUserUserMsi()
{
    project.Scope = InstallScope.perMachine;
    project.OutFileName = $"{outputName}-{versioning.Version}-MultiUser";
    project.Properties = Window.Properties(versioning.Version,
        versioning.VersionPrefix.Major >= 2027 ? @"%ProgramFiles%\Autodesk\Revit\Addins" : @"%ProgramData%\Autodesk\Revit\Addins",
        $"{outputName}-{versioning.Version}-SingleUser.msi");
    project.Dirs =
    [
        new InstallDir(
            versioning.VersionPrefix.Major >= 2027
                ? @"%ProgramFiles%\Autodesk\Revit\Addins"
                : @"%CommonAppDataFolder%\Autodesk\Revit\Addins", wixEntities)
    ];
    project.BuildMsi();
}