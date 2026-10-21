using System.Net;
using System.Net.Http;
using Shouldly;
using ToadExporter.Core;
using ToadTools.UI.RevitServer;

namespace ToadExporter.Tests;

public class RevitServerSourceTests
{
    private const string Contents =
        """{"Path":"|","Folders":[{"Name":"К1","HasContents":true}],"Models":[{"Name":"AR.rvt","ModelSize":1}]}""";

    [Test]
    public async Task ParsesFoldersAndModels()
    {
        var source = new RevitServerSource("rs01", "2022", new FakeHandler(Contents));

        var entries = await source.GetChildrenAsync("", CancellationToken.None);

        entries.Count.ShouldBe(2);
        entries[0].ShouldBe(new SourceEntry("К1", "К1", "", true));
        entries[1].ShouldBe(new SourceEntry("AR.rvt", "", "RSN://rs01/AR.rvt", false));
    }

    [Test]
    public async Task NestedFolder_ModelPathIncludesFolder()
    {
        var handler = new FakeHandler(Contents);
        var source = new RevitServerSource("rs01", "2022", handler);

        var entries = await source.GetChildrenAsync("ЖК", CancellationToken.None);

        entries[0].FolderPath.ShouldBe("ЖК/К1");
        entries[1].ModelPath.ShouldBe("RSN://rs01/ЖК/AR.rvt");
        handler.LastRequest!.RequestUri!.AbsoluteUri.ShouldBe(RevitServerPaths.ContentsUrl("rs01", "2022", "ЖК"));
    }

    [Test]
    public async Task SendsRequiredHeaders()
    {
        var handler = new FakeHandler(Contents);

        await new RevitServerSource("rs01", "2022", handler).GetChildrenAsync("", CancellationToken.None);

        handler.LastRequest!.Headers.Contains("User-Name").ShouldBeTrue();
        handler.LastRequest.Headers.Contains("User-Machine-Name").ShouldBeTrue();
        handler.LastRequest.Headers.Contains("Operation-GUID").ShouldBeTrue();
    }

    [Test]
    public async Task HttpError_ThrowsSourceException()
    {
        var source = new RevitServerSource("rs01", "2022", new FakeHandler("", HttpStatusCode.NotFound));

        var error = await Should.ThrowAsync<SourceException>(() => source.GetChildrenAsync("", CancellationToken.None));

        error.Message.ShouldContain("rs01");
    }

    private sealed class FakeHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
