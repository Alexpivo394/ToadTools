using System.IO;
using System.Text;
using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Import;
using ClassifierCode.Engine.Kit;
using ClassifierCode.Engine.Profile;
using ClassifierCode.Engine.Running;
using System.Text.Json;

namespace ClassifierCode.Check;

/// <summary>
///     Команды проверялки. Всё пишется в work\ рабочей папки; в консоль — короткая сводка.
///     Коды возврата: 0 — успех, 1 — ошибки профиля, 2 — ошибка входных данных.
/// </summary>
public static class CheckCli
{
    /// <summary>modes.json пишет ИИ-агент или человек: комментарии и лишняя запятая допустимы.</summary>
    private static readonly JsonSerializerOptions ModesJson = new() { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public const string Usage = """
        Проверялка профилей классификатора (тот же движок, что «Заполнить» в Revit).

          check classifier <файл.xlsx> [--sheet имя|номер] [--prefix 04.05] [--code-col N --desc-col N --first-row N]
                таблица кодов заказчика → work\positions.tsv
          check summary <выгрузки…>                  обзор выгрузок → work\summary\*.tsv
          check validate <профиль> [--exports <выгрузки…>] [--classifier work\positions.tsv]
                ошибки профиля, невыгруженные категории, коды не из таблицы заказчика
          check run <профиль> <выгрузки…> [--modes work\modes.json]
                прогон как «Заполнить» без записи → work\run\run.md, codes.tsv, unclassified.tsv
          check explain <профиль> <выгрузка> <Id или текст> [--mode ключ]   как получен код
          check diff <профиль> <выгрузки…> [--filled-by kit\filled-by.classifier.json]
                где код в модели расходится с профилем → work\diff.tsv
          check anomalies <профиль> <выгрузки…>      похожее на ошибки в моделях → work\anomalies.tsv

        Выгрузки можно задавать маской: exports\*.export.json
        """;

    public static int Run(string[] args, TextWriter output, string folder)
    {
        if (args.Length == 0)
        {
            output.WriteLine(Usage);
            return 0;
        }

        try
        {
            if (!Allowed.TryGetValue(args[0], out var allowed)) return Fail(output, $"Неизвестная команда «{args[0]}».\n\n{Usage}");
            var options = Options.Parse(args.Skip(1).ToArray(), allowed);
            return args[0] switch
            {
                "classifier" => Classifier(options, output, folder),
                "summary" => Summary(options, output, folder),
                "validate" => Validate(options, output, folder),
                "run" => RunProfile(options, output, folder),
                "explain" => Explain(options, output, folder),
                "diff" => Diff(options, output, folder),
                "anomalies" => Anomalies(options, output, folder),
                _ => Fail(output, $"Неизвестная команда «{args[0]}».\n\n{Usage}")
            };
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ProfileReadException or JsonException or UnauthorizedAccessException)
        {
            return Fail(output, exception.Message);
        }
        catch (Exception exception)
        {
            // Агенту нужна причина, а не трасса стека.
            return Fail(output, $"{exception.Message} ({exception.GetType().Name})");
        }
    }

    public static readonly IReadOnlyList<string> Commands = ["classifier", "summary", "validate", "run", "explain", "diff", "anomalies"];

    private static int Fail(TextWriter output, string message)
    {
        output.WriteLine("Ошибка: " + message);
        return 2;
    }

    /// <summary>Опции каждой команды; остальные — ошибка, а не молча проигнорированная опечатка.</summary>
    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        ["classifier"] = ["sheet", "prefix", "code-col", "desc-col", "first-row"],
        ["summary"] = [],
        ["validate"] = ["exports", "classifier"],
        ["run"] = ["modes"],
        ["explain"] = ["mode", "modes"],
        ["diff"] = ["filled-by", "modes"],
        ["anomalies"] = ["modes"]
    };

    private sealed class Options
    {
        /// <summary>Опция со списком значений: всё до следующей опции, повторы складываются.</summary>
        private const string Many = "exports";

        public List<string> Positional { get; } = [];
        public Dictionary<string, List<string>> Named { get; } = new();

        /// <summary>«--имя значение», «--имя=значение»; у --exports — несколько значений.</summary>
        public static Options Parse(string[] args, IReadOnlyCollection<string> allowed)
        {
            var options = new Options();
            string? current = null;
            foreach (var arg in args)
            {
                if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    var name = arg.Substring(2);
                    string? value = null;
                    var equals = name.IndexOf('=');
                    if (equals >= 0)
                    {
                        value = name.Substring(equals + 1);
                        name = name.Substring(0, equals);
                    }

                    if (!allowed.Contains(name))
                        throw new InvalidDataException($"Неизвестная опция «--{name}». У этой команды: " +
                                                       (allowed.Count == 0 ? "опций нет" : string.Join(", ", allowed.Select(a => "--" + a))));
                    if (!options.Named.ContainsKey(name)) options.Named[name] = [];
                    if (value != null)
                    {
                        options.Named[name].Add(value);
                        current = null;
                    }
                    else current = name;
                }
                else if (current == null) options.Positional.Add(arg);
                else
                {
                    options.Named[current].Add(arg);
                    if (current != Many) current = null;
                }
            }

            return options;
        }

        public string? One(string name) => Named.TryGetValue(name, out var values) && values.Count > 0 ? values[values.Count - 1] : null;

        public int? Int(string name)
        {
            var text = One(name);
            if (text == null) return null;
            return int.TryParse(text, out var value) && value > 0
                ? value
                : throw new InvalidDataException($"--{name}: нужно число от 1 (номер колонки или строки), а не «{text}»");
        }
    }

    private static string Full(string folder, string path) => Path.IsPathRooted(path) ? path : Path.Combine(folder, path);

    /// <summary>Пути и маски выгрузок → файлы.</summary>
    private static List<string> Files(string folder, IEnumerable<string> patterns)
    {
        var files = new List<string>();
        foreach (var pattern in patterns)
        {
            var full = Full(folder, pattern);
            var name = Path.GetFileName(full);
            if (name.Contains('*') || name.Contains('?'))
            {
                var directory = Path.GetDirectoryName(full)!;
                // По маске берутся только выгрузки: «exports\*» не должен зацепить журнал выгрузки.
                if (Directory.Exists(directory))
                    files.AddRange(Directory.GetFiles(directory, name).Where(f => f.EndsWith(ExportNames.Extension, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
            }
            else if (File.Exists(full)) files.Add(full);
            else throw new IOException("Нет файла " + pattern);
        }

        if (files.Count == 0) throw new IOException("Не нашлось ни одной выгрузки по " + string.Join(" ", patterns));
        return files;
    }

    /// <summary>Выгрузки читаются по одной, по мере прохода: вместе в памяти их не держим.</summary>
    private static IEnumerable<ExportModel> Exports(string folder, IEnumerable<string> patterns) => Files(folder, patterns).Select(ExportReader.Read);

    private static ClassifierProfile LoadProfile(string folder, string path)
    {
        var full = Full(folder, path);
        if (!File.Exists(full)) throw new IOException("Нет файла профиля " + path);
        return ProfileSerializer.Read(File.ReadAllText(full));
    }

    /// <summary>Профиль без ошибок или null (ошибки напечатаны).</summary>
    private static CompiledProfile? Compiled(ClassifierProfile profile, TextWriter output)
    {
        var (compiled, issues) = ProfileCompiler.Compile(profile, null);
        if (compiled != null) return compiled;
        output.WriteLine($"В профиле ошибок: {issues.Count}");
        foreach (var issue in issues.Take(50)) output.WriteLine("  " + issue);
        return null;
    }

    /// <summary>work\modes.json: модель → режим. Режим, которого нет в профиле, не применяется — об этом пишется.</summary>
    private static IReadOnlyDictionary<string, string>? Modes(string folder, Options options, CompiledProfile profile, TextWriter output)
    {
        var path = Full(folder, options.One("modes") ?? Path.Combine("work", "modes.json"));
        if (!File.Exists(path)) return null;
        var modes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), ModesJson) ?? new Dictionary<string, string>();
        foreach (var pair in modes.Where(p => !profile.Modes.ContainsKey(p.Value)))
            output.WriteLine($"Внимание: в {Path.GetFileName(path)} для «{pair.Key}» режим «{pair.Value}» — его нет в профиле (есть: {string.Join(", ", profile.Modes.Keys)})");
        return modes;
    }

    private static string Work(string folder, string name)
    {
        var path = Path.Combine(folder, "work", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static int Classifier(Options options, TextWriter output, string folder)
    {
        var file = options.Positional.FirstOrDefault() ?? throw new IOException("Укажите файл Excel");
        // Значения опций проверяются до чтения книги: опечатка видна сразу.
        var codeColumn = options.Int("code-col");
        var descriptionColumn = options.Int("desc-col");
        var firstRow = options.Int("first-row");
        var (rows, sheet, sheets) = XlsxRows.Read(Full(folder, file), options.One("sheet"));
        var (code, description, first) = PositionsImport.Guess(rows);
        code = codeColumn - 1 ?? code;
        description = descriptionColumn - 1 ?? description;
        first = firstRow - 1 ?? first;

        var header = first > 0 ? rows[first - 1] : [];
        var extra = new Dictionary<string, int>();
        for (var c = 0; c < header.Length; c++)
            if (c != code && c != description && header[c].Length > 0 && !extra.ContainsKey(header[c]))
                extra[header[c]] = c;

        var (positions, skipped) = PositionsImport.FromRows(rows, code, description, first, extra);
        var prefix = options.One("prefix");
        if (prefix != null) positions = positions.Where(p => p.Code.StartsWith(prefix, StringComparison.Ordinal)).ToList();

        TsvWriter.Write(Work(folder, "positions.tsv"), new[] { "код", "описание" }.Concat(extra.Keys),
            positions.Select(p => new object?[] { p.Code, p.Description }.Concat(extra.Keys.Select(k => (object?)p.Extra[k]))));
        output.WriteLine($"Лист «{sheet}» (листы: {string.Join(", ", sheets)}); колонки: код — {code + 1}, описание — {description + 1}, данные со строки {first + 1}.");
        output.WriteLine($"позиций: {positions.Count}{(prefix != null ? $" (код начинается с {prefix})" : string.Empty)}, пропущено строк: {skipped.Count}.");
        foreach (var top in positions.Where(p => p.Code.Count(ch => ch == '.') <= 3).Take(40)) output.WriteLine($"  {top.Code} {top.Description}");
        output.WriteLine("→ work\\positions.tsv");
        return 0;
    }

    private static int Summary(Options options, TextWriter output, string folder)
    {
        var summary = SummaryBuilder.Build(Exports(folder, options.Positional));
        summary.Write(Path.Combine(folder, "work", "summary"));
        output.Write(summary.Text());
        output.WriteLine("→ work\\summary\\ (groups.tsv, systems.tsv, worksets.tsv, parameters.tsv, values.tsv, models.tsv, current-codes.tsv)");
        return 0;
    }

    private static int Validate(Options options, TextWriter output, string folder)
    {
        var profile = LoadProfile(folder, options.Positional.FirstOrDefault() ?? throw new IOException("Укажите профиль"));
        // Для проверки категорий хватает шапки — элементы не разбираются.
        var exports = options.Named.TryGetValue("exports", out var patterns) ? Files(folder, patterns).Select(ExportReader.ReadHeader).ToList() : null;
        List<Position>? classifier = null;
        if (options.One("classifier") is { } table)
        {
            var lines = File.ReadAllLines(Full(folder, table)).Skip(1);
            classifier = lines.Select(l => l.Split('\t')).Where(c => c.Length >= 2).Select(c => new Position { Code = c[0], Description = c[1] }).ToList();
        }

        var issues = KitValidator.Validate(profile, exports, classifier);
        if (issues.Count == 0)
        {
            output.WriteLine("Профиль без ошибок.");
            return 0;
        }

        output.WriteLine($"Ошибок: {issues.Count}");
        foreach (var issue in issues.Take(100)) output.WriteLine("  " + issue);
        return 1;
    }

    private static int RunProfile(Options options, TextWriter output, string folder)
    {
        var compiled = Compiled(LoadProfile(folder, options.Positional.FirstOrDefault() ?? throw new IOException("Укажите профиль")), output);
        if (compiled == null) return 1;
        var result = RunReport.Build(compiled, Exports(folder, options.Positional.Skip(1)), Modes(folder, options, compiled, output));
        result.WriteMarkdown(Work(folder, Path.Combine("run", "run.md")));
        result.WriteTsv(Path.Combine(folder, "work", "run"));
        output.Write(result.Text());
        output.WriteLine("→ work\\run\\run.md, codes.tsv, unclassified.tsv");
        return 0;
    }

    private static int Explain(Options options, TextWriter output, string folder)
    {
        if (options.Positional.Count < 3) throw new IOException("Нужно: explain <профиль> <выгрузка> <Id или текст>");
        var compiled = Compiled(LoadProfile(folder, options.Positional[0]), output);
        if (compiled == null) return 1;
        var model = ExportReader.Read(Files(folder, [options.Positional[1]]).Single());
        var mode = options.One("mode") ?? ModeResolver.Resolve(compiled, model.Model, Modes(folder, options, compiled, output));
        if (mode == null || !compiled.Modes.ContainsKey(mode)) throw new InvalidDataException("Раздел модели не определён — укажите --mode");

        var query = string.Join(" ", options.Positional.Skip(2));
        var facts = ExportFacts.Load(model);
        var targets = long.TryParse(query, out var id)
            ? facts.Where(f => f.Id == id).ToList()
            : facts.Where(f => KitElements.Label(f).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).Take(5).ToList();
        if (targets.Count == 0) throw new InvalidDataException("Элемент не найден: " + query);

        var engine = new ClassificationEngine(compiled, mode);
        foreach (var f in targets)
        {
            var r = engine.Classify(f);
            output.WriteLine($"#{f.Id} {f.Category} — {KitElements.Label(f)}");
            output.WriteLine($"  системы: {f.SystemText.Trim(' ', '|')}; рабочий набор: {f.Workset ?? "—"}; сеть: {ExportFacts.DomainKey(f.Domain)}");
            output.WriteLine("  итог: " + (r.Position != null ? r.Position.Code + " " + r.Position.Description : r.Status + ": " + r.Reason));
            foreach (var step in r.Trace) output.WriteLine($"  {step.Stage}: {step.Text}");
        }

        return 0;
    }

    private static int Diff(Options options, TextWriter output, string folder)
    {
        var compiled = Compiled(LoadProfile(folder, options.Positional.FirstOrDefault() ?? throw new IOException("Укажите профиль")), output);
        if (compiled == null) return 1;
        CompiledProfile? filledBy = null;
        var filledPath = options.One("filled-by") ?? Path.Combine("kit", "filled-by.classifier.json");
        if (File.Exists(Full(folder, filledPath))) filledBy = ProfileCompiler.Compile(LoadProfile(folder, filledPath), null).Profile;

        var modes = Modes(folder, options, compiled, output);
        Undetected(compiled, folder, options.Positional.Skip(1), modes, output);
        var diffs = DiffFinder.Find(compiled, filledBy, Exports(folder, options.Positional.Skip(1)), modes);
        DiffFinder.Write(Work(folder, "diff.tsv"), diffs);
        output.WriteLine($"Расхождений с кодами в моделях: {diffs.Count}, из них похоже на ручные правки: {diffs.Count(d => d.IsManual)}" +
                         (filledBy == null ? " (профиля, которым заполняли, нет — ручные правки не определить)" : string.Empty));
        foreach (var group in diffs.GroupBy(d => (d.Label, d.Current, d.Profile)).OrderByDescending(g => g.Count()).Take(20))
            output.WriteLine($"  {group.Count()} — {group.Key.Label}: в модели {group.Key.Current}, по профилю {group.Key.Profile}");
        output.WriteLine("→ work\\diff.tsv");
        return 0;
    }

    /// <summary>Модели, у которых раздел не определился, в diff и anomalies не участвуют — об этом надо сказать.</summary>
    private static void Undetected(CompiledProfile profile, string folder, IEnumerable<string> patterns, IReadOnlyDictionary<string, string>? modes, TextWriter output)
    {
        foreach (var header in Files(folder, patterns).Select(ExportReader.ReadHeader))
            if (ModeResolver.Resolve(profile, header.Model, modes) == null)
                output.WriteLine($"Модель «{header.Model}»: раздел не определён — не проверялась; задайте раздел в work\\modes.json");
    }

    private static int Anomalies(Options options, TextWriter output, string folder)
    {
        var compiled = Compiled(LoadProfile(folder, options.Positional.FirstOrDefault() ?? throw new IOException("Укажите профиль")), output);
        if (compiled == null) return 1;
        var modes = Modes(folder, options, compiled, output);
        Undetected(compiled, folder, options.Positional.Skip(1), modes, output);
        var anomalies = AnomalyFinder.Find(compiled, Exports(folder, options.Positional.Skip(1)), modes);
        AnomalyFinder.Write(Work(folder, "anomalies.tsv"), anomalies);
        output.WriteLine($"Похоже на ошибки в моделях: {anomalies.Count}");
        foreach (var group in anomalies.GroupBy(a => (a.Model, a.Label, a.Where, a.Expected)).OrderByDescending(g => g.Count()).Take(30))
            output.WriteLine($"  {group.Key.Model}: {group.Count()} × {group.Key.Label} — {group.Key.Where}, у остальных {group.Key.Expected}");
        output.WriteLine("→ work\\anomalies.tsv");
        return 0;
    }
}
