using System.IO;
using ClassifierCode.Engine.Profile;
using Microsoft.Win32;
using ToadTools.UI.Models;
using ToadTools.UI.Services;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace ClassifierCode.Services;

/// <summary>
///     Готовит в проекте параметры записи профиля для всех его категорий:
///     отсутствующие параметры добавляет из ФОП, подключённого к Revit, а если там их нет — из ФОП,
///     выбранного пользователем (привязка экземпляра); к уже привязанным параметрам добавляет недостающие
///     категории, сохраняя тип привязки и уже привязанные категории.
///     Параметр ищется по GUID общего параметра, если он задан в профиле, иначе по имени.
/// </summary>
public sealed class ClassifierParameterLoader(RevitApplication app, Document doc, IReadOnlyList<OutputField> outputs, IReadOnlyList<BuiltInCategory> categories)
{
    private readonly List<OutputField> _outputs = outputs.Where(o => !string.IsNullOrWhiteSpace(o.Parameter)).ToList();

    /// <summary>
    ///     true — параметры есть во всех категориях (или успешно добавлены), false — пользователь отменил
    ///     или добавить не удалось (причина уже показана). note — что было сделано, для отчёта.
    /// </summary>
    public bool EnsureParameters(out string? note)
    {
        note = null;
        var notes = new List<string>();

        var missing = _outputs.Where(output => FindBinding(output) == null).ToList();
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

    private static string Names(IEnumerable<OutputField> outputs) => string.Join(", ", outputs.Select(o => "\"" + o.Parameter + "\""));

    /// <summary>Добавляет отсутствующие параметры из ФОП и привязывает их ко всем категориям.</summary>
    private bool AddMissingParameters(List<OutputField> missing, out string? note)
    {
        note = null;
        var originalFile = app.SharedParametersFilename;
        try
        {
            var definitions = FindInConnectedFile(missing, out var usedFile)
                              ?? FindInUserFile(missing, originalFile, out usedFile);
            if (definitions == null)
                return false;

            Bind(definitions);
            note = "В проект добавлены параметры " + Names(missing) + " из ФОП: " + usedFile;
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
        foreach (var output in _outputs)
        {
            if (FindBinding(output) is not { Binding: ElementBinding binding } found)
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
            var categorySet = app.Create.NewCategorySet();
            foreach (Category category in change.Binding.Categories)
                categorySet.Insert(category);
            foreach (var category in change.Added)
                categorySet.Insert(category);

            ElementBinding extended = change.Binding is TypeBinding
                ? app.Create.NewTypeBinding(categorySet)
                : app.Create.NewInstanceBinding(categorySet);
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

    /// <summary>Привязка параметра проекта; null — параметра в проекте нет.</summary>
    private (Definition Definition, Binding Binding)? FindBinding(OutputField output)
    {
        if (output.Guid is { } guid && SharedParameterElement.Lookup(doc, guid)?.GetDefinition() is { } shared &&
            doc.ParameterBindings.get_Item(shared) is { } sharedBinding)
            return (shared, sharedBinding);

        var iterator = doc.ParameterBindings.ForwardIterator();
        while (iterator.MoveNext())
        {
            if (iterator.Key != null && iterator.Key.Name == output.Parameter)
                return (iterator.Key, (Binding)iterator.Current);
        }

        return null;
    }

    /// <summary>Категории профиля, которые есть в документе и допускают параметры проекта.</summary>
    private List<Category> GetTargetCategories()
    {
        return categories
            .Select(builtIn => Category.GetCategory(doc, builtIn))
            .Where(category => category != null && category.AllowsBoundParameters)
            .ToList();
    }

    private static void ShowError(string message)
    {
        ToadDialogService.Show("Ошибка!", message, DialogButtons.OK, DialogIcon.Error);
    }

    /// <summary>ФОП, подключённый в настройках Revit. null — файла нет или в нём нет нужных параметров.</summary>
    private List<ExternalDefinition>? FindInConnectedFile(List<OutputField> missing, out string? usedFile)
    {
        usedFile = app.SharedParametersFilename;
        if (string.IsNullOrEmpty(usedFile) || !File.Exists(usedFile))
            return null;

        try
        {
            return FindDefinitions(app.OpenSharedParameterFile(), missing);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return null; // повреждённый ФОП — спросим другой
        }
    }

    /// <summary>Просит пользователя выбрать ФОП, пока не найдутся параметры или пользователь не откажется.</summary>
    private List<ExternalDefinition>? FindInUserFile(List<OutputField> missing, string? connectedFile, out string? usedFile)
    {
        usedFile = null;
        var hasConnectedFile = !string.IsNullOrEmpty(connectedFile) && File.Exists(connectedFile);
        var reason = hasConnectedFile
            ? "В подключённом ФОП нет нужных параметров:" + Environment.NewLine + connectedFile
            : "К Revit не подключён файл общих параметров (ФОП).";

        while (true)
        {
            var answer = ToadDialogService.Show(
                "В проекте нет параметров для записи кода",
                reason + Environment.NewLine + Environment.NewLine +
                "Нужны: " + Names(missing) + "." + Environment.NewLine +
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

            var definitions = FindDefinitions(file, missing);
            if (definitions != null)
            {
                usedFile = path;
                return definitions;
            }

            reason = "В выбранном ФОП нет нужных параметров:" + Environment.NewLine + path;
        }
    }

    /// <summary>Все запрошенные параметры из ФОП (по всем группам) или null, если хотя бы одного нет.</summary>
    private static List<ExternalDefinition>? FindDefinitions(DefinitionFile? file, List<OutputField> missing)
    {
        if (file == null)
            return null;

        var all = new List<ExternalDefinition>();
        foreach (DefinitionGroup group in file.Groups)
        foreach (var definition in group.Definitions)
            if (definition is ExternalDefinition external)
                all.Add(external);

        var found = new List<ExternalDefinition>();
        foreach (var output in missing)
        {
            var definition = output.Guid is { } guid
                ? all.FirstOrDefault(d => d.GUID == guid)
                : all.FirstOrDefault(d => d.Name == output.Parameter);
            if (definition == null)
                return null;
            found.Add(definition);
        }

        return found;
    }

    private void Bind(IEnumerable<ExternalDefinition> definitions)
    {
        var categorySet = app.Create.NewCategorySet();
        foreach (var category in GetTargetCategories())
            categorySet.Insert(category);

        using var transaction = new Transaction(doc, "Добавление параметров классификатора");
        transaction.Start();
        foreach (var definition in definitions)
        {
            var binding = app.Create.NewInstanceBinding(categorySet);
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
