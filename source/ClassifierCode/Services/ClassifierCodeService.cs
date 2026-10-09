using ClassifierCode.Dictionaries;
using ClassifierCode.Models;

namespace ClassifierCode.Services;

/// <summary>
///     Заполняет "ADSK_Код по классификатору" и "ADSK_Описание по классификатору" у элементов ИОС
///     активного документа.
/// </summary>
public sealed class ClassifierCodeService(ElementCollector collector)
{
    /// <summary>Раздел, предлагаемый по шифру в имени файла (ПТ, КНД, ОВВ, ОВО, ВК...); None — не определён.</summary>
    public static Discipline DetectSection(string? documentTitle)
    {
        switch (TextRules.MatchDocumentTitle(documentTitle))
        {
            case Discipline.Fire:
            case Discipline.Gas:
            case Discipline.Powder:
                return Discipline.Fire;
            case Discipline.Cooling:
                return Discipline.Cooling;
            case Discipline.Vent:
            case Discipline.Smoke:
                return Discipline.Vent;
            case Discipline.Heating:
            case Discipline.HeatSupply:
                return Discipline.Heating;
            case Discipline.Water:
            case Discipline.Sewer:
            case Discipline.Storm:
                return Discipline.Water;
            case Discipline.Electrical:
                return Discipline.Electrical;
            case Discipline.LowCurrent:
                return Discipline.LowCurrent;
            default:
                return Discipline.None;
        }
    }

    /// <summary>
    ///     Запуск заполнения. null — пользователь отказался добавлять параметры в проект
    ///     (или добавить не удалось — причина уже показана). Report.Total == 0 — подходящих элементов нет.
    /// </summary>
    public ClassifierReport? Run(SectionOption section, RunScope scope, bool keepExisting)
    {
        var uiDocument = RevitContext.ActiveUiDocument!;
        var document = uiDocument.Document;
        var selection = uiDocument.Selection.GetElementIds();

        // Нет параметров классификатора в проекте — добавляем из ФОП (подключённого или выбранного пользователем).
        var loader = new ClassifierParameterLoader(document.Application, document);
        if (!loader.EnsureParameters(out var parametersNote))
            return null;

        var targets = collector.Collect(document, document.ActiveView, scope, selection);
        var report = new ClassifierReport(section, targets.Count);
        if (parametersNote != null)
            report.Notes.Add(parametersNote);
        if (targets.Count == 0)
            return report;

        var classifier = new ElementClassifier(document, section.Section);

        using var transaction = new Transaction(document, "Заполнение кода по классификатору");
        var options = transaction.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(new WarningSwallower());
        transaction.SetFailureHandlingOptions(options);
        transaction.Start();

        AllowVaryBetweenGroups(targets, report);

        var writer = new ClassifierParameterWriter(document, keepExisting, report);
        foreach (var element in targets)
        {
            var classification = classifier.Classify(element);
            if (classification.Skip)
            {
                report.AddSkipped(classification.Reason ?? string.Empty);
                continue;
            }

            if (classification.Item == null)
            {
                report.AddUnclassified(element, classification.Reason);
                continue;
            }

            writer.Write(element, classification.Item);
        }

        writer.FlushTypes();
        transaction.Commit();

        return report;
    }

    /// <summary>
    ///     Элементы в группах: если ADSK-параметры не помечены "значения могут меняться по экземплярам групп",
    ///     Revit не даёт их записать. Включаем этот признак для двух параметров классификатора.
    /// </summary>
    private static void AllowVaryBetweenGroups(List<Element> targets, ClassifierReport report)
    {
        foreach (var name in ClassifierParameters.All)
        {
            var grouped = targets.FirstOrDefault(e => e.GroupId != ElementId.InvalidElementId && e.LookupParameter(name) != null);
            if (grouped?.LookupParameter(name).Definition is not InternalDefinition definition || definition.VariesAcrossGroups)
                continue;

            try
            {
                definition.SetAllowVaryBetweenGroups(grouped.Document, true);
                report.Notes.Add("Для параметра \"" + name + "\" включено \"Значения могут меняться по экземплярам групп\".");
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                report.Notes.Add("Не удалось разрешить различие по группам для \"" + name + "\": " + ex.Message);
            }
        }
    }
}
