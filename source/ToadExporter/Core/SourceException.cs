namespace ToadExporter.Core;

/// <summary>Не удалось прочитать источник — текст показывается пользователю как есть.</summary>
public sealed class SourceException(string message, Exception? inner = null) : Exception(message, inner);
