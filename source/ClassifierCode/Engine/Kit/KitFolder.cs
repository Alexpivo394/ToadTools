using System.IO;
using System.Text;
using ClassifierCode.Engine.Library;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Engine.Kit;

/// <summary>
///     Рабочая папка объекта для ИИ-агента. Набор (инструкция, формат, проверялка, профиль-основа) раскладывается заново
///     при каждой выгрузке; то, что сделали агент и координатор (work\, profile.classifier.json, REVIEW.md), не трогается.
/// </summary>
public static class KitFolder
{
    public const string Exports = "exports";
    public const string Kit = "kit";
    public const string Log = "_журнал.txt";

    private static readonly string[] Required = ["AGENTS.md", "FORMAT.md", "profile.schema.json", "README_КООРДИНАТОРУ.txt", Path.Combine("check", "check.exe")];

    /// <summary>Набор в установке: рядом со сборкой плагина, ClassifierCode\AiKit.</summary>
    public static string InstalledKit(string assemblyFolder) => Path.Combine(assemblyFolder, "ClassifierCode", "AiKit");

    /// <summary>Документы\ToadTools\Классификатор\&lt;объект&gt;.</summary>
    public static string DefaultFolder(string objectName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(objectName.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        if (safe.Length == 0) safe = "Объект";
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ToadTools", "Классификатор", safe);
    }

    public static void Prepare(string folder, string kitSource, ClassifierProfile baseProfile, ClassifierProfile? filledBy)
    {
        var missing = Required.Where(f => !File.Exists(Path.Combine(kitSource, f))).ToList();
        if (missing.Count > 0)
            throw new IOException($"Набор для ИИ не найден или неполон ({string.Join(", ", missing)}) в {kitSource}. Переустановите ToadTools.");

        Directory.CreateDirectory(folder);
        var kit = Path.Combine(folder, Kit);
        Directory.CreateDirectory(kit);
        Directory.CreateDirectory(Path.Combine(folder, Exports));

        var agents = Path.Combine(kitSource, "AGENTS.md");
        File.Copy(agents, Path.Combine(folder, "AGENTS.md"), true);
        WriteClaudeMd(Path.Combine(folder, "CLAUDE.md"));
        File.Copy(Path.Combine(kitSource, "README_КООРДИНАТОРУ.txt"), Path.Combine(folder, "README_КООРДИНАТОРУ.txt"), true);
        File.Copy(Path.Combine(kitSource, "FORMAT.md"), Path.Combine(kit, "FORMAT.md"), true);
        File.Copy(Path.Combine(kitSource, "profile.schema.json"), Path.Combine(kit, "profile.schema.json"), true);

        var check = Path.Combine(kit, "check");
        if (Directory.Exists(check)) Directory.Delete(check, true);
        CopyDirectory(Path.Combine(kitSource, "check"), check);

        Write(Path.Combine(kit, "base.classifier.json"), baseProfile);
        if (filledBy != null) Write(Path.Combine(kit, "filled-by.classifier.json"), filledBy);
    }

    /// <summary>Профиль, которым заполнялась модель: тот же Id и та же версия. Другая версия — не он: её отличия сошли бы за ручные правки.</summary>
    public static ClassifierProfile? FindFilledBy(IEnumerable<ClassifierProfile> library, ModelBinding binding) =>
        library.FirstOrDefault(p => p.Id == binding.Id && p.Version == binding.Version);

    private const string ClaudeImport = "@AGENTS.md";

    /// <summary>
    ///     CLAUDE.md подключает AGENTS.md (Claude Code читает импорт «@файл»), поэтому обновлять его не нужно, а заметки,
    ///     которые туда допишут агент или координатор, переживают повторную выгрузку.
    /// </summary>
    private static void WriteClaudeMd(string path)
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, "# Задание — в AGENTS.md\n\n" + ClaudeImport + "\n", new UTF8Encoding(false));
            return;
        }

        var text = File.ReadAllText(path);
        if (!text.Contains(ClaudeImport)) File.WriteAllText(path, ClaudeImport + "\n\n" + text, new UTF8Encoding(false));
    }

    /// <summary>Каким профилем заполнялось большинство моделей объекта (по профилю и версии).</summary>
    public static ModelBinding? CommonBinding(IEnumerable<ModelBinding?> bindings) =>
        bindings.Where(b => b != null).GroupBy(b => (b!.Id, b.Version)).OrderByDescending(g => g.Count()).Select(g => g.First()).FirstOrDefault();

    public static void WriteFilledBy(string folder, ClassifierProfile profile) => Write(Path.Combine(folder, Kit, "filled-by.classifier.json"), profile);

    private static void Write(string path, ClassifierProfile profile) =>
        File.WriteAllText(path, ProfileSerializer.Write(profile), new UTF8Encoding(false));

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(from)) CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
