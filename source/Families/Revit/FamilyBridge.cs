using System.IO;
using Alexpivo394.Revit.Families.Files;
using Alexpivo394.Revit.Families.Revit;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Families.Core;
using Nice3point.Revit.Toolkit.External;

namespace Families.Revit;

/// <summary>Панель ходит в Revit только отсюда: всё через внешние события Toolkit, сеть сюда не попадает.</summary>
public sealed class FamilyBridge(int revitYear) : IFamilyBridge
{
    private readonly AsyncRequestExternalEvent<ActiveDocument?> _active = new(Active);
    private readonly AsyncRequestExternalEvent<LoadRequest, LoadOutcome> _load = new(Load);
    private readonly AsyncRequestExternalEvent<IReadOnlyList<Core.ProjectFamily>> _families = new(Families);
    private readonly AsyncRequestExternalEvent<(string Path, bool Overwrite, string Root), PublishedCopy> _publish = new(PublishCopy);

    /// <summary>Куда уходит документ, открытый прямо из библиотеки или ещё не сохранённый, перед публикацией копии.</summary>
    public static string DraftsFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToadTools", "Families", "Черновики");

    public int RevitYear { get; } = revitYear;

    public Task<ActiveDocument?> ActiveDocumentAsync() => _active.RaiseAsync();

    public Task<LoadOutcome> LoadAsync(LoadRequest request) => _load.RaiseAsync(request);

    public Task<IReadOnlyList<Core.ProjectFamily>> ProjectFamiliesAsync() => _families.RaiseAsync();

    public Task<PublishedCopy> PublishCopyAsync(string path, bool overwrite, string libraryRoot) => _publish.RaiseAsync((path, overwrite, libraryRoot));

    private static ActiveDocument? Active(UIApplication application) =>
        application.ActiveUIDocument?.Document is { } document ? new ActiveDocument(document.Title, document.IsFamilyDocument, document.PathName ?? "") : null;

    private static Document Project(UIApplication application) =>
        application.ActiveUIDocument?.Document is { IsFamilyDocument: false } document ? document : throw new InvalidOperationException("Откройте модель проекта");

    private static LoadOutcome Load(UIApplication application, LoadRequest request)
    {
        var project = Project(application);
        if (!File.Exists(request.Path)) throw new InvalidOperationException($"Файла «{request.Path}» нет — его переложили или нет доступа к папке библиотеки");

        // С каталогом .txt выбираются только типоразмеры, которые в каталоге есть: у семейства проекта могли быть свои, которых в библиотеке нет.
        var types = request.Types;
        var catalog = Path.ChangeExtension(request.Path, ".txt");
        if (types is not null && File.Exists(catalog))
        {
            var known = new HashSet<string>(TypeCatalogParser.ParseFile(catalog).Rows.Select(r => r.TypeName), StringComparer.Ordinal);
            types = types.Where(known.Contains).ToList();
            if (types.Count == 0) types = null;
        }

        try
        {
            var result = FamilyLoader.Load(project, request.Path, types, new FamilyStamp(request.CardId, request.Hash, DateTime.UtcNow));
            using (var transaction = new Transaction(project, "ToadTools: итерация семейства"))
            {
                transaction.Start();
                IterationStamps.Write(result.Family, request.IterationId);
                transaction.Commit();
            }

            return new LoadOutcome(result.Types, result.Replaced);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException exception)
        {
            throw new InvalidOperationException($"Revit не загрузил семейство: {exception.Message}", exception);
        }
    }

    private static IReadOnlyList<Core.ProjectFamily> Families(UIApplication application)
    {
        var project = Project(application);
        return FamilyStamps.ReadAll(project)
            .Select(f => new Core.ProjectFamily(f.Name, f.Category, f.Stamp?.CardId, f.Stamp?.Hash,
                f.Family.GetFamilySymbolIds().Select(project.GetElement).OfType<FamilySymbol>().Select(s => s.Name).ToList(),
                f.Stamp is null ? null : IterationStamps.Read(f.Family)))
            .ToList();
    }

    /// <summary>
    ///     Документ сохраняется на своё место, а в библиотеку уходит копия файла с каталогом <c>.txt</c>. Открытый прямо из библиотеки или
    ///     ещё не сохранённый документ сначала уходит в черновики: иначе следующее «Сохранить» писало бы в библиотеку мимо публикации.
    /// </summary>
    private static PublishedCopy PublishCopy(UIApplication application, (string Path, bool Overwrite, string Root) target)
    {
        if (application.ActiveUIDocument?.Document is not { IsFamilyDocument: true } document) throw new InvalidOperationException("Откройте семейство в редакторе семейств");
        if (!target.Overwrite && File.Exists(target.Path)) throw new InvalidOperationException($"Файл «{target.Path}» уже есть");

        var source = document.PathName ?? "";
        var root = target.Root.TrimEnd('\\') + "\\";
        var redirected = source.Length == 0 || source.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        if (redirected)
        {
            Directory.CreateDirectory(DraftsFolder);
            var draft = Path.Combine(DraftsFolder, Path.GetFileName(target.Path));
            var catalogBefore = source.Length > 0 ? Path.ChangeExtension(source, ".txt") : null;
            RevitSave(() => document.SaveAs(draft, new SaveAsOptions { OverwriteExistingFile = true, Compact = true }));
            // Каталог типоразмеров лежал рядом с файлом библиотеки — с черновиком он тоже нужен.
            if (catalogBefore is not null && File.Exists(catalogBefore)) File.Copy(catalogBefore, Path.ChangeExtension(draft, ".txt"), overwrite: true);
            source = draft;
        }
        else if (document.IsModified)
            RevitSave(() => document.Save(new SaveOptions { Compact = true }));

        Directory.CreateDirectory(Path.GetDirectoryName(target.Path)!);
        File.Copy(source, target.Path, target.Overwrite);
        var sourceCatalog = Path.ChangeExtension(source, ".txt");
        var targetCatalog = Path.ChangeExtension(target.Path, ".txt");
        if (File.Exists(sourceCatalog)) File.Copy(sourceCatalog, targetCatalog, overwrite: true);
        // Без каталога у новой версии прежний каталог описывал бы чужие типоразмеры; его копия остаётся в истории библиотеки.
        else if (target.Overwrite && File.Exists(targetCatalog)) File.Delete(targetCatalog);
        return new PublishedCopy(source, redirected);
    }

    /// <summary>Отказ Revit сохранить (файл занят, только для чтения) — понятной ошибкой панели, а не сбоем команды.</summary>
    private static void RevitSave(Action save)
    {
        try
        {
            save();
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException exception)
        {
            throw new InvalidOperationException($"Revit не сохранил семейство: {exception.Message}", exception);
        }
    }
}
