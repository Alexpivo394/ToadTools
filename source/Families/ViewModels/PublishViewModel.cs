using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Families.Core;
using ToadTools.Connection;

namespace Families.ViewModels;

/// <summary>Заявка в выборе «эта публикация закрывает заявку»; без заявки — пустой вариант.</summary>
public sealed record RequestOption(Guid? Id, string Label);

/// <summary>
///     «Опубликовать» (координатор, в редакторе семейства): новое семейство — в папку проверки или сразу в папку библиотеки; новая
///     версия существующего — в папку проверки под его именем. Файл сохраняет Revit, сервер получает автора, описание и заявку и
///     привязывает их к версии, когда обход её увидит.
/// </summary>
public sealed partial class PublishViewModel(IFamiliesApi api, IFamilyBridge revit, IFamilyDialogs dialogs, Func<string, Task<string>> hashFile) : ObservableObject
{
    /// <summary>Год, в котором отдел хранит семейства (владелец): семейство новее не попадёт в проекты 2022.</summary>
    public const int StandardYear = 2022;

    private LibraryInfo? _library;

    /// <summary>true — новая версия существующего семейства, false — новое семейство.</summary>
    [ObservableProperty] private bool _isNewVersion;

    [ObservableProperty] private string _name = "";

    /// <summary>Куда положить новое: папка от корня; по умолчанию — папка проверки.</summary>
    [ObservableProperty] private string _folder = "";

    [ObservableProperty] private string _existingQuery = "";
    [ObservableProperty] private FamilyHit? _existing;
    [ObservableProperty] private string _comment = "";
    [ObservableProperty] private RequestOption? _request;
    [ObservableProperty] private string _result = "";
    [ObservableProperty] private bool _isBusy;

    /// <summary>Предупреждение о версии Revit; пусто — Revit 2022.</summary>
    public string YearWarning => revit.RevitYear > StandardYear
        ? $"Семейство сохранится в Revit {revit.RevitYear}: проекты в Revit {StandardYear} его не получат. Публикуйте из Revit {StandardYear}, если оно нужно всем"
        : "";

    public ObservableCollection<FamilyHit> ExistingResults { get; } = [];

    public ObservableCollection<RequestOption> Requests { get; } = [];

    /// <summary>Открыт редактор семейства: имя по умолчанию — имя файла, заявки — открытые.</summary>
    public async Task PrepareAsync(LibraryInfo library, ActiveDocument document)
    {
        _library = library;
        if (Name.Length == 0 || Path.GetFileNameWithoutExtension(document.Title) != Name) Name = Path.GetFileNameWithoutExtension(document.Title);
        if (Folder.Length == 0) Folder = library.ReviewFolder;
        Result = "";
        OnPropertyChanged(nameof(YearWarning));
        try
        {
            var open = await api.OpenRequestsAsync(CancellationToken.None);
            Requests.Clear();
            Requests.Add(new RequestOption(null, "Без заявки"));
            foreach (var request in open) Requests.Add(new RequestOption(request.Id, $"№ {request.Number} · {request.Title}"));
            Request ??= Requests[0];
        }
        catch (ToadFlowRequestException)
        {
            // без списка заявок публикация всё равно возможна
        }
    }

    [RelayCommand]
    private async Task FindExisting()
    {
        if (ExistingQuery.Trim().Length < 2) return;
        var page = await api.SearchAsync(new FamilySearch(ExistingQuery, null, null, true), 1, CancellationToken.None);
        ExistingResults.Clear();
        foreach (var hit in page.Items.Take(20)) ExistingResults.Add(hit);
        Existing = ExistingResults.FirstOrDefault(h => h.Name.Equals(ExistingQuery.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Куда ляжет файл: полный путь в библиотеке; null — данных не хватает (причина — в <see cref="Result" />).</summary>
    public string? TargetPath()
    {
        if (_library is null) return null;
        if (IsNewVersion)
            return Existing is null ? null : Path.Combine(_library.Root, _library.ReviewFolder, Existing.Name + ".rfa");
        var name = Name.Trim();
        var folder = Folder.Replace('/', '\\').Trim().Trim('\\');
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        if (folder.Split('\\').Any(part => part is ".." or ".")) return null;
        return folder.Length == 0 ? Path.Combine(_library.Root, name + ".rfa") : Path.Combine(_library.Root, folder, name + ".rfa");
    }

    [RelayCommand]
    private async Task Publish()
    {
        Result = "";
        if (TargetPath() is not { } target)
        {
            Result = IsNewVersion ? "Найдите и выберите семейство, новую версию которого публикуете" : "Имя файла — без знаков \\ / : * ? \" < > |, папка — без «..»";
            return;
        }

        IsBusy = true;
        try
        {
            if (!IsNewVersion)
            {
                // Имя занято — семейство в проекте затёрло бы другое: такую публикацию сервер не примет, файл лучше не сохранять вовсе.
                var taken = (await api.ProjectStatusAsync([new ProjectFamilyDto(Name.Trim(), null, null)], true, CancellationToken.None)).FirstOrDefault();
                if (taken?.CardId is not null)
                {
                    Result = $"Имя «{Name.Trim()}» уже занято семейством библиотеки «{taken.CardName}». Выберите «Новая версия существующего» или другое имя";
                    return;
                }

                if (File.Exists(target))
                {
                    Result = $"Файл «{target}» уже есть — выберите другое имя или папку";
                    return;
                }
            }

            if (YearWarning.Length > 0 && !dialogs.Confirm(YearWarning + ". Всё равно опубликовать?")) return;

            await revit.SaveFamilyAsync(target, overwrite: IsNewVersion);
            var hash = await hashFile(target);
            var published = await api.PublishAsync(new PublishRequest(target, hash, IsNewVersion ? Existing!.Id : null,
                string.IsNullOrWhiteSpace(Comment) ? null : Comment.Trim(), Request?.Id), CancellationToken.None);
            Result = $"Опубликовано в «{published.RelativePath}». В каталоге появится после обхода библиотеки"
                     + (published.ScanStarted ? " — он уже начат" : " — он начнётся сразу после текущего")
                     + (target.Contains(_library!.ReviewFolder) ? ". Пока семейство на проверке, проектировщики его не видят" : "");
            Comment = "";
        }
        catch (ToadFlowRequestException exception)
        {
            Result = $"Файл сохранён, но ToadFlow не принял публикацию: {exception.Message}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Result = $"Не сохранилось: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
