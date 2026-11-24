namespace ToadTools.Telemetry;

/// <summary>Модель в записях аналитики: имя файла, полный путь хранилища, вид хранилища и узел Revit Server.</summary>
/// <param name="Storage"><c>File</c>, <c>RevitServer</c> или <c>Cloud</c>.</param>
/// <param name="ServerHost">Узел Revit Server из пути <c>RSN://узел/…</c>; у других хранилищ null.</param>
public sealed record ModelRef(string Name, string Path, string Storage, string? ServerHost)
{
    private static readonly string[] CloudPrefixes = ["BIM 360://", "Autodesk Docs://", "cld://"];

    /// <summary>
    ///     По путям документа. <paramref name="centralPath" /> — путь хранилища, если модель совместная (человек открыл свою
    ///     локальную копию, а считать надо модель хранилища); пусто — берётся <paramref name="filePath" />.
    /// </summary>
    public static ModelRef From(string? centralPath, string filePath)
    {
        var path = string.IsNullOrWhiteSpace(centralPath) ? filePath : centralPath!;
        var name = path.Split('/', '\\').LastOrDefault(part => part.Length > 0) ?? path;
        if (path.StartsWith("RSN://", StringComparison.OrdinalIgnoreCase))
        {
            var rest = path.Substring("RSN://".Length);
            var slash = rest.IndexOf('/');
            return new ModelRef(name, path, "RevitServer", slash > 0 ? rest.Substring(0, slash) : null);
        }

        return CloudPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            ? new ModelRef(name, path, "Cloud", null)
            : new ModelRef(name, path, "File", null);
    }
}
