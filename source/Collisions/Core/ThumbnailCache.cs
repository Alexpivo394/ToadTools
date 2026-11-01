using System.Collections.Concurrent;
using System.IO;

namespace Collisions.Core;

/// <summary>
///     Картинки коллизий: память, потом диск (<c>{id:N}_{версия}.png</c>), потом сервер. Одновременно грузится не больше
///     <c>parallel</c> картинок, а одна и та же картинка — один раз, сколько бы строк её ни ждали.
/// </summary>
public sealed class ThumbnailCache
{
    public static string DefaultFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToadTools", "Clash", "images");

    private readonly string _folder;
    private readonly Func<Guid, string, CancellationToken, Task<byte[]>> _load;
    private readonly SemaphoreSlim _slots;
    private readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> _items = new();

    public ThumbnailCache(string folder, Func<Guid, string, CancellationToken, Task<byte[]>> load, int parallel = 4)
    {
        _folder = folder;
        _load = load;
        _slots = new SemaphoreSlim(parallel, parallel);
    }

    /// <remarks>Загрузку не отменяет отмена одного ждущего: картинка нужна и другим строкам; <paramref name="ct" /> прерывает только ожидание.</remarks>
    public Task<byte[]> GetAsync(Guid id, string version, CancellationToken ct)
    {
        var key = $"{id:N}_{Safe(version)}";
        var item = _items.GetOrAdd(key, k => new Lazy<Task<byte[]>>(() => LoadAsync(id, version, k)));
        var task = item.Value;
        return ct.CanBeCanceled ? WaitAsync(task, ct) : task;
    }

    /// <summary>Удаляет файлы кеша старше <paramref name="olderThan" />.</summary>
    public void Prune(TimeSpan olderThan)
    {
        if (!Directory.Exists(_folder)) return;
        var before = DateTime.UtcNow - olderThan;
        foreach (var file in Directory.EnumerateFiles(_folder, "*.png"))
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

    private async Task<byte[]> LoadAsync(Guid id, string version, string key)
    {
        try
        {
            var file = Path.Combine(_folder, key + ".png");
            if (File.Exists(file))
            {
                try
                {
                    return File.ReadAllBytes(file);
                }
                catch (IOException)
                {
                }
            }

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

            try
            {
                Directory.CreateDirectory(_folder);
                File.WriteAllBytes(file, bytes);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }

            return bytes;
        }
        catch
        {
            _items.TryRemove(key, out _); // не получилось — в следующий раз попробуем снова
            throw;
        }
    }

    private static async Task<byte[]> WaitAsync(Task<byte[]> task, CancellationToken ct)
    {
        var cancelled = new TaskCompletionSource<byte[]>();
        using (ct.Register(() => cancelled.TrySetCanceled()))
            return await (await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false)).ConfigureAwait(false);
    }

    private static string Safe(string version) => string.Concat(version.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
}
