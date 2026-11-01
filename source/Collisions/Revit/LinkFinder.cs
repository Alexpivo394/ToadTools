using System.IO;
using Autodesk.Revit.DB;

namespace Collisions.Revit;

/// <summary>Подгруженная связь, в которой лежит модель второй стороны коллизии.</summary>
public static class LinkFinder
{
    /// <summary>
    ///     Сначала по пути центральной модели связанного документа, потом по имени файла центральной модели (у связи —
    ///     тоже без хвоста локальной копии). Выгруженные связи не подходят: элемента в них не выбрать.
    /// </summary>
    public static RevitLinkInstance? Find(Document document, string modelName, string modelPath)
    {
        var links = new FilteredElementCollector(document).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>()
            .Select(link => (Link: link, Document: link.GetLinkDocument()))
            .Where(x => x.Document is not null)
            .Select(x => (x.Link, Model: DocumentModel.Read(x.Document)))
            .ToList();

        if (!string.IsNullOrWhiteSpace(modelPath)
            && links.FirstOrDefault(x => string.Equals(x.Model.CentralPath, modelPath, StringComparison.OrdinalIgnoreCase)).Link is { } byPath)
            return byPath;

        var name = Path.GetFileName(modelName);
        return links.FirstOrDefault(x => string.Equals(x.Model.FileName, name, StringComparison.OrdinalIgnoreCase)).Link;
    }
}
