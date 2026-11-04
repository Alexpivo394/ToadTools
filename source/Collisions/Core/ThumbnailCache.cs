using System.IO;
using ToadTools.Connection;

namespace Collisions.Core;

/// <summary>Картинка коллизии или почему её нет: текст сервера («выгрузка модели удалена», «элемента нет в выгрузке») или «нет картинки».</summary>
public sealed record ThumbnailResult(byte[]? Image, string? Reason)
{
    public const string NoImage = "нет картинки";
}

/// <summary>
///     Картинки коллизий: память, потом диск (<c>{id:N}_{версия}.png</c>), потом сервер. Одновременно грузится не больше
///     <c>parallel</c> картинок, а одна и та же картинка — один раз, сколько бы строк её ни ждали. В памяти — только
///     <c>capacity</c> последних картинок: остальные при следующем показе читаются с диска.
/// </summary>
public sealed class ThumbnailCache
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static string DefaultFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToadTools", "Clash", "images");

    private readonly string _folder;
    private readonly Func<Guid, string, CancellationToken, Task<byte[]>> _load;
    private readonly SemaphoreSlim _slots;
    private readonly int _capacity;
    private readonly object _gate = new();

    // Последние запрошенные — в начале списка; при переполнении уходит хвост.
    private readonly Dictionary<string, LinkedListNode<Entry>> _items = new();
    private readonly LinkedList<Entry> _recent = new();

    public ThumbnailCache(string folder, Func<Guid, string, CancellationToken, Task<byte[]>> load, int parallel = 4, int capacity = 200)
    {
        _folder = folder;
        _load = load;
        _slots = new SemaphoreSlim(parallel, parallel);
        _capacity = Math.Max(1, capacity);
    }

    /// <remarks>Загрузку не отменяет отмена одного ждущего: картинка нужна и другим строкам; <paramref name="ct" /> прерывает только ожидание.</remarks>
    public Task<byte[]> GetAsync(Guid id, string version, CancellationToken ct)
    {
        var key = $"{id:N}_{Safe(version)}";
        Entry entry;
        lock (_gate)
        {
            if (_items.TryGetValue(key, out var node))
            {
                _recent.Remove(node);
                _recent.AddFirst(node);
                entry = node.Value;
            }
            else
            {
                entry = new Entry(key);
                _items[key] = _recent.AddFirst(entry);
                if (_items.Count > _capacity)
                {
                    // Вытесненная, если ещё грузится, догрузится для тех, кто её ждёт; новый запрос прочтёт её с диска.
                    var oldest = _recent.Last!;
                    _recent.RemoveLast();
                    _items.Remove(oldest.Value.Key);
                }
            }
        }

        var task = entry.Start(() => LoadAsync(id, version, entry));
        return ct.CanBeCanceled ? WaitAsync(task, ct) : task;
    }

    /// <summary>
    ///     Картинка или причина отказа — без исключений: строка списка и карточка показывают причину вместо вечного
    ///     «рисуется…». Отказ не запоминается: следующий запрос (кнопка «Обновить») попробует снова.
    /// </summary>
    public async Task<ThumbnailResult> TryGetAsync(Guid id, string version, CancellationToken ct)
    {
        try
        {
            return new ThumbnailResult(await GetAsync(id, version, ct).ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ThumbnailResult(null, Reason(exception));
        }
    }

    /// <summary>Причина для человека: текст сервера, иначе «нет картинки».</summary>
    public static string Reason(Exception exception) =>
        exception is ToadFlowRequestException { FromServer: true } refused && !string.IsNullOrWhiteSpace(refused.Message)
            ? refused.Message
            : ThumbnailResult.NoImage;

    /// <summary>Удаляет файлы кеша старше <paramref name="olderThan" /> и брошенные недописанные файлы.</summary>
    public void Prune(TimeSpan olderThan)
    {
        if (!Directory.Exists(_folder)) return;
        var before = DateTime.UtcNow - olderThan;
        foreach (var file in Directory.EnumerateFiles(_folder, "*.png").Concat(Directory.EnumerateFiles(_folder, "*.tmp")))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < before) File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private async Task<byte[]> LoadAsync(Guid id, string version, Entry entry)
    {
        try
        {
            var file = Path.Combine(_folder, entry.Key + ".png");
            if (ReadFile(file) is { } cached) return cached;

            await _slots.WaitAsync().ConfigureAwait(false);
            byte[] bytes;
            try
            {
                bytes = await _load(id, version, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _slots.Release();
            }

            if (IsPng(bytes)) WriteFile(file, bytes);
            return bytes;
        }
        catch
        {
            Forget(entry); // не получилось — в следующий раз попробуем снова
            throw;
        }
    }

    /// <summary>Файл с диска; повреждённый (не PNG — например, недописанный при сбое) удаляется, и картинка грузится заново.</summary>
    private static byte[]? ReadFile(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            var bytes = File.ReadAllBytes(file);
            if (IsPng(bytes)) return bytes;
            File.Delete(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    /// <summary>Сначала во временный файл, потом под своё имя: другой Revit или сбой не оставят недописанный PNG.</summary>
    private void WriteFile(string file, byte[] bytes)
    {
        var temp = $"{file}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(file)) File.Delete(file);
            File.Move(temp, file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private void Forget(Entry entry)
    {
        lock (_gate)
        {
            if (_items.TryGetValue(entry.Key, out var node) && ReferenceEquals(node.Value, entry))
            {
                _recent.Remove(node);
                _items.Remove(entry.Key);
            }
        }
    }

    private static bool IsPng(byte[] bytes) => bytes.Length > PngSignature.Length && PngSignature.SequenceEqual(bytes.Take(PngSignature.Length));

    private static async Task<byte[]> WaitAsync(Task<byte[]> task, CancellationToken ct)
    {
        var cancelled = new TaskCompletionSource<byte[]>();
        using (ct.Register(() => cancelled.TrySetCanceled()))
            return await (await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false)).ConfigureAwait(false);
    }

    private static string Safe(string version) => string.Concat(version.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));

    /// <summary>Одна картинка: загрузка начинается при первом запросе и одна на всех ждущих.</summary>
    private sealed class Entry(string key)
    {
        private Lazy<Task<byte[]>>? _task;

        public string Key => key;

        public Task<byte[]> Start(Func<Task<byte[]>> load)
        {
            Interlocked.CompareExchange(ref _task, new Lazy<Task<byte[]>>(load), null);
            return _task!.Value;
        }
    }
}
