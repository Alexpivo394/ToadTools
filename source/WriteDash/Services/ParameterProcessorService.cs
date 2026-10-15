using Nice3point.Revit.Toolkit;
using ToadTools.UI.Models;
using ToadTools.UI.Progress;
using Wpf.Ui.Controls;
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

        try
        {
            using var progress = ToadProgress.Show("Записать прочерк",
                new ProgressOptions { Icon = SymbolRegular.TextEditStyle24 });
            progress.Stage("Сбор элементов");

            var elements = new FilteredElementCollector(document)
                .WhereElementIsNotElementType()
                .Where(x =>
                    x.Category != null &&
                    x.Category.CategoryType == CategoryType.Model)
                .ToList();

            using var transaction = new Transaction(document, "Fill empty parameters");

            transaction.Start();
            FillElements(document, elements, parameterNames, progress);
            transaction.Commit();
        }
        catch (OperationCanceledException)
        {
            ToadDialogService.Show(
                "Прервано",
                "Запись прервана, изменения в модели откачены.",
                DialogButtons.OK,
                DialogIcon.Info
            );

            return;
        }

        ToadDialogService.Show(
            "Успех!",
            "Значение записано в выбранные параметры!",
            DialogButtons.OK,
            DialogIcon.Info
        );
    }

    private static void FillElements(Document document, List<Element> elements, HashSet<string> parameterNames,
        ToadProgress progress)
    {
        var processedTypeIds = new HashSet<ElementId>();

        foreach (var element in progress.Track(elements, "Заполнение пустых параметров"))
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