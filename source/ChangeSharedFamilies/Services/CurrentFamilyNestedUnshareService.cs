using Autodesk.Revit.DB;
using System.IO;
using ToadTools.UI.Progress;
using Wpf.Ui.Controls;

namespace ChangeSharedFamilies.Services;

public class CurrentFamilyNestedUnshareService
{
    private readonly Document _familyDocument;
    private readonly NestedFamilyFinder _familyFinder;
    private readonly NestedFamilyLoader _familyLoader;
    private readonly NestedFamilyReplacementService _replacementService;
    private readonly NestedFamilyTempFileService _tempFileService;
    private readonly string _tempFolder;

    public CurrentFamilyNestedUnshareService(Document familyDocument)
    {
        _familyDocument = familyDocument ?? throw new ArgumentNullException(nameof(familyDocument));

        _tempFolder = Path.Combine(
            Path.GetTempPath(),
            "RevitNestedFamilyUnshare");

        _familyFinder = new NestedFamilyFinder(_familyDocument);
        _familyLoader = new NestedFamilyLoader(_familyDocument);
        _replacementService = new NestedFamilyReplacementService(_familyDocument);
        _tempFileService = new NestedFamilyTempFileService(_familyDocument);
    }

    public NestedFamilyConvertReport ConvertSharedNestedFamiliesToNonShared()
    {
        ValidateCurrentDocument();

        Directory.CreateDirectory(_tempFolder);

        var report = new NestedFamilyConvertReport();

        // Каждое семейство заменяется своими транзакциями, поэтому «Прервать» останавливает работу
        // после текущего семейства, а уже заменённые остаются.
        using var progress = ToadProgress.Show("Замена общих семейств", new ProgressOptions
        {
            Icon = SymbolRegular.ShareAndroid24,
            CancelHint = "Остановимся после текущего семейства — уже заменённые останутся"
        });
        progress.Stage("Поиск общих вложенных семейств");

        var families = _familyFinder.GetSharedNestedFamilies().ToList();
        for (var i = 0; i < families.Count; i++)
        {
            if (progress.IsCancelled)
            {
                report.Cancelled = true;
                break;
            }

            var oldNestedFamily = families[i];
            var oldFamilyName = oldNestedFamily.Name;
            progress.Stage("Замена семейств");
            progress.Report(i, families.Count, $"{i + 1} из {families.Count}: {oldFamilyName}");

            try
            {
                ConvertOneNestedFamily(oldNestedFamily);

                report.ConvertedFamiliesCount++;
                report.ConvertedFamilyNames.Add(oldFamilyName);
            }
            catch (Exception exception)
            {
                report.Errors.Add($"{oldFamilyName}: {exception.Message}");
            }
        }

        return report;
    }

    private void ValidateCurrentDocument()
    {
        if (!_familyDocument.IsFamilyDocument)
        {
            throw new InvalidOperationException(
                "Активный документ не является семейством. Команду надо запускать из редактора семейства.");
        }

        if (_familyDocument.IsReadOnly)
        {
            throw new InvalidOperationException(
                "Текущее семейство открыто только для чтения.");
        }

        if (_familyDocument.IsModifiable)
        {
            throw new InvalidOperationException(
                "Документ сейчас modifiable. Нельзя запускать этот сервис внутри уже открытой Transaction.");
        }
    }

    private void ConvertOneNestedFamily(Family oldNestedFamily)
    {
        var oldFamilyName = oldNestedFamily.Name;
        var tempFamilyName = NestedFamilyTempName.CreateUnique(_familyDocument, oldFamilyName);
        var tempFamilyPath = Path.Combine(_tempFolder, tempFamilyName + ".rfa");

        try
        {
            _tempFileService.CreateNonSharedTempFamilyFile(
                oldNestedFamily,
                tempFamilyName,
                tempFamilyPath);

            var tempLoadedFamily = _familyLoader.LoadTempFamily(
                tempFamilyPath,
                tempFamilyName);

            _replacementService.ReplaceOldNestedFamilyWithTempFamily(
                oldNestedFamily,
                tempLoadedFamily,
                oldFamilyName);
        }
        finally
        {
            TempFileCleaner.TryDelete(tempFamilyPath);
        }
    }
}
