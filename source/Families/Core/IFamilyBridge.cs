namespace Families.Core;

/// <summary>Документ в активном окне Revit.</summary>
/// <param name="Title">Имя файла без пути: имя модели для учёта загрузок или имя семейства.</param>
/// <param name="IsFamily">Открыт редактор семейства.</param>
/// <param name="PathName">Где документ сохранён; пусто — ещё не сохранялся.</param>
public sealed record ActiveDocument(string Title, bool IsFamily, string PathName);

/// <summary>Загрузить семейство из библиотеки в открытый проект.</summary>
/// <param name="Types">Выбранные типоразмеры; null — все.</param>
/// <param name="Hash">Хеш загружаемого файла — в метку на семействе.</param>
/// <param name="IterationId">Итерация — во вторую метку; null — «Основная».</param>
public sealed record LoadRequest(string Path, IReadOnlyCollection<string>? Types, Guid CardId, string Hash, Guid? IterationId = null);

/// <param name="Replaced">Семейство с таким именем уже было в проекте и заменено.</param>
public sealed record LoadOutcome(IReadOnlyList<string> Types, bool Replaced);

/// <summary>Загружаемое семейство открытого проекта: метка (если есть) и его типоразмеры в проекте.</summary>
public sealed record ProjectFamily(string Name, string Category, Guid? CardId, string? Hash, IReadOnlyList<string> TypeNames, Guid? IterationId = null);

/// <summary>Что получилось при публикации копии.</summary>
/// <param name="SourcePath">Откуда скопирован файл — где теперь открытый документ.</param>
/// <param name="Redirected">
///     Документ был открыт прямо из библиотеки или ещё не сохранялся: он сохранён в локальные черновики и дальше связан с ними, а не с
///     библиотекой.
/// </param>
public sealed record PublishedCopy(string SourcePath, bool Redirected);

/// <summary>Всё, что панели нужно от Revit. Реализация ходит в Revit через внешние события; моделям панели Revit API не нужен.</summary>
public interface IFamilyBridge
{
    /// <summary>Год запущенного Revit: семейство новее не загрузится.</summary>
    int RevitYear { get; }

    /// <summary>Документ в активном окне; null — документа нет.</summary>
    Task<ActiveDocument?> ActiveDocumentAsync();

    /// <summary>Загружает семейство в проект и ставит метку; бросает с понятным текстом, если Revit не смог.</summary>
    Task<LoadOutcome> LoadAsync(LoadRequest request);

    /// <summary>Загружаемые семейства открытого проекта.</summary>
    Task<IReadOnlyList<ProjectFamily>> ProjectFamiliesAsync();

    /// <summary>
    ///     Публикует открытое семейство копией: сохраняет документ на его место и копирует файл (и каталог <c>.txt</c> рядом) в
    ///     <paramref name="path" />. Открытый документ остаётся связан со своим файлом, а не с библиотекой.
    /// </summary>
    /// <param name="overwrite">Файл в библиотеке уже есть, и его можно заменить.</param>
    /// <param name="libraryRoot">Корень библиотеки: документ, открытый прямо оттуда, сначала уходит в черновики.</param>
    Task<PublishedCopy> PublishCopyAsync(string path, bool overwrite, string libraryRoot);
}

/// <summary>Окна и внешние программы: вопрос, сообщение, браузер, проводник.</summary>
public interface IFamilyDialogs
{
    bool Confirm(string text);

    void Info(string text);

    void OpenUrl(string url);

    /// <summary>Проводник с выделенным файлом.</summary>
    void ShowInExplorer(string path);
}
