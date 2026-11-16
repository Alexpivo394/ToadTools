using System.Net;
using System.Net.Http.Headers;

namespace ToadTools.Desktop.Core.Downloads;

/// <summary>
///     Скачивание файла релиза: пишет {файл}.part, после обрыва докачивает с Range, 3 попытки с паузой 2, 4 с
///     (retryDelay × 1, × 2). Готовый файл переименовывается из .part, только когда размер сошёлся с ожидаемым.
/// </summary>
public sealed class Downloader(HttpMessageHandler handler, TimeSpan? retryDelay = null)
{
    private const int Attempts = 3;
    private readonly HttpClient _client = new(handler, false) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly TimeSpan _retryDelay = retryDelay ?? TimeSpan.FromSeconds(2);

    public async Task DownloadAsync(Uri url, string targetPath, long expectedSize, IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (File.Exists(targetPath) && new FileInfo(targetPath).Length == expectedSize) return;

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var part = targetPath + ".part";
        if (File.Exists(part) && new FileInfo(part).Length > expectedSize) File.Delete(part);

        for (var attempt = 1;; attempt++)
        {
            try
            {
                await DownloadOnceAsync(url, part, expectedSize, progress, cancellationToken).ConfigureAwait(false);
                break;
            }
            catch (Exception exception) when (attempt < Attempts && exception is HttpRequestException or IOException
                                                  && !cancellationToken.IsCancellationRequested)
            {
                Log.Info($"Скачивание {url} прервалось ({exception.Message}), попытка {attempt + 1} из {Attempts}");
                await Task.Delay(TimeSpan.FromTicks(_retryDelay.Ticks * attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        var size = new FileInfo(part).Length;
        if (size != expectedSize)
        {
            File.Delete(part);
            throw new InvalidDataException($"Скачано {size} байт вместо {expectedSize}: {url}");
        }

        if (File.Exists(targetPath)) File.Delete(targetPath);
        File.Move(part, targetPath);
    }

    private async Task DownloadOnceAsync(Uri url, string part, long expectedSize, IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var have = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (have == expectedSize && expectedSize > 0) return;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ToadTools-Desktop", "1.0"));
        if (have > 0) request.Headers.Range = new RangeHeaderValue(have, null);

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Сервер ответил {(int)response.StatusCode} {response.ReasonPhrase}: {url}");

        // На Range сервер ответил целым файлом (200) — начинаем заново
        var append = have > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!append) have = 0;

        using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var target = new FileStream(part, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
            have += read;
            if (expectedSize > 0) progress?.Report(Math.Min(1.0, (double)have / expectedSize));
        }
    }
}
