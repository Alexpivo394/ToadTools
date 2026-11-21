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
    private readonly AsyncRequestExternalEvent<(string Path, bool Overwrite), bool> _save = new(Save);

    public int RevitYear { get; } = revitYear;

    public Task<ActiveDocument?> ActiveDocumentAsync() => _active.RaiseAsync();

    public Task<LoadOutcome> LoadAsync(LoadRequest request) => _load.RaiseAsync(request);

    public Task<IReadOnlyList<Core.ProjectFamily>> ProjectFamiliesAsync() => _families.RaiseAsync();

    public Task SaveFamilyAsync(string path, bool overwrite) => _save.RaiseAsync((path, overwrite));

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
                f.Family.GetFamilySymbolIds().Select(project.GetElement).OfType<FamilySymbol>().Select(s => s.Name).ToList()))
            .ToList();
    }

    private static bool Save(UIApplication application, (string Path, bool Overwrite) target)
    {
        if (application.ActiveUIDocument?.Document is not { IsFamilyDocument: true } document) throw new InvalidOperationException("Откройте семейство в редакторе семейств");
        Directory.CreateDirectory(Path.GetDirectoryName(target.Path)!);
        document.SaveAs(target.Path, new SaveAsOptions { OverwriteExistingFile = target.Overwrite, Compact = true });
        return true;
    }
}
