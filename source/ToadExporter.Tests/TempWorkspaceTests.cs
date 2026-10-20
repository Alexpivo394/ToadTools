using Shouldly;
using ToadExporter.Core;

namespace ToadExporter.Tests;

[NotInParallel(nameof(TempWorkspace))]
public class TempWorkspaceTests
{
    [Test]
    public void CleanupUnused_RemovesAbandonedDirsOfAnyAge()
    {
        using var root = new RootScope();
        var abandoned = Directory.CreateDirectory(Path.Combine(root.Path, "abandoned"));
        File.WriteAllText(Path.Combine(abandoned.FullName, "x.rvt"), "");

        TempWorkspace.CleanupUnused();

        Directory.Exists(abandoned.FullName).ShouldBeFalse();
    }

    [Test]
    public void CleanupUnused_KeepsWorkspaceInUse()
    {
        using var root = new RootScope();
        using var busy = TempWorkspace.Create();
        File.WriteAllText(Path.Combine(busy.Directory, "AR.rvt"), "");

        TempWorkspace.CleanupUnused();

        File.Exists(Path.Combine(busy.Directory, "AR.rvt")).ShouldBeTrue();
    }

    [Test]
    public void Dispose_RemovesOwnDirectory()
    {
        using var root = new RootScope();
        var workspace = TempWorkspace.Create();
        File.WriteAllText(Path.Combine(workspace.Directory, "AR.rvt"), "");

        workspace.Dispose();

        Directory.Exists(workspace.Directory).ShouldBeFalse();
        workspace.Directory.ShouldStartWith(root.Path);
    }

    [Test]
    public void CleanupUnused_MissingRoot_DoesNothing()
    {
        using var root = new RootScope();
        Directory.Delete(root.Path);

        Should.NotThrow(TempWorkspace.CleanupUnused);
    }

    private sealed class RootScope : IDisposable
    {
        public string Path { get; } = Directory.CreateDirectory(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "te-tmp-" + Guid.NewGuid().ToString("N"))).FullName;

        public RootScope() => TempWorkspace.RootOverride = Path;

        public void Dispose()
        {
            TempWorkspace.RootOverride = null;
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
