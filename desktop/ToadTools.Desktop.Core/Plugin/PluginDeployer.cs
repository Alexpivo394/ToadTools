using System.Diagnostics;

namespace ToadTools.Desktop.Core.Plugin;

/// <summary>Чем кончилась установка: в какие годы плагин встал, в каких файлы заняты (там открыт Revit).</summary>
public sealed record DeployResult(IReadOnlyList<int> Deployed, IReadOnlyList<int> Locked);

/// <summary>
///     Установка плагина из staging в Addins\{год}. Новая версия копируется рядом (ToadTools.new), потом две
///     мгновенные операции: ToadTools → ToadTools.old, ToadTools.new → ToadTools. Если файлы держит Revit, первое
///     переименование не проходит, и всё остаётся как было. Revit смотрит только на *.addin, лишние папки ему не мешают.
/// </summary>
public sealed class PluginDeployer(AppPaths paths)
{
    private const string Current = PluginArchive.PluginFolder;
    private const string New = Current + ".new";
    private const string Old = Current + ".old";
    private const string Addin = PluginArchive.AddinFile;
    private const string AddinTemp = Addin + ".tmp";

    public DeployResult Deploy(string stagedDir, IReadOnlyCollection<int> years)
    {
        var available = PluginArchive.Years(stagedDir);
        var deployed = new List<int>();
        var locked = new List<int>();

        foreach (var year in years.Where(available.Contains).OrderBy(year => year))
        {
            if (DeployYear(Path.Combine(stagedDir, year.ToString()), paths.AddinsYear(year))) deployed.Add(year);
            else locked.Add(year);
        }

        return new DeployResult(deployed, locked);
    }

    private static bool DeployYear(string source, string addins)
    {
        Directory.CreateDirectory(addins);
        var current = Path.Combine(addins, Current);
        var fresh = Path.Combine(addins, New);
        var old = Path.Combine(addins, Old);

        TryDelete(fresh);
        TryDelete(old);
        if (Directory.Exists(old)) return false; // старая копия ещё держится — тот же Revit

        CopyDirectory(Path.Combine(source, Current), fresh);

        var hadCurrent = Directory.Exists(current);
        if (hadCurrent)
        {
            try
            {
                Directory.Move(current, old);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Info($"{addins}: файлы плагина заняты ({exception.Message}), установка после закрытия Revit");
                TryDelete(fresh);
                return false;
            }
        }

        try
        {
            Directory.Move(fresh, current);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error($"{addins}: не удалось поставить новую папку плагина", exception);
            if (hadCurrent && !Directory.Exists(current)) Directory.Move(old, current);
            TryDelete(fresh);
            return false;
        }

        var addinTemp = Path.Combine(addins, AddinTemp);
        var addin = Path.Combine(addins, Addin);
        File.Copy(Path.Combine(source, Addin), addinTemp, true);
        if (File.Exists(addin)) File.Replace(addinTemp, addin, null);
        else File.Move(addinTemp, addin);

        TryDelete(old);
        return true;
    }

    /// <summary>Доводит до целого состояния установку, прерванную выключением компьютера или падением программы.</summary>
    public void Recover()
    {
        if (!Directory.Exists(paths.AddinsRoot)) return;

        foreach (var addins in Directory.GetDirectories(paths.AddinsRoot))
        {
            var current = Path.Combine(addins, Current);
            var old = Path.Combine(addins, Old);
            try
            {
                if (Directory.Exists(old) && !Directory.Exists(current))
                {
                    Directory.Move(old, current);
                    Log.Info($"{addins}: прерванная установка откатилась на прежнюю версию");
                }

                TryDelete(old);
                TryDelete(Path.Combine(addins, New));
                var addinTemp = Path.Combine(addins, AddinTemp);
                if (File.Exists(addinTemp)) File.Delete(addinTemp);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Error($"{addins}: не удалось довести прерванную установку", exception);
            }
        }
    }

    /// <summary>Убирает плагин из года (выключен в настройках или программа удаляется); чужие файлы не трогает.</summary>
    public void Remove(int year)
    {
        var addins = paths.AddinsYear(year);
        var addin = Path.Combine(addins, Addin);
        if (File.Exists(addin)) File.Delete(addin);
        foreach (var name in new[] { Current, New, Old })
        {
            var folder = Path.Combine(addins, name);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    /// <summary>Плагин стоит в этом году: есть ToadTools.addin.</summary>
    public bool IsDeployed(int year) => File.Exists(Path.Combine(paths.AddinsYear(year), Addin));

    /// <summary>Версия файла ToadTools\ToadTools.dll (для установок старым MSI, о которых программа ещё не знает).</summary>
    public Version? DeployedVersion(int year)
    {
        var dll = Path.Combine(paths.AddinsYear(year), Current, "ToadTools.dll");
        if (!File.Exists(dll)) return null;
        var info = FileVersionInfo.GetVersionInfo(dll);
        return info.FileMajorPart == 0 && info.FileMinorPart == 0 && info.FileBuildPart == 0
            ? null
            : new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, file.Substring(source.Length + 1));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Info($"Не удалось удалить {folder}: {exception.Message}");
        }
    }
}
