using System.Net;
using System.Net.Http.Headers;
using Shouldly;
using ToadTools.Desktop.Core.Downloads;

namespace ToadTools.Desktop.Tests.Downloads;

public class DownloaderTests
{
    private static readonly Uri Url = new("https://example.org/ToadTools-4.0.3.zip");
    private static readonly byte[] Payload = Enumerable.Range(0, 100_000).Select(i => (byte)(i % 251)).ToArray();

    [Test]
    public async Task WholeFile_Downloads()
    {
        using var temp = new TempFolder();
        var target = temp.Combine("a.zip");
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) });

        await new Downloader(handler, TimeSpan.Zero).DownloadAsync(Url, target, Payload.Length, null, CancellationToken.None);

        File.ReadAllBytes(target).ShouldBe(Payload);
        File.Exists(target + ".part").ShouldBeFalse();
    }

    [Test]
    public async Task BrokenMidway_ResumesWithRange()
    {
        using var temp = new TempFolder();
        var target = temp.Combine("a.zip");
        var calls = 0;
        var handler = new FakeHandler(request =>
        {
            calls++;
            if (calls == 1) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BreakingStream(Payload, 40_000)) };

            var from = (int)request.Headers.Range!.Ranges.Single().From!.Value;
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(Payload.Skip(from).ToArray()) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, Payload.Length - 1, Payload.Length);
            return response;
        });

        await new Downloader(handler, TimeSpan.Zero).DownloadAsync(Url, target, Payload.Length, null, CancellationToken.None);

        File.ReadAllBytes(target).ShouldBe(Payload);
        handler.Requests[1].Headers.Range!.Ranges.Single().From.ShouldBe(40_000);
    }

    [Test]
    public async Task RangeIgnored_StartsOver()
    {
        using var temp = new TempFolder();
        var target = temp.Combine("a.zip");
        File.WriteAllBytes(target + ".part", Payload.Take(30_000).ToArray());
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) });

        await new Downloader(handler, TimeSpan.Zero).DownloadAsync(Url, target, Payload.Length, null, CancellationToken.None);

        File.ReadAllBytes(target).ShouldBe(Payload);
    }

    [Test]
    public async Task WrongSize_Throws_NoTarget()
    {
        using var temp = new TempFolder();
        var target = temp.Combine("a.zip");
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) });

        await Should.ThrowAsync<InvalidDataException>(() =>
            new Downloader(handler, TimeSpan.Zero).DownloadAsync(Url, target, Payload.Length + 1, null, CancellationToken.None));

        File.Exists(target).ShouldBeFalse();
    }

    [Test]
    public async Task AlreadyDownloaded_DoesNotRequest()
    {
        using var temp = new TempFolder();
        var target = temp.Combine("a.zip");
        File.WriteAllBytes(target, Payload);
        var handler = new FakeHandler(_ => throw new InvalidOperationException("не должен качать"));

        await new Downloader(handler, TimeSpan.Zero).DownloadAsync(Url, target, Payload.Length, null, CancellationToken.None);

        handler.Requests.ShouldBeEmpty();
    }

    [Test]
    public async Task ServerAlwaysFails_ThrowsAfterThreeAttempts()
    {
        using var temp = new TempFolder();
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        await Should.ThrowAsync<HttpRequestException>(() =>
            new Downloader(handler, TimeSpan.Zero).DownloadAsync(Url, temp.Combine("a.zip"), Payload.Length, null, CancellationToken.None));

        handler.Requests.Count.ShouldBe(3);
    }

    /// <summary>Поток, который обрывается на заданном байте, как разорванное соединение.</summary>
    private sealed class BreakingStream(byte[] data, int breakAt) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (Position >= breakAt) throw new IOException("connection reset");
            return base.Read(buffer, offset, (int)Math.Min(count, breakAt - Position));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer, offset, count));
    }
}
