using System.IO;

namespace Families.Core;

/// <summary>
///     Картинки семейств: картинка версии не меняется никогда (ключ — хеш содержимого), поэтому она берётся из памяти, потом
///     с диска (<c>{хеш}.png</c>), и только потом с сервера. Одновременно грузится не больше четырёх; одна и та же — один раз.
/// </summary>
public sealed class PreviewCache(string folder, Func<Guid, string, CancellationToken, Task<byte[]>> load)
{
    public static string DefaultFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToadTools", "Families", "previews");

    private const int Capacity = 300;
    private readonly SemaphoreSlim _slots = new(4, 4);
    private readonly object _gate = new();
    private readonly Dictionary<string, Task<byte[]?>> _loading = new();
    private readonly Dictionary<string, byte[]> _memory = new();
    private readonly Queue<string> _order = new();

    /// <summary>Картинка или null, если её нет или сервер не отдал. Ошибки сети не бросает: картинка — не главное.</summary>
    public Task<byte[]?> GetAsync(Guid cardId, string hash)
    {
        lock (_gate)
        {
            if (_memory.TryGetValue(hash, out var cached)) return Task.FromResult<byte[]?>(cached);
            if (_loading.TryGetValue(hash, out var running)) return running;
            var task = LoadAsync(cardId, hash);
            _loading[hash] = task;
            return task;
        }
    }

    private async Task<byte[]?> LoadAsync(Guid cardId, string hash)
    {
        await _slots.WaitAsync().ConfigureAwait(false);
        try
        {
            var path = Path.Combine(folder, Safe(hash) + ".png");
            byte[]? bytes = null;
            try
            {
                if (File.Exists(path)) bytes = File.ReadAllBytes(path);
            }
            catch (IOException)
            {
                // прочитаем с сервера
            }

            if (bytes is null)
            {
                try
                {
                    bytes = await load(cardId, hash, CancellationToken.None).ConfigureAwait(false);
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(path, bytes);
                }
                catch (Exception)
                {
                    bytes ??= null; // нет картинки или сети — строка покажет заглушку
                }
            }

            if (bytes is { Length: > 0 }) Remember(hash, bytes);
            return bytes is { Length: > 0 } ? bytes : null;
        }
        finally
        {
            _slots.Release();
            lock (_gate) _loading.Remove(hash);
        }
    }

    private void Remember(string hash, byte[] bytes)
    {
        lock (_gate)
        {
            if (_memory.ContainsKey(hash)) return;
            _memory[hash] = bytes;
            _order.Enqueue(hash);
            while (_order.Count > Capacity) _memory.Remove(_order.Dequeue());
        }
    }

    private static string Safe(string hash) => new(hash.Where(char.IsLetterOrDigit).Take(64).ToArray());
}
