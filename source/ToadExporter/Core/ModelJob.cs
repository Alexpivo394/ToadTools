namespace ToadExporter.Core;

/// <summary>Модель, отмеченная к выгрузке: откуда её открыть и в какие форматы выгрузить.</summary>
public sealed record ModelJob(string Name, string SourcePath, bool IsServer, bool Rvt, bool Nwc);
