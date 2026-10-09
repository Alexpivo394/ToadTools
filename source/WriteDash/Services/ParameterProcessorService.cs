using Nice3point.Revit.Toolkit;
using ToadTools.UI.Models;
using ToadTools.UI.Services;
using WriteDash.Models;

namespace WriteDash.Services;

public class ParameterProcessorService
{
    public void Process(List<ParameterDescriptor> selectedParameters)
    {
        if (selectedParameters == null || selectedParameters.Count == 0)
        {
            ToadDialogService.Show(
                "Ошибка!",
                "Выберите параметры для записи!",
                DialogButtons.OK,
                DialogIcon.Error
            );

            return;
        }

        var document = RevitContext.ActiveDocument!;

        var parameterNames = new HashSet<string>(
            selectedParameters
                .Select(x => x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!));

        var elements = new FilteredElementCollector(document)
            .WhereElementIsNotElementType()
            .Where(x =>
                x.Category != null &&
                x.Category.CategoryType == CategoryType.Model);

        var processedTypeIds = new HashSet<ElementId>();

        using var transaction = new Transaction(document, "Fill empty parameters");

        transaction.Start();

        foreach (var element in elements)
        {
            ProcessParameters(element.Parameters, parameterNames);

            var typeId = element.GetTypeId();

            if (typeId == ElementId.InvalidElementId)
                continue;

            if (!processedTypeIds.Add(typeId))
                continue;

            if (document.GetElement(typeId) is ElementType elementType)
            {
                ProcessParameters(elementType.Parameters, parameterNames);
            }
        }

        transaction.Commit();

        ToadDialogService.Show(
            "Успех!",
            "Значение записано в выбранные параметры!",
            DialogButtons.OK,
            DialogIcon.Info
        );
    }

    private static void ProcessParameters(
        ParameterSet parameters,
        HashSet<string> parameterNames)
    {
        foreach (Parameter parameter in parameters)
        {
            if (parameter == null)
                continue;

            var definition = parameter.Definition;

            if (definition == null)
                continue;

            var parameterName = definition.Name;

            if (string.IsNullOrWhiteSpace(parameterName))
                continue;

            if (!parameterNames.Contains(parameterName))
                continue;

            if (parameter.IsReadOnly)
                continue;

            if (parameter.StorageType != StorageType.String)
                continue;

            var currentValue = parameter.AsString();

            if (!string.IsNullOrWhiteSpace(currentValue))
                continue;

            parameter.Set("-");
        }
    }
}