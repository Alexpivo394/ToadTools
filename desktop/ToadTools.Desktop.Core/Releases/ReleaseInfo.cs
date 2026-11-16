namespace ToadTools.Desktop.Core.Releases;

/// <summary>Файл релиза GitHub.</summary>
public sealed record ReleaseAsset(string Name, Uri DownloadUrl, long Size);

/// <summary>Релиз GitHub: тег (версия плагина), описание (что нового) и файлы.</summary>
public sealed record ReleaseInfo(string Tag, string Notes, IReadOnlyList<ReleaseAsset> Assets);

public enum ReleaseCheckStatus
{
    /// <summary>Пришёл релиз (новый или первый раз).</summary>
    Updated,

    /// <summary>С прошлой проверки ничего не поменялось (304 по ETag).</summary>
    NotModified,

    /// <summary>Сеть, лимит GitHub, ответ не разобрался — <see cref="ReleaseCheck.Error" />.</summary>
    Failed
}

/// <summary>Результат одной проверки GitHub.</summary>
public sealed record ReleaseCheck(ReleaseCheckStatus Status, ReleaseInfo? Release, string? ETag, string? Error);
