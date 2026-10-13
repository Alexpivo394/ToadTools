using System.Globalization;
using SmartTags.Layout;
using SmartTags.Layout.Geometry;
using SmartTags.Models;

namespace SmartTags.Services;

/// <summary>Создание, измерение и установка марок по результатам раскладки.</summary>
internal sealed class TagPlacer(TagView view, Action<string>? log)
{
    private readonly ViewProjection _projection = view.Projection;

    /// <summary>Создаёт марку, «сжатую» в точку привязки, — чтобы затем измерить её текст.</summary>
    public void CreateCollapsed(TagTarget target)
    {
        var anchor = target.Anchors[0];
        var tag = TagApi.Create(view.Document, view.View, target.Choice.TagType.Id, target.Primary, anchor);
        TagApi.SetHasLeader(tag, true);
        TagApi.Collapse(tag, anchor);
        target.Tag = tag;
    }

    /// <summary>Размер текста новой марки и смещение его угла от головы марки.</summary>
    public bool Measure(TagTarget target)
    {
        if (target.Tag is null || !TryMeasure(target.Tag, out var text, out var head)) return false;

        target.TextWidth = text.Width;
        target.TextHeight = text.Height;
        target.TextOffset = text.Min - head;
        target.Measured = true;
        log?.Invoke(string.Format(CultureInfo.InvariantCulture, "Марка {0}: текст {1:0.##}×{2:0.##} мм, «{3}»",
            RevitCompat.IdValue(target.Tag.Id), text.Width, text.Height, SafeText(target.Tag)));
        return true;
    }

    /// <summary>Текст марки в координатах листа и положение её головы.</summary>
    public bool TryMeasure(IndependentTag tag, out Box2D text, out Vector2D head)
    {
        text = Box2D.Empty;
        head = Vector2D.Zero;

        var box = tag.get_BoundingBox(view.View);
        if (box is null || TagApi.GetHead(tag) is not { } headPoint) return false;

        head = _projection.ToSheet(headPoint);
        if (view.Kind == ViewKind.ThreeD)
        {
            // На 3D-виде рамка выровнена по осям модели — восстанавливаем размер в плоскости вида.
            if (!_projection.TryGetPlanarSize(box, out var width, out var height)) return false;

            var center = (box.Transform ?? Transform.Identity).OfPoint((box.Min + box.Max) * 0.5);
            text = Box2D.FromCenter(_projection.ToSheet(center), width, height);
        }
        else
        {
            text = _projection.Project(box);
        }

        // Отсекаем явный мусор: пустую марку или рамку, раздутую чем-то посторонним.
        return !text.IsEmpty && text.Width is >= 0.5 and <= 300 && text.Height is >= 0.5 and <= 300;
    }

    /// <summary>Ставит голову марки и выноску по найденному положению.</summary>
    public bool Apply(TagTarget target, Placement placement)
    {
        if (target.Tag is not { } tag) return false;

        var head = _projection.ToModel(placement.Text.Min - target.TextOffset, target.Depth);
        var elbow = _projection.ToModel(placement.Elbow, target.Depth);
        var end = placement.AnchorIndex < target.Anchors.Count
            ? target.Anchors[placement.AnchorIndex]
            : _projection.ToModel(placement.Anchor, target.Depth);

        TagApi.SetHasLeader(tag, true);
        if (!TagApi.SetHead(tag, head)) return false;

        if (TagApi.MakeFreeEnd(tag))
            TagApi.SetLeaderEnd(tag, end);

        if (!TagApi.SetLeaderElbow(tag, elbow))
            log?.Invoke($"Излом выноски не установлен: марка {RevitCompat.IdValue(tag.Id)}");

        return true;
    }

    private static string SafeText(IndependentTag tag)
    {
        try
        {
            return tag.TagText;
        }
        catch
        {
            return string.Empty;
        }
    }
}
