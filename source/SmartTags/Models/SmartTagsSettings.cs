using SmartTags.Layout;

namespace SmartTags.Models;

/// <summary>Параметры одного запуска, собранные из окна. Размеры выносок — мм на листе.</summary>
public sealed class SmartTagsSettings
{
    public List<CategoryChoice> Choices { get; } = [];

    /// <summary>Маркировать только эти элементы (выбранные на виде). Null — все видимые.</summary>
    public HashSet<ElementId>? OnlyElements { get; set; }

    public ExistingTagMode ExistingMode { get; set; } = ExistingTagMode.Skip;

    /// <summary>Участки труб/воздуховодов короче этого (мм в модели) не маркируются.</summary>
    public double MinRunLength { get; set; } = 300;

    public bool TagVerticalRuns { get; set; }
    public bool SkipSameDuctSection { get; set; } = true;

    public double PreferredLeader { get; set; } = 12;
    public double MaxLeader { get; set; } = 35;
    public double MinLeader { get; set; } = 5;
    public double Gap { get; set; } = 1.5;
    public LayoutQuality Quality { get; set; } = LayoutQuality.Standard;

    public bool Angle30 { get; set; } = true;
    public bool Angle45 { get; set; } = true;
    public bool Angle60 { get; set; } = true;

    public bool AvoidTextOverElements { get; set; } = true;
    public bool UseCrop { get; set; } = true;
    public bool WriteLog { get; set; }

    public string CategoriesText => string.Join(", ", Choices.Select(choice => choice.Name));

    public LayoutSettings ToLayoutSettings()
    {
        var angles = new List<double>();
        if (Angle30) angles.Add(30);
        if (Angle45) angles.Add(45);
        if (Angle60) angles.Add(60);
        if (angles.Count == 0) angles.Add(45);

        var minGap = Math.Max(0.2, Gap);
        var minLeader = Math.Max(0.5, MinLeader);
        var preferred = Math.Max(minLeader, PreferredLeader);
        return new LayoutSettings
        {
            MinGap = minGap,
            SoftGap = Math.Max(minGap, Gap * 2),
            MinLeader = minLeader,
            PreferredLeader = preferred,
            MaxLeader = Math.Max(preferred, MaxLeader),
            PreferredAngles = angles,
            AvoidTextOverElements = AvoidTextOverElements,
            Quality = Quality
        };
    }
}
