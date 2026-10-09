using System.IO;
using ClassifierCode.Models;
using Microsoft.Win32;
using ToadTools.UI.Models;
using ToadTools.UI.Services;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace ClassifierCode.Services;

/// <summary>
///     Добавляет в проект параметры "ADSK_Код по классификатору" и "ADSK_Описание по классификатору",
///     если их нет: берёт из ФОП, подключённого к Revit, а если там их нет — из ФОП, выбранного пользователем.
///     Параметры привязываются как параметры экземпляра к категориям ИОС.
/// </summary>
public sealed class ClassifierParameterLoader(RevitApplication app, Document doc)
{
    private const string Title = "Код по классификатору";

    /// <summary>Категории, к которым привязываются параметры (те, что обрабатывает плагин).</summary>
    private static readonly BuiltInCategory[] Categories =
    [
        BuiltInCategory.OST_PipeCurves,
        BuiltInCategory.OST_FlexPipeCurves,
        BuiltInCategory.OST_PipeFitting,
        BuiltInCategory.OST_PipeAccessory,
        BuiltInCategory.OST_PipeInsulations,
        BuiltInCategory.OST_Sprinklers,
        BuiltInCategory.OST_PlumbingFixtures,
        BuiltInCategory.OST_MechanicalEquipment,
        BuiltInCategory.OST_DuctCurves,
        BuiltInCategory.OST_FlexDuctCurves,
        BuiltInCategory.OST_DuctFitting,
        BuiltInCategory.OST_DuctAccessory,
        BuiltInCategory.OST_DuctTerminal,
        BuiltInCategory.OST_DuctInsulations,
        BuiltInCategory.OST_DuctLinings,
        BuiltInCategory.OST_FireAlarmDevices,
        BuiltInCategory.OST_ElectricalEquipment,
        BuiltInCategory.OST_SpecialityEquipment,
        BuiltInCategory.OST_GenericModel
    ];

    /// <summary>
    ///     true — параметры есть (или успешно добавлены), false — пользователь отменил или добавить не удалось
    ///     (причина уже показана). note — что было сделано, для отчёта.
    /// </summary>
    public bool EnsureParameters(out string? note)
    {
        note = null;
        var missing = ClassifierParameters.All.Where(name => !IsBound(name)).ToList();
        if (missing.Count == 0)
            return true;

        var originalFile = app.SharedParametersFilename;
        try
        {
            var definitions = FindInConnectedFile(missing, out var usedFile)
                              ?? FindInUserFile(missing, originalFile, out usedFile);
            if (definitions == null)
                return false;

            Bind(definitions.Values);
            note = "В проект добавлены параметры " + string.Join(", ", missing.Select(n => "\"" + n + "\"")) +
                   " из ФОП: " + usedFile;
            return true;
        }
        catch (Exception ex)
        {
            ToadDialogService.Show(
                "Ошибка!",
                "Не удалось добавить параметры классификатора в проект:" + Environment.NewLine + ex.Message,
                DialogButtons.OK,
                DialogIcon.Error);
            return false;
        }
        finally
        {
            RestoreSharedParameterFile(originalFile);
        }
    }

    /// <summary>Параметр уже привязан к проекту (как параметр проекта).</summary>
    private bool IsBound(string name)
    {
        var iterator = doc.ParameterBindings.ForwardIterator();
        while (iterator.MoveNext())
        {
            if (iterator.Key != null && iterator.Key.Name == name)
                return true;
        }

        return false;
    }

    /// <summary>ФОП, подключённый в настройках Revit. null — файла нет или в нём нет нужных параметров.</summary>
    private Dictionary<string, ExternalDefinition>? FindInConnectedFile(List<string> names, out string? usedFile)
    {
        usedFile = app.SharedParametersFilename;
        if (string.IsNullOrEmpty(usedFile) || !File.Exists(usedFile))
            return null;

        try
        {
            return FindDefinitions(app.OpenSharedParameterFile(), names);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return null; // повреждённый ФОП — спросим другой
        }
    }

    /// <summary>Просит пользователя выбрать ФОП, пока не найдутся параметры или пользователь не откажется.</summary>
    private Dictionary<string, ExternalDefinition>? FindInUserFile(List<string> names, string? connectedFile, out string? usedFile)
    {
        usedFile = null;
        var hasConnectedFile = !string.IsNullOrEmpty(connectedFile) && File.Exists(connectedFile);
        var reason = hasConnectedFile
            ? "В подключённом ФОП нет нужных параметров:" + Environment.NewLine + connectedFile
            : "К Revit не подключён файл общих параметров (ФОП).";

        while (true)
        {
            var answer = ToadDialogService.Show(
                "В проекте нет параметров классификатора",
                reason + Environment.NewLine + Environment.NewLine +
                "Нужны: " + string.Join(", ", names.Select(n => "\"" + n + "\"")) + "." + Environment.NewLine +
                "Нажмите OK, чтобы указать ФОП, в котором они есть.",
                DialogButtons.OKCancel,
                DialogIcon.Warning);
            if (answer != "OK")
                return null;

            var dialog = new OpenFileDialog
            {
                Title = "Файл общих параметров (ФОП)",
                Filter = "Файл общих параметров (*.txt)|*.txt|Все файлы (*.*)|*.*"
            };
            if (hasConnectedFile)
                dialog.InitialDirectory = Path.GetDirectoryName(connectedFile);
            if (dialog.ShowDialog() != true)
                return null;

            var path = dialog.FileName;
            DefinitionFile? file;
            try
            {
                app.SharedParametersFilename = path;
                file = app.OpenSharedParameterFile();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                file = null;
            }

            if (file == null)
            {
                reason = "Файл не читается как ФОП Revit:" + Environment.NewLine + path;
                continue;
            }

            var definitions = FindDefinitions(file, names);
            if (definitions != null)
            {
                usedFile = path;
                return definitions;
            }

            reason = "В выбранном ФОП нет нужных параметров:" + Environment.NewLine + path;
        }
    }

    /// <summary>Все запрошенные параметры из ФОП (по всем группам) или null, если хотя бы одного нет.</summary>
    private static Dictionary<string, ExternalDefinition>? FindDefinitions(DefinitionFile? file, List<string> names)
    {
        if (file == null)
            return null;

        var found = new Dictionary<string, ExternalDefinition>();
        foreach (DefinitionGroup group in file.Groups)
        {
            foreach (var definition in group.Definitions)
            {
                if (definition is ExternalDefinition external && names.Contains(external.Name) && !found.ContainsKey(external.Name))
                    found[external.Name] = external;
            }
        }

        return found.Count == names.Count ? found : null;
    }

    private void Bind(IEnumerable<ExternalDefinition> definitions)
    {
        var categories = app.Create.NewCategorySet();
        foreach (var builtIn in Categories)
        {
            var category = Category.GetCategory(doc, builtIn);
            if (category != null && category.AllowsBoundParameters)
                categories.Insert(category);
        }

        using var transaction = new Transaction(doc, "Добавление параметров классификатора");
        transaction.Start();
        foreach (var definition in definitions)
        {
            var binding = app.Create.NewInstanceBinding(categories);
#if REVIT2022_OR_GREATER
            var inserted = doc.ParameterBindings.Insert(definition, binding, GroupTypeId.IdentityData);
#else
            var inserted = doc.ParameterBindings.Insert(definition, binding, BuiltInParameterGroup.PG_IDENTITY_DATA);
#endif
            if (!inserted)
                throw new InvalidOperationException("Revit не принял параметр \"" + definition.Name + "\".");
        }

        transaction.Commit();
    }

    /// <summary>Возвращаем пользователю его настройку ФОП, если меняли её для выбранного файла.</summary>
    private void RestoreSharedParameterFile(string? originalFile)
    {
        try
        {
            if (app.SharedParametersFilename != originalFile)
                app.SharedParametersFilename = originalFile ?? string.Empty;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // Пустой путь Revit может не принять — настройка останется на выбранном файле.
        }
    }
}
