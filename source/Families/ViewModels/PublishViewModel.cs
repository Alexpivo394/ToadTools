using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Families.Core;
using ToadTools.Connection;

namespace Families.ViewModels;

/// <summary>Заявка в выборе «эта публикация закрывает заявку»; без заявки — пустой вариант.</summary>
public sealed record RequestOption(Guid? Id, string Label);

/// <summary>Итерация в выборе «куда публиковать»; <see cref="Id" /> null — новая итерация.</summary>
public sealed record IterationOption(Guid? Id, string Label, string Name);

/// <summary>Куда публикуется открытое семейство.</summary>
public enum PublishMode
{
    /// <summary>Новое семейство — в папку проверки или сразу в папку библиотеки.</summary>
    NewFamily,

    /// <summary>Новая версия «Основной» существующего — в папку проверки под его именем.</summary>
    NewVersion,

    /// <summary>В итерацию существующего семейства — сразу, без проверки: существующую или новую.</summary>
    Iteration
}

/// <summary>
///     «Опубликовать» (координатор, в редакторе семейства). В библиотеку уходит копия файла с каталогом <c>.txt</c>; открытый документ
///     остаётся связан со своим файлом. Сервер получает автора, описание, заявку, итерацию и финал и привязывает их к версии, когда обход её
///     увидит.
/// </summary>
public sealed partial class PublishViewModel(IFamiliesApi api, IFamilyBridge revit, IFamilyDialogs dialogs, Func<string, Task<string>> hashFile) : ObservableObject
{
    /// <summary>Год, в котором отдел хранит семейства (владелец): семейство новее не попадёт в проекты 2022.</summary>
    public const int StandardYear = 2022;

    private const string NewIterationLabel = "Новая итерация…";

    private LibraryInfo? _library;
    private FamilyCardDto? _existingCard;

    [ObservableProperty] private PublishMode _mode;

    [ObservableProperty] private string _name = "";

    /// <summary>Куда положить новое: папка от корня; по умолчанию — папка проверки.</summary>
    [ObservableProperty] private string _folder = "";

    [ObservableProperty] private string _existingQuery = "";
    [ObservableProperty] private FamilyHit? _existing;
    [ObservableProperty] private IterationOption? _iteration;
    [ObservableProperty] private string _newIterationName = "";
    [ObservableProperty] private string _iterationComment = "";
    [ObservableProperty] private string _comment = "";
    [ObservableProperty] private RequestOption? _request;

    /// <summary>Без оповещения «Обновлено» тем, кто загружал семейство (мелкая правка).</summary>
    [ObservableProperty] private bool _silent;

    /// <summary>Версия итерации — финальная: итерация завершается.</summary>
    [ObservableProperty] private bool _isFinal;

    [ObservableProperty] private string _finalComment = "";
    [ObservableProperty] private string _result = "";
    [ObservableProperty] private bool _isBusy;

    public bool IsNewFamily
    {
        get => Mode == PublishMode.NewFamily;
        set
        {
            if (value) Mode = PublishMode.NewFamily;
        }
    }

    /// <summary>true — новая версия «Основной» существующего семейства.</summary>
    public bool IsNewVersion
    {
        get => Mode == PublishMode.NewVersion;
        set
        {
            if (value) Mode = PublishMode.NewVersion;
            else if (Mode == PublishMode.NewVersion) Mode = PublishMode.NewFamily;
        }
    }

    public bool IsIteration
    {
        get => Mode == PublishMode.Iteration;
        set
        {
            if (value) Mode = PublishMode.Iteration;
        }
    }

    /// <summary>Сервер знает итерации (ToadFlow 3.4+): старый принял бы файл итерации за чужое семейство с занятым именем.</summary>
    public bool IterationsAvailable => _library?.IterationsFolder is not null;

    /// <summary>Нужно выбрать существующее семейство: новая версия или итерация.</summary>
    public bool NeedsExisting => Mode != PublishMode.NewFamily;

    /// <summary>Выбрана «Новая итерация…».</summary>
    public bool IsNewIteration => Mode == PublishMode.Iteration && Iteration is { Id: null };

    /// <summary>Предупреждение о версии Revit; пусто — Revit 2022.</summary>
    public string YearWarning => revit.RevitYear > StandardYear
        ? $"Семейство сохранится в Revit {revit.RevitYear}: проекты в Revit {StandardYear} его не получат. Публикуйте из Revit {StandardYear}, если оно нужно всем"
        : "";

    public ObservableCollection<FamilyHit> ExistingResults { get; } = [];

    public ObservableCollection<RequestOption> Requests { get; } = [];

    /// <summary>Папки библиотеки для нового семейства: папка проверки и папки, где уже лежат семейства.</summary>
    public ObservableCollection<string> Folders { get; } = [];

    /// <summary>Незавершённые итерации выбранного семейства и «Новая итерация…».</summary>
    public ObservableCollection<IterationOption> Iterations { get; } = [];

    partial void OnModeChanged(PublishMode value)
    {
        OnPropertyChanged(nameof(IsNewFamily));
        OnPropertyChanged(nameof(IsNewVersion));
        OnPropertyChanged(nameof(IsIteration));
        OnPropertyChanged(nameof(NeedsExisting));
        OnPropertyChanged(nameof(IsNewIteration));
        if (value == PublishMode.Iteration) _ = LoadIterationsAsync();
    }

    partial void OnExistingChanged(FamilyHit? value)
    {
        _existingCard = null;
        Iterations.Clear();
        Iteration = null;
        if (Mode == PublishMode.Iteration) _ = LoadIterationsAsync();
    }

    partial void OnIterationChanged(IterationOption? value) => OnPropertyChanged(nameof(IsNewIteration));

    /// <summary>Открыт редактор семейства: имя по умолчанию — имя файла, заявки — открытые, папки — из библиотеки.</summary>
    public async Task PrepareAsync(LibraryInfo library, ActiveDocument document)
    {
        _library = library;
        if (Name.Length == 0 || Path.GetFileNameWithoutExtension(document.Title) != Name) Name = Path.GetFileNameWithoutExtension(document.Title);
        if (Folder.Length == 0) Folder = library.ReviewFolder;
        Result = "";
        OnPropertyChanged(nameof(YearWarning));
        OnPropertyChanged(nameof(IterationsAvailable));
        if (!IterationsAvailable && Mode == PublishMode.Iteration) Mode = PublishMode.NewFamily;
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

        try
        {
            var folders = await api.FoldersAsync(CancellationToken.None);
            Folders.Clear();
            Folders.Add(library.ReviewFolder);
            foreach (var folder in folders) Folders.Add(folder);
        }
        catch (ToadFlowRequestException)
        {
            // папку можно вписать руками
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

    /// <summary>Итерации выбранного семейства: в завершённую не публикуют.</summary>
    public async Task LoadIterationsAsync()
    {
        if (Existing is not { } existing || _existingCard?.Id == existing.Id) return;
        try
        {
            var card = await api.CardAsync(existing.Id, true, CancellationToken.None);
            if (Existing?.Id != existing.Id) return;
            _existingCard = card;
            Iterations.Clear();
            foreach (var iteration in (card.Iterations ?? []).Where(i => i.RemovedAt is null && !i.IsFinal).OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
                Iterations.Add(new IterationOption(iteration.Id, iteration.Comment is { Length: > 0 } comment ? $"{iteration.Name} — {comment}" : iteration.Name, iteration.Name));
            Iterations.Add(new IterationOption(null, NewIterationLabel, ""));
            Iteration = Iterations[0];
        }
        catch (ToadFlowRequestException exception)
        {
            Result = $"Итерации не загрузились: {exception.Message}";
        }
    }

    /// <summary>Куда ляжет файл: полный путь в библиотеке; null — данных не хватает (причина — в <see cref="Result" />).</summary>
    public string? TargetPath()
    {
        if (_library is null) return null;
        switch (Mode)
        {
            case PublishMode.NewVersion:
                return Existing is null ? null : Path.Combine(_library.Root, _library.ReviewFolder, Existing.Name + ".rfa");
            case PublishMode.Iteration:
            {
                if (Existing is null || Iteration is null) return null;
                var name = Iteration.Id is null ? NewIterationName.Trim() : Iteration.Name;
                if (!IsFolderName(name)) return null;
                return _library.IterationPath(Existing.Name, name);
            }
            default:
            {
                var name = Name.Trim();
                var folder = Folder.Replace('/', '\\').Trim().Trim('\\');
                if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
                if (folder.Split('\\').Any(part => part is ".." or ".")) return null;
                return folder.Length == 0 ? Path.Combine(_library.Root, name + ".rfa") : Path.Combine(_library.Root, folder, name + ".rfa");
            }
        }
    }

    private static bool IsFolderName(string name) =>
        name.Length > 0 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.EndsWith(".", StringComparison.Ordinal);

    private string MissingText() => Mode switch
    {
        PublishMode.NewVersion => "Найдите и выберите семейство, новую версию которого публикуете",
        PublishMode.Iteration when Existing is null => "Найдите и выберите семейство, итерацию которого публикуете",
        PublishMode.Iteration => "Выберите итерацию или впишите название новой — без знаков \\ / : * ? \" < > |",
        _ => "Имя файла — без знаков \\ / : * ? \" < > |, папка — без «..»"
    };

    [RelayCommand]
    private async Task Publish()
    {
        Result = "";
        if (Mode == PublishMode.Iteration && !IterationsAvailable)
        {
            Result = "Итерации появились в ToadFlow 3.4 — этот сервер их ещё не знает";
            return;
        }

        if (TargetPath() is not { } target)
        {
            Result = MissingText();
            return;
        }

        if (Mode == PublishMode.Iteration && IsFinal && FinalComment.Trim().Length == 0)
        {
            Result = "Напишите, что проделано в финальной версии итерации";
            return;
        }

        IsBusy = true;
        try
        {
            if (Mode == PublishMode.NewFamily)
            {
                // Имя занято — семейство в проекте затёрло бы другое: такую публикацию сервер не примет, файл лучше не сохранять вовсе.
                var taken = (await api.ProjectStatusAsync([new ProjectFamilyDto(Name.Trim(), null, null)], true, CancellationToken.None)).FirstOrDefault();
                if (taken?.CardId is not null)
                {
                    Result = $"Имя «{Name.Trim()}» уже занято семейством библиотеки «{taken.CardName}». Выберите «Новая версия существующего» или другое имя";
                    return;
                }
            }

            var newIteration = Mode == PublishMode.Iteration && Iteration!.Id is null;
            if ((Mode == PublishMode.NewFamily || newIteration) && File.Exists(target))
            {
                Result = $"Файл «{target}» уже есть — выберите другое имя или папку";
                return;
            }

            if (YearWarning.Length > 0 && !dialogs.Confirm(YearWarning + ". Всё равно опубликовать?")) return;

            var copy = await revit.PublishCopyAsync(target, overwrite: Mode == PublishMode.NewVersion || (Mode == PublishMode.Iteration && !newIteration), _library!.Root);
            var hash = await hashFile(target);
            var request = new PublishRequest(target, hash, Mode == PublishMode.NewFamily ? null : Existing!.Id, Clean(Comment),
                Mode == PublishMode.NewFamily ? Request?.Id : Mode == PublishMode.NewVersion ? Request?.Id : null,
                Mode == PublishMode.Iteration ? Iteration!.Id : null,
                newIteration ? NewIterationName.Trim() : null,
                newIteration ? Clean(IterationComment) : null,
                Silent,
                Mode == PublishMode.Iteration && IsFinal ? FinalComment.Trim() : null);
            var published = await api.PublishAsync(request, CancellationToken.None);
            Result = $"Опубликовано в «{published.RelativePath}». В каталоге появится после обхода библиотеки"
                     + (published.ScanStarted ? " — он уже начат" : " — он начнётся сразу после текущего")
                     + (target.Contains(_library.ReviewFolder) ? ". Пока семейство на проверке, проектировщики его не видят" : "")
                     + (copy.Redirected ? $". Открытый документ теперь сохранён у вас: «{copy.SourcePath}» — правьте его, а не файл библиотеки" : "");
            Comment = "";
            FinalComment = "";
            IsFinal = false;
            Silent = false;
            if (newIteration)
            {
                NewIterationName = "";
                IterationComment = "";
                _existingCard = null;
            }
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

    private static string? Clean(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
