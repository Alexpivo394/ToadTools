using System.Net;
using System.Text;

namespace ToadTools.Tests;

/// <summary>Подставной сервер: отвечает функцией на каждый запрос и запоминает запросы с телами.</summary>
public sealed class FakeHttp(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Requests) Requests.Add((request, body));
        return respond(request, body);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Ok(string json) => Json(HttpStatusCode.OK, json);
}

/// <summary>Временная папка теста, удаляется после него.</summary>
public sealed class TempFolder : IDisposable
{
    public string Path { get; } = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "toadtools-tests", Guid.NewGuid().ToString("N"))).FullName;

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, true);
        }
        catch (IOException)
        {
        }
    }
}
