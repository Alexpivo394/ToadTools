namespace ToadExporter.Core;

/// <summary>Откуда берутся модели: Revit Server или папка на диске.</summary>
public interface ITreeSource
{
    /// <summary>Путь корня в формате источника.</summary>
    string RootPath { get; }

    /// <summary>Содержимое папки: сначала папки, затем модели. Ошибки — <see cref="SourceException" />.</summary>
    Task<IReadOnlyList<SourceEntry>> GetChildrenAsync(string folderPath, CancellationToken ct);
}
