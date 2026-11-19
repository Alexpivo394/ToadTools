using System.IO;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Engine.Library;

public enum ProfileOrigin
{
    Builtin,
    Personal,
    Shared
}

/// <summary>Профиль в библиотеке. Profile == null — файл не прочитался, причина в Error.</summary>
public sealed record ProfileEntry(ClassifierProfile? Profile, string? Path, ProfileOrigin Origin, bool ReadOnly, string? Error)
{
    /// <summary>Время файла, когда профиль прочитан или записан, — чтобы не затереть чужую правку в общей папке.</summary>
    public DateTime? FileTimeUtc { get; init; }

    public string Title => Profile != null
        ? Profile.Name + " " + Profile.Version + Origin switch
        {
            ProfileOrigin.Builtin => " — встроенный",
            ProfileOrigin.Shared when ReadOnly => " — общий, только чтение",
            ProfileOrigin.Shared => " — общий",
            _ => string.Empty
        }
        : System.IO.Path.GetFileName(Path) + " — не читается";
}

/// <summary>Профиль нельзя записать: встроенный, папка только для чтения или файл из более новой версии.</summary>
public sealed class ProfileSaveException(string message) : Exception(message);

/// <summary>
///     Профили: встроенные (из сборки), личная папка и общие папки (например, сетевая папка отдела).
///     Перед перезаписью файла кладёт копию .bak рядом.
/// </summary>
public sealed class ProfileLibrary(
    string personalFolder,
    IEnumerable<string> sharedFolders,
    Func<IReadOnlyList<ClassifierProfile>> builtins,
    Func<string, bool>? canWrite = null)
{
    public const string Extension = ".classifier.json";

    private readonly List<string> _sharedFolders = sharedFolders.ToList();
    private readonly Func<string, bool> _canWrite = canWrite ?? CanWriteFolder;

    public string PersonalFolder => personalFolder;

    /// <summary>Личная папка по умолчанию: %AppData%\ToadTools\ClassifierCode\Profiles.</summary>
    public static string DefaultPersonalFolder =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToadTools", "ClassifierCode", "Profiles");

    public IReadOnlyList<ProfileEntry> List()
    {
        var entries = builtins().Select(p => new ProfileEntry(p, null, ProfileOrigin.Builtin, true, null)).ToList();
        entries.AddRange(Folder(personalFolder, ProfileOrigin.Personal));
        foreach (var folder in _sharedFolders.Where(f => !SamePath(f, personalFolder)))
            entries.AddRange(Folder(folder, ProfileOrigin.Shared));
        return entries;
    }

    private IEnumerable<ProfileEntry> Folder(string folder, ProfileOrigin origin)
    {
        string[] files;
        try
        {
            files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*" + Extension) : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [new ProfileEntry(null, folder, origin, true, "Папка не открывается: " + exception.Message)];
        }

        var readOnly = origin == ProfileOrigin.Shared && !_canWrite(folder);
        return files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Select(file => Read(file, origin, readOnly)).ToList();
    }

    private static ProfileEntry Read(string file, ProfileOrigin origin, bool readOnly)
    {
        try
        {
            return new ProfileEntry(ProfileSerializer.Read(File.ReadAllText(file)), file, origin, readOnly, null) { FileTimeUtc = File.GetLastWriteTimeUtc(file) };
        }
        catch (ProfileReadException exception)
        {
            return new ProfileEntry(null, file, origin, true, exception.Message);
        }
        catch (Exception exception)
        {
            // Один испорченный файл в общей папке не должен ронять окно у всех — он просто показывается с ошибкой.
            return new ProfileEntry(null, file, origin, true, "Не удалось прочитать профиль: " + exception.Message);
        }
    }

    /// <summary>Записывает профиль. path == null — новый файл в личной папке по названию профиля.</summary>
    /// <param name="expectedFileTimeUtc">Время файла при открытии: если файл с тех пор изменён (коллегой), запись отклоняется.</param>
    public ProfileEntry Save(ClassifierProfile profile, string? path, DateTime? expectedFileTimeUtc = null)
    {
        if (path == null && builtins().Any(b => b.Id == profile.Id))
            throw new ProfileSaveException("Встроенный профиль не меняется — сохраните копию.");

        path ??= NewPath(personalFolder, profile);
        var folder = System.IO.Path.GetDirectoryName(path)!;
        var origin = SamePath(folder, personalFolder) ? ProfileOrigin.Personal : ProfileOrigin.Shared;
        if (origin == ProfileOrigin.Shared && !_canWrite(folder))
            throw new ProfileSaveException($"В папку «{folder}» нельзя записывать — сохраните копию в личную папку.");

        if (File.Exists(path))
        {
            if (expectedFileTimeUtc != null && File.GetLastWriteTimeUtc(path) != expectedFileTimeUtc.Value)
                throw new ProfileSaveException("Файл профиля изменён после того, как вы его открыли (возможно, коллегой). Сохраните свою версию копией или закройте и откройте профиль заново.");

            try
            {
                ProfileSerializer.Read(File.ReadAllText(path));
            }
            catch (ProfileReadException exception) when (exception.Message.Contains("новее"))
            {
                throw new ProfileSaveException("Файл сделан в более новой версии плагина и не будет перезаписан: " + path);
            }
            catch (ProfileReadException)
            {
                // Битый файл можно заменить — копия останется в .bak.
            }

            File.Copy(path, path + ".bak", true);
        }

        Directory.CreateDirectory(folder);
        File.WriteAllText(path, ProfileSerializer.Write(profile));
        return new ProfileEntry(profile, path, origin, false, null) { FileTimeUtc = File.GetLastWriteTimeUtc(path) };
    }

    public ClassifierProfile Duplicate(ClassifierProfile source)
    {
        var copy = ProfileSerializer.Clone(source);
        copy.Id = Guid.NewGuid();
        copy.Name = source.Name + " (копия)";
        return copy;
    }

    /// <summary>Копирует профиль из файла в личную папку.</summary>
    /// <param name="replaceExisting">
    ///     Такой профиль уже есть в личной папке: true — заменить его, false — импортировать отдельной копией.
    ///     null — заменять без вопроса.
    /// </param>
    public ProfileEntry Import(string file, Func<ProfileEntry, bool>? replaceExisting = null)
    {
        var profile = ProfileSerializer.Read(File.ReadAllText(file));
        if (builtins().Any(b => b.Id == profile.Id))
        {
            profile.Id = Guid.NewGuid();
            return Save(profile, null);
        }

        var existing = List().FirstOrDefault(e => e.Profile?.Id == profile.Id && e.Origin == ProfileOrigin.Personal);
        if (existing != null && replaceExisting != null && !replaceExisting(existing))
        {
            profile.Id = Guid.NewGuid();
            return Save(profile, null);
        }

        return Save(profile, existing?.Path);
    }

    public void Export(ClassifierProfile profile, string file) => File.WriteAllText(file, ProfileSerializer.Write(profile));

    /// <summary>Имя файла по названию; если файл с таким именем занят другим профилем — с номером.</summary>
    private static string NewPath(string folder, ClassifierProfile profile)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var name = new string(profile.Name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (name.Length == 0) name = "Профиль";
        var path = System.IO.Path.Combine(folder, name + Extension);
        for (var n = 2; File.Exists(path) && !IsSameProfile(path, profile.Id); n++)
            path = System.IO.Path.Combine(folder, $"{name} ({n}){Extension}");
        return path;
    }

    private static bool IsSameProfile(string path, Guid id)
    {
        try
        {
            return ProfileSerializer.Read(File.ReadAllText(path)).Id == id;
        }
        catch (Exception exception) when (exception is ProfileReadException or IOException)
        {
            return false;
        }
    }

    private static bool CanWriteFolder(string folder)
    {
        try
        {
            var probe = System.IO.Path.Combine(folder, ".toadtools-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(System.IO.Path.GetFullPath(a).TrimEnd('\\', '/'), System.IO.Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
