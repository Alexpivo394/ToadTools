using System.IO;
using Autodesk.Revit.DB;
using Collisions.Core;

namespace Collisions.Revit;

/// <summary>По какому пути и имени искать открытый документ в ToadFlow.</summary>
public static class DocumentModel
{
    /// <returns>
    ///     Путь центральной модели (<c>RSN://…</c> или UNC), у не совместной — путь файла; null — документ не сохранён.
    ///     Имя файла — центральной модели: у локальной копии без хвоста <c>_{пользователь}</c>.
    /// </returns>
    public static (string? CentralPath, string FileName) Read(Document document)
    {
        string? path = null;
        if (document.IsWorkshared)
        {
            var central = document.GetWorksharingCentralModelPath();
            if (central is not null) path = ModelPathUtils.ConvertModelPathToUserVisiblePath(central);
        }

        if (string.IsNullOrWhiteSpace(path)) path = string.IsNullOrWhiteSpace(document.PathName) ? null : document.PathName;
        var fileName = path is null ? document.Title + ".rvt" : Path.GetFileName(path.Replace('/', '\\'));
        return (path, ModelNames.CentralName(fileName, Environment.UserName));
    }
}
