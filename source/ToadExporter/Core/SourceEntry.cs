namespace ToadExporter.Core;

/// <summary>
///     Папка или модель в источнике. У папки <see cref="FolderPath" /> — её собственный путь, а
///     <see cref="ModelPath" /> пуст; у модели <see cref="FolderPath" /> — путь родительской папки,
///     <see cref="ModelPath" /> — путь, по которому Revit её откроет.
/// </summary>
public sealed record SourceEntry(string Name, string FolderPath, string ModelPath, bool IsFolder);
