namespace ToadExporter.Core;

/// <summary>
///     Адреса Revit Server. Путь папки — от корня через «/», без ведущего слэша; "" — корень.
/// </summary>
public static class RevitServerPaths
{
    /// <summary>Адрес REST-запроса содержимого папки: сегменты пути разделяются «|».</summary>
    public static string ContentsUrl(string server, string year, string folderPath)
    {
        var segments = Split(folderPath).Select(Uri.EscapeDataString).ToArray();
        var path = segments.Length == 0 ? "%7C" : string.Join("%7C", segments);
        return $"http://{server}/RevitServerAdminRESTService{year}/AdminRESTService.svc/{path}/contents";
    }

    /// <summary>Путь модели для Revit: RSN://сервер/папка/модель.rvt.</summary>
    public static string ModelPath(string server, string folderPath, string modelName) =>
        folderPath.Length == 0 ? $"RSN://{server}/{modelName}" : $"RSN://{server}/{folderPath}/{modelName}";

    public static string Combine(string folderPath, string name) =>
        folderPath.Length == 0 ? name : $"{folderPath}/{name}";

    private static string[] Split(string folderPath) =>
        folderPath.Split(['/'], StringSplitOptions.RemoveEmptyEntries);
}
