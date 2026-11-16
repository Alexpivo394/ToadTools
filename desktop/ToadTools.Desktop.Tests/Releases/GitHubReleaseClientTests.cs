using System.Net;
using Shouldly;
using ToadTools.Desktop.Core.Releases;

namespace ToadTools.Desktop.Tests.Releases;

public class GitHubReleaseClientTests
{
    private static string Latest => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "latest.json"));

    [Test]
    public async Task Ok_ParsesRelease_AndETag()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = FakeHandler.Json(Latest);
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"abc\"");
            return response;
        });

        var check = await new GitHubReleaseClient(handler).GetLatestAsync(null, CancellationToken.None);

        check.Status.ShouldBe(ReleaseCheckStatus.Updated);
        check.ETag.ShouldBe("\"abc\"");
        check.Release!.Tag.ShouldBe("4.0.3");
        check.Release.Notes.ShouldContain("Коллизии");
        check.Release.Assets.Count.ShouldBe(4);
        check.Release.Assets[0].Name.ShouldBe("ToadTools-4.0.3.zip");
        check.Release.Assets[0].Size.ShouldBe(1048576);
        check.Release.Assets[0].DownloadUrl.ShouldBe(
            new Uri("https://github.com/Alexpivo394/ToadTools/releases/download/4.0.3/ToadTools-4.0.3.zip"));
        var request = handler.Requests.Single();
        request.RequestUri.ShouldBe(new Uri("https://api.github.com/repos/Alexpivo394/ToadTools/releases/latest"));
        request.Headers.UserAgent.ToString().ShouldContain("ToadTools-Desktop");
    }

    [Test]
    public async Task NotModified_WhenETagMatches()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotModified));

        var check = await new GitHubReleaseClient(handler).GetLatestAsync("\"abc\"", CancellationToken.None);

        check.Status.ShouldBe(ReleaseCheckStatus.NotModified);
        handler.Requests.Single().Headers.IfNoneMatch.Single().Tag.ShouldBe("\"abc\"");
    }

    [Test]
    public async Task RateLimited_Fails_WithMessage()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = FakeHandler.Json("{\"message\":\"API rate limit exceeded\"}", HttpStatusCode.Forbidden);
            response.Headers.Add("X-RateLimit-Remaining", "0");
            return response;
        });

        var check = await new GitHubReleaseClient(handler).GetLatestAsync(null, CancellationToken.None);

        check.Status.ShouldBe(ReleaseCheckStatus.Failed);
        check.Error!.ShouldContain("лимит");
    }

    [Test]
    public async Task ServerError_Fails_WithStatus()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("oops", HttpStatusCode.BadGateway));

        var check = await new GitHubReleaseClient(handler).GetLatestAsync(null, CancellationToken.None);

        check.Status.ShouldBe(ReleaseCheckStatus.Failed);
        check.Error!.ShouldContain("502");
    }

    [Test]
    public async Task NetworkError_Fails_WithoutThrowing()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("no route"));

        var check = await new GitHubReleaseClient(handler).GetLatestAsync(null, CancellationToken.None);

        check.Status.ShouldBe(ReleaseCheckStatus.Failed);
        check.Error!.ShouldContain("no route");
    }

    [Test]
    public async Task BrokenJson_Fails_WithoutThrowing()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("<html>captive portal</html>"));

        var check = await new GitHubReleaseClient(handler).GetLatestAsync(null, CancellationToken.None);

        check.Status.ShouldBe(ReleaseCheckStatus.Failed);
    }

    [Test]
    public async Task CustomFeedUrl_IsUsed()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json(Latest));

        await new GitHubReleaseClient(handler, new Uri("http://localhost:5000/latest.json"))
            .GetLatestAsync(null, CancellationToken.None);

        handler.Requests.Single().RequestUri.ShouldBe(new Uri("http://localhost:5000/latest.json"));
    }
}
