namespace ToadTools.Desktop.Tests;

/// <summary>Временная папка теста: удаляется при Dispose.</summary>
public sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "toad-desktop-" + Guid.NewGuid().ToString("N"));

    public TempFolder() => Directory.CreateDirectory(Path);

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, ..parts]);

    public void Dispose()
    {
        try { Directory.Delete(Path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
