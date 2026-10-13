namespace SmartTags.Models;

/// <summary>Состояние окна, которое запоминается между запусками.</summary>
public sealed class SmartTagsFormState
{
    public List<CategoryPick> Categories { get; set; } = [];

    public ExistingTagMode ExistingMode { get; set; } = ExistingTagMode.Skip;
    public double MinRunLength { get; set; } = 300;
    public bool TagVerticalRuns { get; set; }
    public bool SkipSameDuctSection { get; set; } = true;

    public double PreferredLeader { get; set; } = 12;
    public double MaxLeader { get; set; } = 35;

    /// <summary>На 3D-видах выноски обычно длиннее — свой максимум.</summary>
    public double MaxLeader3D { get; set; } = 70;

    public int Quality { get; set; } = 1;

    public bool AdvancedExpanded { get; set; }
    public double Gap { get; set; } = 1.5;
    public double MinLeader { get; set; } = 5;
    public bool Angle30 { get; set; } = true;
    public bool Angle45 { get; set; } = true;
    public bool Angle60 { get; set; } = true;
    public bool AvoidTextOverElements { get; set; } = true;
    public bool UseCrop { get; set; } = true;
    public bool WriteLog { get; set; }
}

/// <summary>Отмеченная категория и выбранный для неё тип марки.</summary>
public sealed class CategoryPick
{
    public string Name { get; set; } = string.Empty;
    public bool Selected { get; set; }
    public string? TagTypeName { get; set; }
}
