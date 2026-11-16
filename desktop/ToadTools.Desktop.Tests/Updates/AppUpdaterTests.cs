using Shouldly;
using ToadTools.Desktop.Core.Updates;

namespace ToadTools.Desktop.Tests.Updates;

public class AppUpdaterTests
{
    [Test]
    public void StartInfo_SilentInstall_WithLog()
    {
        var info = AppUpdater.StartInfo(@"C:\d\ToadTools-App-1.1.0.msi", @"C:\logs");

        info.FileName.ShouldEndWith("msiexec.exe");
        info.Arguments.ShouldStartWith(@"/i ""C:\d\ToadTools-App-1.1.0.msi"" /qn /norestart /l*v ""C:\logs\msi-");
    }
}
