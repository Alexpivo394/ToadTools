using System.Globalization;

namespace SmartTags.Services;

/// <summary>
///     Где по высоте в марке стоит текст. Рамка марки, которую отдаёт Revit, — это рамка подписи из
///     семейства (у семейств ADSK, например, 15×10 мм), а не сам текст. Высоту строки и её положение
///     относительно головы марки берём из подписей семейства. Открывать семейство можно только вне
///     транзакции — поэтому всё считается до начала запуска.
/// </summary>
internal sealed class TagTextMetrics(Document document, Action<string>? log)
{
    private const double MmPerFoot = 304.8;

    // Строка текста выше высоты шрифта: выносные элементы букв и подчёркивание.
    private const double LineFactor = 1.4;

    private readonly Dictionary<ElementId, (double Bottom, double Top)?> _lines = new();

    /// <summary>
    ///     Строки текста марки по вертикали, мм от головы марки (вверх — плюс). Null — семейство
    ///     прочитать не удалось, остаётся рамка от Revit.
    /// </summary>
    public (double Bottom, double Top)? LineOf(ElementId tagTypeId)
    {
        return _lines.TryGetValue(tagTypeId, out var line) ? line : null;
    }

    public void Load(IEnumerable<ElementId> tagTypeIds)
    {
        foreach (var id in tagTypeIds.Distinct())
        {
            if (_lines.ContainsKey(id)) continue;

            try
            {
                _lines[id] = Read(id);
            }
            catch (Exception ex)
            {
                _lines[id] = null;
                log?.Invoke($"Семейство марки {RevitCompat.IdValue(id)}: не прочитано — {ex.Message}");
            }
        }
    }

    private (double Bottom, double Top)? Read(ElementId tagTypeId)
    {
        if (document.GetElement(tagTypeId) is not FamilySymbol { Family: { IsEditable: true } family }) return null;

        var familyDocument = document.EditFamily(family);
        try
        {
            double bottom = double.MaxValue, top = double.MinValue;
            var texts = new FilteredElementCollector(familyDocument)
                .WhereElementIsNotElementType()
                .OfType<TextElement>()
                .ToList();

            foreach (var text in texts)
            {
                var size = familyDocument.GetElement(text.GetTypeId())?.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() ?? 0;
                if (size <= 0) continue;

                var height = size * MmPerFoot * LineFactor;
                var y = text.Coord.Y * MmPerFoot;
                var alignment = Alignment(text);
                var (lineBottom, lineTop) = alignment switch
                {
                    VerticalTextAlignment.Top => (y - height, y),
                    VerticalTextAlignment.Bottom => (y, y + height),
                    _ => (y - height / 2, y + height / 2)
                };

                log?.Invoke(string.Format(CultureInfo.InvariantCulture,
                    "Семейство «{0}»: подпись {1}, шрифт {2:0.##} мм, точка ({3:0.##}; {4:0.##}) мм, выравнивание {5}",
                    family.Name, text.GetType().Name, size * MmPerFoot, text.Coord.X * MmPerFoot, y, alignment));

                bottom = Math.Min(bottom, lineBottom);
                top = Math.Max(top, lineTop);
            }

            if (top <= bottom) return null;
            return (bottom, top);
        }
        finally
        {
            familyDocument.Close(false);
        }
    }

    private static VerticalTextAlignment Alignment(TextElement text)
    {
        try
        {
            return text.VerticalAlignment;
        }
        catch
        {
            return VerticalTextAlignment.Middle;
        }
    }
}
