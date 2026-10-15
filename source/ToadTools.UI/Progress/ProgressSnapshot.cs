#nullable enable
using System;

namespace ToadTools.UI.Progress;

/// <summary>Состояние прогресса, которое поток Revit пишет, а окно читает (копия под замком).</summary>
internal sealed class ProgressSnapshot
{
    public string Stage = "Подготовка";
    public string Detail = string.Empty;
    public int Current;
    public int Total;

    public bool ItemVisible;
    public string? ItemTitle;
    public string? ItemStatus;
    public int ItemCurrent;
    public int ItemTotal;
    public bool ItemCanSkip;
    public DateTime ItemStartedAt;

    public ProgressSnapshot Clone() => (ProgressSnapshot)MemberwiseClone();
}
