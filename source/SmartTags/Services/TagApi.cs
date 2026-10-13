namespace SmartTags.Services;

/// <summary>
///     Работа с выноской марки. В Revit 2022 API выносок перешёл на ссылки (Reference) —
///     до 2022 используются старые свойства LeaderEnd/LeaderElbow/TaggedLocalElementId.
/// </summary>
internal static class TagApi
{
    public static IndependentTag Create(Document document, View view, ElementId tagTypeId, Element element, XYZ point)
    {
        return IndependentTag.Create(document, tagTypeId, view.Id, new Reference(element), true,
            TagOrientation.Horizontal, point);
    }

    public static IEnumerable<ElementId> TaggedElementIds(IndependentTag tag)
    {
#if REVIT2022_OR_GREATER
        return tag.GetTaggedLocalElementIds().Where(id => id != ElementId.InvalidElementId);
#else
        var id = tag.TaggedLocalElementId;
        return id == ElementId.InvalidElementId ? [] : [id];
#endif
    }

    public static XYZ? GetHead(IndependentTag tag)
    {
        try
        {
            return tag.TagHeadPosition;
        }
        catch
        {
            return null;
        }
    }

    public static bool SetHead(IndependentTag tag, XYZ point)
    {
        try
        {
            tag.TagHeadPosition = point;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void SetHasLeader(IndependentTag tag, bool value)
    {
        if (tag.HasLeader != value) tag.HasLeader = value;
    }

    /// <summary>Переводит конец выноски в «свободный» — только тогда его можно поставить в нужную точку.</summary>
    public static bool MakeFreeEnd(IndependentTag tag)
    {
        try
        {
            if (tag.LeaderEndCondition == LeaderEndCondition.Free) return true;
            if (!tag.CanLeaderEndConditionBeAssigned(LeaderEndCondition.Free)) return false;

            tag.LeaderEndCondition = LeaderEndCondition.Free;
            return tag.LeaderEndCondition == LeaderEndCondition.Free;
        }
        catch
        {
            return false;
        }
    }

    public static XYZ? GetLeaderEnd(IndependentTag tag)
    {
        if (!tag.HasLeader) return null;

        try
        {
#if REVIT2022_OR_GREATER
            return MainReference(tag) is { } reference ? tag.GetLeaderEnd(reference) : null;
#else
            return tag.LeaderEnd;
#endif
        }
        catch
        {
            return null;
        }
    }

    public static bool SetLeaderEnd(IndependentTag tag, XYZ point)
    {
        if (!tag.HasLeader || tag.LeaderEndCondition != LeaderEndCondition.Free) return false;

        try
        {
#if REVIT2022_OR_GREATER
            if (MainReference(tag) is not { } reference) return false;
            tag.SetLeaderEnd(reference, point);
#else
            tag.LeaderEnd = point;
#endif
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static XYZ? GetLeaderElbow(IndependentTag tag)
    {
        if (!tag.HasLeader) return null;

        try
        {
#if REVIT2022_OR_GREATER
            return MainReference(tag) is { } reference && tag.HasLeaderElbow(reference)
                ? tag.GetLeaderElbow(reference)
                : null;
#else
            return tag.HasElbow ? tag.LeaderElbow : null;
#endif
        }
        catch
        {
            return null;
        }
    }

    public static bool SetLeaderElbow(IndependentTag tag, XYZ point)
    {
        if (!tag.HasLeader) return false;

        try
        {
#if REVIT2022_OR_GREATER
            if (MainReference(tag) is not { } reference) return false;
            tag.SetLeaderElbow(reference, point);
#else
            tag.LeaderElbow = point;
#endif
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     «Сжимает» марку: голова, излом и конец выноски в одной точке. Тогда рамка марки равна
    ///     рамке текста, и её можно измерить.
    /// </summary>
    public static void Collapse(IndependentTag tag, XYZ point)
    {
        SetHead(tag, point);
        if (!tag.HasLeader) return;

        if (MakeFreeEnd(tag))
        {
            SetLeaderEnd(tag, point);
            SetLeaderElbow(tag, point);
        }
        else
        {
            SetHasLeader(tag, false);
        }
    }

#if REVIT2022_OR_GREATER
    private static Reference? MainReference(IndependentTag tag)
    {
        var references = tag.GetTaggedReferences();
        return references.Count > 0 ? references[0] : null;
    }
#endif
}
