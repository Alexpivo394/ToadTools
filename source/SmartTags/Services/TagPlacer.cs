using System.Globalization;
using SmartTags.Layout;
using SmartTags.Layout.Geometry;
using SmartTags.Models;

namespace SmartTags.Services;

/// <summary>Создание, измерение и установка марок по результатам раскладки.</summary>
internal sealed class TagPlacer(TagView view, Action<string>? log, TagTextMetrics? metrics = null)
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
        if (target.Tag is null || !TryMeasure(target.Tag, out var text, out var head, out var attachHeight)) return false;

        target.TextWidth = text.Width;
        target.TextHeight = text.Height;
        target.AttachHeight = attachHeight;
        target.TextOffset = text.Min - head;
        target.Text = SafeText(target.Tag);
        target.Measured = true;
        log?.Invoke(string.Format(CultureInfo.InvariantCulture, "Марка {0}: текст {1:0.##}×{2:0.##} мм, выноска на {3:0.##} мм, «{4}»",
            RevitCompat.IdValue(target.Tag.Id), text.Width, text.Height, attachHeight, SafeText(target.Tag)));
        return true;
    }

    /// <inheritdoc cref="TryMeasure(IndependentTag, out Box2D, out Vector2D, out double)" />
    public bool TryMeasure(IndependentTag tag, out Box2D text, out Vector2D head) => TryMeasure(tag, out text, out head, out _);

    /// <summary>
    ///     Текст марки в координатах листа, положение её головы и высота над низом текста, на которой
    ///     Revit подводит выноску: середина рамки марки (у марок с подчёркиванием — его уровень).
    /// </summary>
    public bool TryMeasure(IndependentTag tag, out Box2D text, out Vector2D head, out double attachHeight)
    {
        text = Box2D.Empty;
        head = Vector2D.Zero;
        attachHeight = 0;

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
        if (text.IsEmpty || text.Width is < 0.5 or > 300 || text.Height is < 0.5 or > 300) return false;

        var frame = text;
        text = TrimToLine(tag, text, head);
        log?.Invoke(string.Format(CultureInfo.InvariantCulture,
            "Марка {0}: рамка {1:0.##}…{2:0.##} мм от головы, текст {3:0.##}…{4:0.##} мм",
            RevitCompat.IdValue(tag.Id), frame.MinY - head.Y, frame.MaxY - head.Y, text.MinY - head.Y, text.MaxY - head.Y));
        attachHeight = frame.Center.Y - text.MinY;
        return true;
    }

    /// <summary>
    ///     Рамка подписи бывает намного выше самого текста — из-за служебных линий семейства. Где рамка
    ///     выходит за линии семейства, там текст (перенесённые строки) — граница остаётся; где не выходит —
    ///     граница по видимым надписям семейства.
    /// </summary>
    private Box2D TrimToLine(IndependentTag tag, Box2D text, Vector2D head)
    {
        if (metrics?.Of(tag.GetTypeId()) is not { } family) return text;

        const double tolerance = 0.5;
        var bottom = text.MinY - head.Y < family.LinesBottom - tolerance ? text.MinY - head.Y : family.Bottom;
        var top = text.MaxY - head.Y > family.LinesTop + tolerance ? text.MaxY - head.Y : family.Top;
        var lineHeight = top - bottom;
        if (lineHeight <= 0 || text.Height < lineHeight + 2 * tolerance) return text;

        var lineMin = head.Y + bottom;
        var lineMax = head.Y + top;
        if (lineMin < text.MinY - tolerance || lineMax > text.MaxY + tolerance)
        {
            log?.Invoke(string.Format(CultureInfo.InvariantCulture,
                "Марка {0}: строка текста {1:0.##}…{2:0.##} мм вне рамки {3:0.##}…{4:0.##} мм — берём рамку целиком",
                RevitCompat.IdValue(tag.Id), bottom, top, text.MinY - head.Y, text.MaxY - head.Y));
            return text;
        }

        lineMin = Math.Max(lineMin, text.MinY);
        lineMax = Math.Min(lineMax, text.MaxY);
        return new Box2D(text.MinX, lineMin, text.MaxX, lineMax);
    }

    /// <summary>Ставит голову марки и выноску по найденному положению.</summary>
    public bool Apply(TagTarget target, Placement placement)
    {
        if (target.Tag is not { } tag) return false;

        var head = _projection.ToModel(placement.Text.Min - target.TextOffset, target.Depth);
        var elbow = _projection.ToModel(placement.Elbow, target.Depth);
        // У общей подписи выноска каждой марки идёт от своего элемента к общему излому.
        var end = target.SharedWith is not null
            ? target.Anchors[0]
            : placement.AnchorIndex < target.Anchors.Count
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

    public static string SafeText(IndependentTag tag)
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
