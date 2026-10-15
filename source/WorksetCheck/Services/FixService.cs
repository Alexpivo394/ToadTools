using ToadTools.UI.Progress;
using WorksetCheck.Models;

namespace WorksetCheck.Services;

/// <summary>
///     «Исправить»: переносит элементы, для которых РН известен (создавая недостающие РН),
///     а остальные собирает на 3D-виде «Проверка РН». Всё — одной транзакцией: «Прервать» откатывает всё.
/// </summary>
public sealed class FixService
{
    public const string ViewName = "Проверка РН";

    private readonly Document _document;
    private readonly ElementId _activeViewId;

    public FixService(Document document, ElementId activeViewId)
    {
        _document = document;
        _activeViewId = activeViewId;
    }

    public FixResult Run(IReadOnlyList<WorksetIssue> issues, ToadProgress progress)
    {
        var result = new FixResult();
        var toMove = issues.Where(issue => issue.IsAutoFix).ToList();
        var toShow = issues.Where(issue => issue.Action == IssueAction.ShowOn3D).Select(issue => issue.ElementId).ToList();

        using var transaction = new Transaction(_document, "Исправление рабочих наборов");
        var options = transaction.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(new WarningSwallower());
        transaction.SetFailureHandlingOptions(options);
        transaction.Start();

        Move(toMove, result, progress);

        if (toShow.Count > 0)
        {
            progress.Stage("Сбор элементов на 3D-виде", $"{toShow.Count} эл.");
            var ids = toShow.Where(id => _document.GetElement(id) is not null).ToList();
            result.ViewId = BuildView(ids);
            result.ShownOn3D = ids.Count;
        }

        progress.ThrowIfCancelled();
        progress.Stage("Сохранение изменений");
        if (transaction.Commit() != TransactionStatus.Committed)
            throw new InvalidOperationException("Revit не принял изменения — транзакция откачена.");

        return result;
    }

    private void Move(List<WorksetIssue> issues, FixResult result, ToadProgress progress)
    {
        var worksets = new FilteredWorksetCollector(_document)
            .OfKind(WorksetKind.UserWorkset)
            .ToWorksets()
            .ToDictionary(workset => workset.Name, workset => workset.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var issue in progress.Track(issues, "Перенос в рабочие наборы"))
        {
            try
            {
                var target = issue.TargetWorkset!;
                if (!worksets.TryGetValue(target, out var worksetId))
                {
                    worksetId = Workset.Create(_document, target).Id;
                    worksets[target] = worksetId;
                    result.CreatedWorksets.Add(target);
                }

                var parameter = _document.GetElement(issue.ElementId)?.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                if (parameter is null || parameter.IsReadOnly)
                {
                    result.Failures.Add($"{issue.Element} (Id {issue.Id}): рабочий набор элемента нельзя изменить");
                    continue;
                }

                parameter.Set(worksetId.IntegerValue);
                result.Moved++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                result.Failures.Add($"{issue.Element} (Id {issue.Id}): {exception.Message}");
            }
        }
    }

    /// <summary>
    ///     3D-вид «Проверка РН» с изолированными элементами: без шаблона, все РН видимы.
    ///     Прежний вид удаляется, а если он сейчас открыт — переиспользуется.
    /// </summary>
    private ElementId BuildView(List<ElementId> ids)
    {
        var view = new FilteredElementCollector(_document)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .FirstOrDefault(candidate => !candidate.IsTemplate && candidate.Name == ViewName);

        if (view is not null && view.Id != _activeViewId)
        {
            _document.Delete(view.Id);
            view = null;
        }

        if (view is null)
        {
            var viewType = new FilteredElementCollector(_document)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .First(type => type.ViewFamily == ViewFamily.ThreeDimensional);
            view = View3D.CreateIsometric(_document, viewType.Id);
            view.Name = ViewName;
        }
        else
        {
            ResetHidden(view);
        }

        view.ViewTemplateId = ElementId.InvalidElementId;
        view.IsSectionBoxActive = false;
        view.Discipline = ViewDiscipline.Coordination;

        foreach (var workset in new FilteredWorksetCollector(_document).OfKind(WorksetKind.UserWorkset))
            view.SetWorksetVisibility(workset.Id, WorksetVisibility.Visible);

        if (ids.Count > 0)
        {
            view.IsolateElementsTemporary(ids);
            view.ConvertTemporaryHideIsolateToPermanent();
        }

        return view.Id;
    }

    /// <summary>Возвращает видимость всему, что скрыла прошлая изоляция на этом виде.</summary>
    private void ResetHidden(View view)
    {
        view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);

        var hidden = new FilteredElementCollector(_document)
            .WhereElementIsNotElementType()
            .Where(element => element.CanBeHidden(view) && element.IsHidden(view))
            .Select(element => element.Id)
            .ToList();
        if (hidden.Count > 0) view.UnhideElements(hidden);
    }

    /// <summary>Предупреждения при переносе (например, о дубликатах) не должны останавливать исправление.</summary>
    private sealed class WarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (var failure in accessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning) accessor.DeleteWarning(failure);
            }

            return FailureProcessingResult.Continue;
        }
    }
}
