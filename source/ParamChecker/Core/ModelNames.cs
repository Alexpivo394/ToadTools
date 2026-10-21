namespace ParamChecker.Core;

public static class ModelNames
{
    /// <summary>
    ///     Имя модели по пути на Revit Server или к файлу — без папок и расширения. Имя открытого документа
    ///     не годится: у отсоединённой копии и у локальной копии в нём бывают приписки.
    /// </summary>
    public static string FromPath(string path)
    {
        var name = path.Trim().TrimEnd('/', '\\');
        var slash = name.LastIndexOfAny(['/', '\\']);
        if (slash >= 0) name = name.Substring(slash + 1);
        return name.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
    }

    /// <summary>Путь на Revit Server: и <c>RSN://</c>, и <c>RSN:\\</c> — Revit понимает оба.</summary>
    public static bool IsServerPath(string path) => path.TrimStart().StartsWith("RSN:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     Ключ для сравнения путей: <c>RSN:\\сервер\папка\модель.rvt</c> и <c>RSN://сервер/папка/модель.rvt</c> — одна модель.
    ///     Сравнивать без учёта регистра.
    /// </summary>
    public static string Key(string path)
    {
        var trimmed = path.Trim();
        if (!IsServerPath(trimmed)) return trimmed;
        return "RSN://" + trimmed.Substring(4).TrimStart('/', '\\').Replace('\\', '/');
    }

    /// <summary>Имя сервера из пути <c>RSN://сервер/…</c>; не путь сервера — null.</summary>
    public static string? ServerOf(string path)
    {
        if (!IsServerPath(path)) return null;
        var rest = Key(path).Substring("RSN://".Length);
        var slash = rest.IndexOf('/');
        var server = slash < 0 ? rest : rest.Substring(0, slash);
        return server.Length == 0 ? null : server;
    }

    /// <summary>Имя, годное для файла: недопустимые символы заменяются на «_».</summary>
    public static string ToFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return safe.Length == 0 ? "_" : safe;
    }
}
