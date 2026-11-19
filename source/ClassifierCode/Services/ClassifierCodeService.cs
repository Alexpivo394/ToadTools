using ToadDesign;
using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Profile;
using ClassifierCode.Engine.Running;
using ClassifierCode.Models;
using ToadTools.UI.Progress;

namespace ClassifierCode.Services;

/// <summary>Заполняет параметры профиля у элементов активного документа по выбранному режиму.</summary>
public sealed class ClassifierCodeService(ElementCollector collector)
{
    /// <summary>
    ///     Запуск заполнения. null — пользователь отказался добавлять параметры в проект
    ///     (или добавить не удалось — причина уже показана). Report.Total == 0 — подходящих элементов нет.
    /// </summary>
    public ClassifierReport? Run(CompiledProfile profile, string modeKey, RunScope scope, bool keepExisting)
    {
        var uiDocument = RevitContext.ActiveUiDocument!;
        var document = uiDocument.Document;
        var selection = uiDocument.Selection.GetElementIds();
        var source = profile.Source;
        var mode = profile.Modes[modeKey].Source;
        var codeOutput = source.Outputs.First(o => o.Field == "code");

        var unknownCategories = new List<string>();
        var categories = ElementCollector.Categories(source.Categories, unknownCategories);

        // Нет параметров в проекте — добавляем из ФОП (подключённого или выбранного пользователем).
        var loader = new ClassifierParameterLoader(document.Application, document, source.Outputs, categories);
        if (!loader.EnsureParameters(out var parametersNote))
            return null;

        using var progress = ToadProgress.Show("Код по классификатору",
            new ProgressOptions { Icon = Symbol.ListDetails });
        progress.Stage("Сбор элементов");

        var targets = collector.Collect(document, document.ActiveView, scope, selection, categories, codeOutput);
        var report = new ClassifierReport(source.Name, source.Version, mode.Title, mode.ReportMode, targets.Count);
        if (parametersNote != null)
            report.Notes.Add(parametersNote);
        if (unknownCategories.Count > 0)
            report.Notes.Add("Категории профиля, которых нет в этой версии Revit: " + string.Join(", ", unknownCategories) + ".");
        if (targets.Count == 0)
            return report;

        var engine = new ClassificationEngine(profile, modeKey);
        var facts = new FactsBuilder(document);

        using var transaction = new Transaction(document, "Заполнение кода по классификатору");
        var options = transaction.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(new WarningSwallower());
        transaction.SetFailureHandlingOptions(options);
        transaction.Start();

        AllowVaryBetweenGroups(targets, source.Outputs, report);

        var writer = new ClassifierParameterWriter(document, keepExisting, report, source.Outputs);
        foreach (var element in progress.Track(targets, "Классификация элементов"))
        {
            var result = engine.Classify(facts.Get(element));
            switch (result.Status)
            {
                case EngineStatus.Skipped:
                    report.AddSkipped(result.Reason ?? string.Empty);
                    break;
                case EngineStatus.Unclassified:
                    report.AddUnclassified(element, result.Reason);
                    break;
                default:
                    writer.Write(element, result.Position!);
                    break;
            }
        }

        foreach (var warning in engine.Warnings)
            report.Notes.Add(warning);

        progress.Stage("Запись типов и сохранение");
        writer.FlushTypes();
        if (ProfileBinding.TryWrite(document, source, modeKey) is { } bindingNote)
            report.Notes.Add(bindingNote);
        var status = transaction.Commit();
        if (status != TransactionStatus.Committed)
            throw new InvalidOperationException($"Revit не принял изменения (статус транзакции: {status}) — в модель ничего не записано.");

        return report;
    }

    /// <summary>
    ///     Элементы в группах: если параметры не помечены "значения могут меняться по экземплярам групп",
    ///     Revit не даёт их записать. Включаем этот признак для параметров записи профиля.
    /// </summary>
    private static void AllowVaryBetweenGroups(List<Element> targets, IReadOnlyList<OutputField> outputs, ClassifierReport report)
    {
        foreach (var output in outputs.Where(o => !string.IsNullOrWhiteSpace(o.Parameter)))
        {
            var grouped = targets.FirstOrDefault(e => e.GroupId != ElementId.InvalidElementId && ClassifierParameterWriter.Find(e, output) != null);
            if (grouped == null || ClassifierParameterWriter.Find(grouped, output)!.Definition is not InternalDefinition definition ||
                definition.VariesAcrossGroups)
                continue;

            try
            {
                definition.SetAllowVaryBetweenGroups(grouped.Document, true);
                report.Notes.Add("Для параметра \"" + output.Parameter + "\" включено \"Значения могут меняться по экземплярам групп\".");
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                report.Notes.Add("Не удалось разрешить различие по группам для \"" + output.Parameter + "\": " + ex.Message);
            }
        }
    }
}
