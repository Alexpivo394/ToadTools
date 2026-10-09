using System.IO;
using ClassifierCode.Models;
using Microsoft.Win32;
using ToadTools.UI.Models;
using ToadTools.UI.Services;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace ClassifierCode.Services;

/// <summary>
///     Готовит в проекте параметры "ADSK_Код по классификатору" и "ADSK_Описание по классификатору"
///     для всех категорий <see cref="ClassifierCategories" />:
///     отсутствующие параметры добавляет из ФОП, подключённого к Revit, а если там их нет — из ФОП,
///     выбранного пользователем (привязка экземпляра); к уже привязанным параметрам добавляет недостающие
///     категории, сохраняя тип привязки и уже привязанные категории.
/// </summary>
public sealed class ClassifierParameterLoader(RevitApplication app, Document doc)
{
    /// <summary>
    ///     true — параметры есть во всех категориях (или успешно добавлены), false — пользователь отменил
    ///     или добавить не удалось (причина уже показана). note — что было сделано, для отчёта.
    /// </summary>
    public bool EnsureParameters(out string? note)
    {
        note = null;
        var notes = new List<string>();

        var missing = ClassifierParameters.All.Where(name => FindBinding(name) == null).ToList();
        if (missing.Count > 0)
        {
            if (!AddMissingParameters(missing, out var addedNote))
                return false;
            notes.Add(addedNote!);
        }

        try
        {
            var extendedNote = ExtendBindings();
            if (extendedNote != null)
                notes.Add(extendedNote);
        }
        catch (Exception ex)
        {
            ShowError("Не удалось добавить категории к параметрам классификатора:" + Environment.NewLine + ex.Message);
            return false;
        }

        if (notes.Count > 0)
            note = string.Join(Environment.NewLine, notes);
        return true;
    }

    /// <summary>Добавляет отсутствующие параметры из ФОП и привязывает их ко всем категориям.</summary>
    private bool AddMissingParameters(List<string> missing, out string? note)
    {
        note = null;
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
            ShowError("Не удалось добавить параметры классификатора в проект:" + Environment.NewLine + ex.Message);
            return false;
        }
        finally
        {
            RestoreSharedParameterFile(originalFile);
        }
    }

    /// <summary>
    ///     Добавляет к привязке параметров категории, которых в ней нет.
    ///     null — все категории уже привязаны, иначе — что добавлено, для отчёта.
    /// </summary>
    private string? ExtendBindings()
    {
        var targetCategories = GetTargetCategories();
        var changes = new List<(Definition Definition, ElementBinding Binding, List<Category> Added)>();
        foreach (var name in ClassifierParameters.All)
        {
            if (FindBinding(name) is not { Binding: ElementBinding binding } found)
                continue;

            var boundIds = new HashSet<ElementId>();
            foreach (Category category in binding.Categories)
                boundIds.Add(category.Id);

            var added = targetCategories.Where(category => !boundIds.Contains(category.Id)).ToList();
            if (added.Count > 0)
                changes.Add((found.Definition, binding, added));
        }

        if (changes.Count == 0)
            return null;

        using var transaction = new Transaction(doc, "Добавление категорий к параметрам классификатора");
        transaction.Start();
        foreach (var change in changes)
        {
            var categories = app.Create.NewCategorySet();
            foreach (Category category in change.Binding.Categories)
                categories.Insert(category);
            foreach (var category in change.Added)
                categories.Insert(category);

            ElementBinding extended = change.Binding is TypeBinding
                ? app.Create.NewTypeBinding(categories)
                : app.Create.NewInstanceBinding(categories);
            if (!doc.ParameterBindings.ReInsert(change.Definition, extended))
                throw new InvalidOperationException("Revit не принял привязку параметра \"" + change.Definition.Name + "\".");
        }

        transaction.Commit();

        var addedNames = changes
            .SelectMany(change => change.Added)
            .Select(category => category.Name)
            .Distinct()
            .OrderBy(categoryName => categoryName);
        return "К параметрам классификатора добавлены категории: " + string.Join(", ", addedNames) + ".";
    }

    /// <summary>Привязка параметра проекта по имени; null — параметра в проекте нет.</summary>
    private (Definition Definition, Binding Binding)? FindBinding(string name)
    {
        var iterator = doc.ParameterBindings.ForwardIterator();
        while (iterator.MoveNext())
        {
            if (iterator.Key != null && iterator.Key.Name == name)
                return (iterator.Key, (Binding)iterator.Current);
        }

        return null;
    }

    /// <summary>Категории плагина, которые есть в документе и допускают параметры проекта.</summary>
    private List<Category> GetTargetCategories()
    {
        return ClassifierCategories.All
            .Select(builtIn => Category.GetCategory(doc, builtIn))
            .Where(category => category != null && category.AllowsBoundParameters)
            .ToList();
    }

    private static void ShowError(string message)
    {
        ToadDialogService.Show("Ошибка!", message, DialogButtons.OK, DialogIcon.Error);
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
        foreach (var category in GetTargetCategories())
            categories.Insert(category);

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
