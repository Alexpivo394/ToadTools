using SmartTags.Layout;

namespace SmartTags.Models;

/// <summary>Параметры одного запуска, собранные из окна. Размеры выносок — мм на листе.</summary>
public sealed class SmartTagsSettings
{
    public List<CategoryChoice> Choices { get; } = [];

    /// <summary>Маркировать только эти элементы (выбранные на виде). Null — все видимые.</summary>
    public HashSet<ElementId>? OnlyElements { get; set; }

    public ExistingTagMode ExistingMode { get; set; } = ExistingTagMode.Skip;

    /// <summary>Участки (в режиме трасс — трассы) короче этого, мм в модели, не маркируются.</summary>
    public double MinRunLength { get; set; } = 300;

    public bool TagVerticalRuns { get; set; }

    /// <summary>Сколько марок ставить на линейные элементы: на каждый, на прямой участок или на трассу.</summary>
    public RunTagMode RunMode { get; set; } = RunTagMode.Routes;

    /// <summary>Одинаковые марки соседних штучных элементов — одной подписью с несколькими выносками.</summary>
    public bool ShareLabels { get; set; } = true;

    public double PreferredLeader { get; set; } = 12;
    public double MaxLeader { get; set; } = 35;
    public double MinLeader { get; set; } = 5;
    public double Gap { get; set; } = 1.5;
    public LayoutQuality Quality { get; set; } = LayoutQuality.Standard;

    public bool AvoidTextOverElements { get; set; } = true;
    public bool UseCrop { get; set; } = true;
    public bool WriteLog { get; set; }

    public string CategoriesText => string.Join(", ", Choices.Select(choice => choice.Name));

    public LayoutSettings ToLayoutSettings()
    {
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
            AvoidTextOverElements = AvoidTextOverElements,
            Quality = Quality
        };
    }
}
