namespace Families.Core;

/// <summary>Документ в активном окне Revit.</summary>
/// <param name="Title">Имя файла без пути: имя модели для учёта загрузок или имя семейства.</param>
/// <param name="IsFamily">Открыт редактор семейства.</param>
/// <param name="PathName">Где документ сохранён; пусто — ещё не сохранялся.</param>
public sealed record ActiveDocument(string Title, bool IsFamily, string PathName);

/// <summary>Загрузить семейство из библиотеки в открытый проект.</summary>
/// <param name="Types">Выбранные типоразмеры; null — все.</param>
/// <param name="Hash">Хеш загружаемого файла — в метку на семействе.</param>
public sealed record LoadRequest(string Path, IReadOnlyCollection<string>? Types, Guid CardId, string Hash);

/// <param name="Replaced">Семейство с таким именем уже было в проекте и заменено.</param>
public sealed record LoadOutcome(IReadOnlyList<string> Types, bool Replaced);

/// <summary>Загружаемое семейство открытого проекта: метка (если есть) и его типоразмеры в проекте.</summary>
public sealed record ProjectFamily(string Name, string Category, Guid? CardId, string? Hash, IReadOnlyList<string> TypeNames);

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

    /// <summary>Сохраняет открытое семейство в файл библиотеки; документ дальше связан с этим файлом.</summary>
    Task SaveFamilyAsync(string path, bool overwrite);
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
