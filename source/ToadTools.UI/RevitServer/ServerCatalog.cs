using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ToadTools.UI.RevitServer;

/// <summary>
///     Содержимое Revit Server, обойдённое целиком и запомненное на весь сеанс Revit. Сервер отвечает медленно,
///     поэтому каталог один раз обходит все папки в фоне (несколько запросов параллельно), а дальше отдаёт папки
///     мгновенно. Копия каталога хранится на диске: после перезапуска Revit дерево показывается сразу,
///     а свежий обход идёт в фоне.
/// </summary>
public sealed class ServerCatalog : ITreeSource
{
    private const int Parallelism = 4;

    private static readonly ConcurrentDictionary<string, ServerCatalog> Instances = new(StringComparer.OrdinalIgnoreCase);

    private readonly ITreeSource _source;
    private readonly string? _cacheFile;
    private readonly object _sync = new();
    private Dictionary<string, IReadOnlyList<SourceEntry>> _folders = new(StringComparer.OrdinalIgnoreCase);
    private Task? _refresh;

    /// <param name="source">Откуда читать папки.</param>
    /// <param name="cacheFile">Файл копии каталога на диске; null — без копии.</param>
    public ServerCatalog(ITreeSource source, string? cacheFile)
    {
        _source = source;
        _cacheFile = cacheFile;
        LoadCache();
    }

    /// <summary>Общий на сеанс Revit каталог сервера.</summary>
    public static ServerCatalog For(string server, string year) =>
        Instances.GetOrAdd($"{server}|{year}", _ => new ServerCatalog(new RevitServerSource(server, year), CacheFile(server, year)));

    public string RootPath => _source.RootPath;

    /// <summary>Когда каталог последний раз обойдён целиком; null — ещё ни разу.</summary>
    public DateTime? UpdatedAt { get; private set; }

    /// <summary>Каталог обойдён в этом сеансе Revit, а не только прочитан с диска.</summary>
    public bool IsFresh { get; private set; }

    public bool IsRefreshing => _refresh is { IsCompleted: false };

    /// <summary>Ошибка последнего обхода: часть папок не прочиталась, для них остались прежние данные.</summary>
    public string? LastError { get; private set; }

    /// <summary>Сколько папок прочитано в текущем обходе.</summary>
    public int FoldersRead { get; private set; }

    /// <summary>Каталог изменился: прочитана папка или закончен обход. Вызывается не в потоке окна.</summary>
    public event EventHandler? Changed;

    /// <summary>Папка из каталога, если она уже прочитана.</summary>
    public IReadOnlyList<SourceEntry>? TryGetChildren(string folderPath)
    {
        lock (_sync) return _folders.TryGetValue(folderPath, out var entries) ? entries : null;
    }

    public async Task<IReadOnlyList<SourceEntry>> GetChildrenAsync(string folderPath, CancellationToken ct)
    {
        if (TryGetChildren(folderPath) is { } cached) return cached;

        var entries = await _source.GetChildrenAsync(folderPath, ct).ConfigureAwait(false);
        lock (_sync) _folders[folderPath] = entries;
        return entries;
    }

    /// <summary>Все модели, известные каталогу, — для поиска по всему серверу.</summary>
    public IReadOnlyList<SourceEntry> AllModels()
    {
        lock (_sync)
        {
            return _folders.Values.SelectMany(entries => entries).Where(entry => !entry.IsFolder)
                .OrderBy(entry => entry.ModelPath, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    /// <summary>Обходит сервер, если в этом сеансе ещё не обходил. Идущий обход не перезапускает.</summary>
    public Task EnsureFreshAsync() => IsFresh ? Task.CompletedTask : RefreshAsync();

    /// <summary>Заново обходит весь сервер. Пока обход идёт, повторный вызов возвращает его же.</summary>
    public Task RefreshAsync()
    {
        lock (_sync)
        {
            if (_refresh is { IsCompleted: false }) return _refresh;
            _refresh = Task.Run(CrawlAsync);
            return _refresh;
        }
    }

    private async Task CrawlAsync()
    {
        var fresh = new ConcurrentDictionary<string, IReadOnlyList<SourceEntry>>(StringComparer.OrdinalIgnoreCase);
        var failed = new ConcurrentBag<string>();
        var errors = new ConcurrentBag<string>();
        using var gate = new SemaphoreSlim(Parallelism);
        FoldersRead = 0;
        LastError = null;
        Notify();

        async Task Visit(string folder)
        {
            IReadOnlyList<SourceEntry> entries;
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                entries = await _source.GetChildrenAsync(folder, CancellationToken.None).ConfigureAwait(false);
            }
            catch (SourceException exception)
            {
                failed.Add(folder);
                errors.Add(exception.Message);
                return;
            }
            finally
            {
                gate.Release();
            }

            fresh[folder] = entries;
            lock (_sync)
            {
                _folders[folder] = entries;
                FoldersRead++;
            }

            Notify();
            await Task.WhenAll(entries.Where(entry => entry.IsFolder).Select(entry => Visit(entry.FolderPath)))
                .ConfigureAwait(false);
        }

        await Visit(_source.RootPath).ConfigureAwait(false);

        lock (_sync)
        {
            // Удалённые с сервера папки уходят из каталога; непрочитанные остаются такими, какими были.
            var merged = new Dictionary<string, IReadOnlyList<SourceEntry>>(fresh, StringComparer.OrdinalIgnoreCase);
            foreach (var folder in failed)
                if (_folders.TryGetValue(folder, out var old)) merged[folder] = old;
            _folders = merged;
        }

        LastError = errors.FirstOrDefault();
        if (!fresh.IsEmpty)
        {
            IsFresh = true;
            UpdatedAt = DateTime.Now;
            SaveCache();
        }

        Notify();
    }

    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);

    private static string CacheFile(string server, string year)
    {
        var name = string.Concat($"{server}_{year}".Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToadTools", "RevitServerCache", name + ".json");
    }

    private void LoadCache()
    {
        if (_cacheFile is null) return;
        try
        {
            if (!File.Exists(_cacheFile)) return;
            var cache = JsonConvert.DeserializeObject<CacheFileData>(File.ReadAllText(_cacheFile));
            if (cache?.Folders is null) return;

            _folders = new Dictionary<string, IReadOnlyList<SourceEntry>>(
                cache.Folders.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<SourceEntry>)pair.Value),
                StringComparer.OrdinalIgnoreCase);
            UpdatedAt = cache.UpdatedAt;
        }
        catch
        {
            // Копия каталога — только ускорение: битая или чужая просто не используется.
        }
    }

    private void SaveCache()
    {
        if (_cacheFile is null) return;
        try
        {
            CacheFileData cache;
            lock (_sync)
            {
                cache = new CacheFileData
                {
                    UpdatedAt = UpdatedAt,
                    Folders = _folders.ToDictionary(pair => pair.Key, pair => pair.Value.ToList())
                };
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);
            File.WriteAllText(_cacheFile, JsonConvert.SerializeObject(cache));
        }
        catch
        {
            // Не записалась копия — в следующем сеансе каталог просто прочитается с сервера.
        }
    }

    private sealed class CacheFileData
    {
        public DateTime? UpdatedAt { get; set; }
        public Dictionary<string, List<SourceEntry>>? Folders { get; set; }
    }
}
