using Collisions.Core;
using Shouldly;

namespace ToadTools.Tests.Collisions;

public class ThumbnailCacheTests
{
    private static readonly Guid Id = Guid.Parse("11111111-2222-3333-4444-555555555555");

    /// <summary>Подпись PNG и несколько байт: на диск кладутся и с диска читаются только такие.</summary>
    private static byte[] Png(params byte[] tail) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..tail];

    [Test]
    public async Task Thumbnails_SecondRequest_FromMemory()
    {
        using var temp = new TempFolder();
        var loads = 0;
        var cache = new ThumbnailCache(temp.Path, (_, _, _) =>
        {
            loads++;
            return Task.FromResult(new byte[] { 7 });
        });

        (await cache.GetAsync(Id, "v1", CancellationToken.None)).ShouldBe([7]);
        File.Delete(Path.Combine(temp.Path, $"{Id:N}_v1.png"));
        (await cache.GetAsync(Id, "v1", CancellationToken.None)).ShouldBe([7]);

        loads.ShouldBe(1);
    }

    [Test]
    public async Task Thumbnails_FromDisk_AfterRestart()
    {
        using var temp = new TempFolder();
        await new ThumbnailCache(temp.Path, (_, _, _) => Task.FromResult(Png(1, 2))).GetAsync(Id, "v1", CancellationToken.None);
        File.Exists(Path.Combine(temp.Path, $"{Id:N}_v1.png")).ShouldBeTrue();

        var loads = 0;
        var restarted = new ThumbnailCache(temp.Path, (_, _, _) =>
        {
            loads++;
            return Task.FromResult(Png(9));
        });

        (await restarted.GetAsync(Id, "v1", CancellationToken.None)).ShouldBe(Png(1, 2));
        (await restarted.GetAsync(Id, "v2", CancellationToken.None)).ShouldBe(Png(9)); // новая версия картинки — заново
        loads.ShouldBe(1);
    }

    [Test]
    public async Task Thumbnails_ParallelLimit4()
    {
        using var temp = new TempFolder();
        var (running, peak) = (0, 0);
        var gate = new object();
        var cache = new ThumbnailCache(temp.Path, async (_, _, ct) =>
        {
            lock (gate) peak = Math.Max(peak, ++running);
            await Task.Delay(40, ct);
            lock (gate) running--;
            return [1];
        });

        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => cache.GetAsync(Guid.NewGuid(), "v", CancellationToken.None)));

        peak.ShouldBe(4);
    }

    [Test]
    public async Task Thumbnails_SameKeyConcurrent_OneLoad()
    {
        using var temp = new TempFolder();
        var loads = 0;
        var release = new TaskCompletionSource<byte[]>();
        var cache = new ThumbnailCache(temp.Path, (_, _, _) =>
        {
            Interlocked.Increment(ref loads);
            return release.Task;
        });

        var first = cache.GetAsync(Id, "v1", CancellationToken.None);
        var second = cache.GetAsync(Id, "v1", CancellationToken.None);
        release.SetResult([5]);

        (await first).ShouldBe([5]);
        (await second).ShouldBe([5]);
        loads.ShouldBe(1);
    }

    [Test]
    public async Task Thumbnails_FailedLoad_RetriedNextTime()
    {
        using var temp = new TempFolder();
        var loads = 0;
        var cache = new ThumbnailCache(temp.Path,
            (_, _, _) => ++loads == 1 ? Task.FromException<byte[]>(new InvalidOperationException("рисуется")) : Task.FromResult(new byte[] { 3 }));

        await Should.ThrowAsync<InvalidOperationException>(() => cache.GetAsync(Id, "v1", CancellationToken.None));
        (await cache.GetAsync(Id, "v1", CancellationToken.None)).ShouldBe([3]);
    }

    [Test]
    public async Task Thumbnails_Prune_OlderThan30Days()
    {
        using var temp = new TempFolder();
        var old = Path.Combine(temp.Path, $"{Guid.NewGuid():N}_v1.png");
        var fresh = Path.Combine(temp.Path, $"{Guid.NewGuid():N}_v1.png");
        await File.WriteAllBytesAsync(old, [1]);
        await File.WriteAllBytesAsync(fresh, [1]);
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-31));

        new ThumbnailCache(temp.Path, (_, _, _) => Task.FromResult(Array.Empty<byte>())).Prune(TimeSpan.FromDays(30));

        File.Exists(old).ShouldBeFalse();
        File.Exists(fresh).ShouldBeTrue();
    }

    [Test]
    public async Task Thumbnails_Memory_KeepsOnlyRecent()
    {
        using var temp = new TempFolder();
        var loads = new List<Guid>();
        var cache = new ThumbnailCache(temp.Path, (id, _, _) =>
        {
            loads.Add(id);
            return Task.FromResult(new byte[] { 1 }); // не PNG — на диск не ляжет, повтор только из памяти или с сервера
        }, capacity: 2);
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await cache.GetAsync(a, "v", CancellationToken.None);
        await cache.GetAsync(b, "v", CancellationToken.None);
        await cache.GetAsync(a, "v", CancellationToken.None); // a — свежая
        await cache.GetAsync(c, "v", CancellationToken.None); // вытесняет b
        await cache.GetAsync(a, "v", CancellationToken.None);
        await cache.GetAsync(b, "v", CancellationToken.None);

        loads.ShouldBe([a, b, c, b]);
    }

    [Test]
    public async Task Thumbnails_BrokenFileOnDisk_Reloaded()
    {
        using var temp = new TempFolder();
        var file = Path.Combine(temp.Path, $"{Id:N}_v1.png");
        await File.WriteAllBytesAsync(file, [1, 2, 3]); // недописанный файл после сбоя
        var cache = new ThumbnailCache(temp.Path, (_, _, _) => Task.FromResult(Png(5)));

        (await cache.GetAsync(Id, "v1", CancellationToken.None)).ShouldBe(Png(5));

        (await File.ReadAllBytesAsync(file)).ShouldBe(Png(5));
        Directory.GetFiles(temp.Path).ShouldBe([file]); // временного файла не осталось
    }
}
