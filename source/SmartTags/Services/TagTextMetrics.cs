using System.Globalization;

namespace SmartTags.Services;

/// <summary>
///     Где по высоте в марке стоит видимый текст. Рамка марки, которую отдаёт Revit, — это линии семейства
///     и текст видимых надписей. Линии бывают служебными: в семействах ADSK у марок воздуховодов невидимые
///     линии на ±8,5 мм держат рамку симметричной, чтобы выноска приходила к подчёркиванию. Поэтому из
///     семейства берутся надписи, видимые у типа марки (по параметрам видимости), и размах его линий:
///     где рамка выходит за линии — там текст (в том числе перенесённые строки), иначе текст — по надписям.
///     Открывать семейство можно только вне транзакции — поэтому всё читается до начала запуска.
/// </summary>
internal sealed class TagTextMetrics(Document document, Action<string>? log)
{
    private const double MmPerFoot = 304.8;

    // Строка текста выше высоты шрифта: выносные элементы букв и подчёркивание.
    private const double LineFactor = 1.4;

    private readonly Dictionary<ElementId, FamilyText?> _families = new();

    /// <summary>
    ///     Строки видимых надписей по вертикали (по одной строке на надпись) и размах линий семейства,
    ///     мм от головы марки (вверх — плюс). Null — семейство прочитать не удалось.
    /// </summary>
    public FamilyText? Of(ElementId tagTypeId) => _families.TryGetValue(tagTypeId, out var text) ? text : null;

    public void Load(IEnumerable<ElementId> tagTypeIds)
    {
        foreach (var id in tagTypeIds.Distinct())
        {
            if (_families.ContainsKey(id)) continue;

            try
            {
                _families[id] = Read(id);
            }
            catch (Exception ex)
            {
                _families[id] = null;
                log?.Invoke($"Семейство марки {RevitCompat.IdValue(id)}: не прочитано — {ex.Message}");
            }
        }
    }

    private FamilyText? Read(ElementId tagTypeId)
    {
        if (document.GetElement(tagTypeId) is not FamilySymbol { Family: { IsEditable: true } family } symbol) return null;

        var familyDocument = document.EditFamily(family);
        try
        {
            var manager = familyDocument.FamilyManager;
            double bottom = double.MaxValue, top = double.MinValue;
            var texts = new FilteredElementCollector(familyDocument)
                .WhereElementIsNotElementType()
                .OfType<TextElement>()
                .ToList();

            foreach (var text in texts)
            {
                var size = familyDocument.GetElement(text.GetTypeId())?.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() ?? 0;
                if (size <= 0 || !IsVisible(text, manager, symbol)) continue;

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
                    "Семейство «{0}», тип «{1}»: надпись, шрифт {2:0.##} мм, точка ({3:0.##}; {4:0.##}) мм, выравнивание {5}",
                    family.Name, symbol.Name, size * MmPerFoot, text.Coord.X * MmPerFoot, y, alignment));

                bottom = Math.Min(bottom, lineBottom);
                top = Math.Max(top, lineTop);
            }

            if (top <= bottom) return null;

            // Линии семейства (подчёркивание, служебные линии) — по ним Revit тоже строит рамку.
            double linesBottom = double.MaxValue, linesTop = double.MinValue;
            foreach (var curve in new FilteredElementCollector(familyDocument).OfClass(typeof(CurveElement)).Cast<CurveElement>())
            {
                if (curve.GeometryCurve is not { } geometry) continue;

                foreach (var point in geometry.Tessellate())
                {
                    linesBottom = Math.Min(linesBottom, point.Y * MmPerFoot);
                    linesTop = Math.Max(linesTop, point.Y * MmPerFoot);
                }
            }

            return new FamilyText(bottom, top, linesBottom, linesTop);
        }
        finally
        {
            familyDocument.Close(false);
        }
    }

    /// <summary>
    ///     Видна ли надпись у типа марки: видимость надписи бывает связана с параметром семейства
    ///     (у ADSK — по параметру на каждый вариант подписи); иначе — собственная видимость.
    /// </summary>
    private static bool IsVisible(Element text, FamilyManager manager, FamilySymbol symbol)
    {
        var visible = text.get_Parameter(BuiltInParameter.IS_VISIBLE_PARAM);
        if (visible is null) return true;

        var association = manager.GetAssociatedFamilyParameter(visible);
        if (association is null) return visible.AsInteger() != 0;

        // Параметр экземпляра у марки по типу не узнать — считаем, что надпись может быть видна.
        var value = symbol.LookupParameter(association.Definition.Name);
        return value is null || value.AsInteger() != 0;
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

/// <summary>
///     Видимый текст марки по одной строке на надпись (<see cref="Bottom" />…<see cref="Top" />) и размах
///     линий семейства (<see cref="LinesBottom" />…<see cref="LinesTop" />; без линий — пустой), мм от головы.
/// </summary>
internal sealed record FamilyText(double Bottom, double Top, double LinesBottom, double LinesTop);
