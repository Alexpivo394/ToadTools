using System.Net;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ToadTools.Desktop.Core.Releases;

/// <summary>
///     Последний релиз ToadTools на GitHub (releases/latest: без prerelease и черновиков). С ETag: ответ 304 не тратит
///     лимит GitHub (60 запросов в час без токена). Ошибки сети и HTTP не бросаются, а возвращаются как Failed.
/// </summary>
public sealed class GitHubReleaseClient(HttpMessageHandler handler, Uri? latestUrl = null)
{
    public static readonly Uri DefaultLatestUrl = new("https://api.github.com/repos/Alexpivo394/ToadTools/releases/latest");

    private readonly HttpClient _client = new(handler, false) { Timeout = TimeSpan.FromSeconds(30) };
    private readonly Uri _latestUrl = latestUrl ?? DefaultLatestUrl;

    public async Task<ReleaseCheck> GetLatestAsync(string? etag, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _latestUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ToadTools-Desktop", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrEmpty(etag) && EntityTagHeaderValue.TryParse(etag, out var tag)) request.Headers.IfNoneMatch.Add(tag);

        try
        {
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotModified)
                return new ReleaseCheck(ReleaseCheckStatus.NotModified, null, etag, null);

            if (!response.IsSuccessStatusCode) return Failed(Describe(response));

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return new ReleaseCheck(ReleaseCheckStatus.Updated, Parse(json), response.Headers.ETag?.ToString(), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Failed("GitHub не ответил за 30 секунд");
        }
        catch (HttpRequestException exception)
        {
            return Failed($"Нет связи с GitHub: {Innermost(exception).Message}");
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidDataException)
        {
            return Failed($"Ответ GitHub не разобрался: {exception.Message}");
        }
    }

    private static ReleaseCheck Failed(string error) => new(ReleaseCheckStatus.Failed, null, null, error);

    private static string Describe(HttpResponseMessage response)
    {
        var code = (int)response.StatusCode;
        if (code is 403 or 429 && response.Headers.TryGetValues("X-RateLimit-Remaining", out var left) && left.FirstOrDefault() == "0")
            return "Исчерпан лимит запросов к GitHub, следующая попытка позже";
        return $"GitHub ответил {code} {response.ReasonPhrase}";
    }

    private static Exception Innermost(Exception exception)
    {
        while (exception.InnerException != null) exception = exception.InnerException;
        return exception;
    }

    private static ReleaseInfo Parse(string json)
    {
        var root = JObject.Parse(json);
        var tag = (string?)root["tag_name"] ?? throw new InvalidDataException("в ответе нет tag_name");
        var assets = (root["assets"] as JArray ?? [])
            .Select(asset => new ReleaseAsset(
                (string?)asset["name"] ?? "",
                new Uri((string?)asset["browser_download_url"] ?? throw new InvalidDataException("у файла нет адреса")),
                (long?)asset["size"] ?? 0))
            .ToArray();
        return new ReleaseInfo(tag, (string?)root["body"] ?? "", assets);
    }
}
