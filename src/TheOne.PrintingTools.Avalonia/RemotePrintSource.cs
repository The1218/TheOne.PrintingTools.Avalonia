using System.Net;

namespace TheOne.PrintingTools.Avalonia;

internal sealed class RemotePrintSource : PrintSource
{
    private static readonly HttpClient SharedClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        MaxAutomaticRedirections = 5,
        UseCookies = false
    });

    private readonly HttpClient _client;
    private readonly Uri _url;

    internal RemotePrintSource(Uri url, HttpClient? client)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https"))
            throw new ArgumentException("网络打印源仅支持完整的 HTTP/HTTPS 地址。", nameof(url));
        _url = url;
        _client = client ?? SharedClient;
        var name = Uri.UnescapeDataString(url.Segments.LastOrDefault() ?? "").Trim('/');
        Title = string.IsNullOrWhiteSpace(name) ? $"网络文件 · {url.Host}" : $"网络 · {name}";
    }

    public override string Title { get; }
    internal override bool RequiresSingleSelection => PrintFileLoader.IsDocument(_url.AbsolutePath);

    public override async Task<PrintDocument> CreateDocumentAsync(CancellationToken cancellationToken = default)
    {
        var (extension, bytes) = await DownloadAsync(cancellationToken).ConfigureAwait(false);
        return await PrintFileLoader.LoadBytesAsync("download" + extension, bytes, cancellationToken, Title)
            .ConfigureAwait(false);
    }

    private async Task<(string Extension, byte[] Bytes)> DownloadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (_client.Timeout != Timeout.InfiniteTimeSpan) timeout.CancelAfter(_client.Timeout);
        var downloadToken = timeout.Token;
        try
        {
            using var response = await _client.GetAsync(_url, HttpCompletionOption.ResponseHeadersRead, downloadToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var type = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            var advertised = type switch
            {
                "application/pdf" => ".pdf", "image/png" => ".png", "image/jpeg" => ".jpg",
                "image/bmp" or "image/x-ms-bmp" or "image/x-bmp" => ".bmp", "image/webp" => ".webp",
                null or "application/octet-stream" or "binary/octet-stream" => null,
                _ => throw new NotSupportedException("网络地址返回的内容类型不受支持，仅支持 PDF 和 PNG/JPEG/BMP/WebP 图片。")
            };
            var length = response.Content.Headers.ContentLength;
            if (length > 64L * 1024 * 1024) throw new ArgumentException("网络文件超过 64 MB 导入上限。");
            if (advertised is not (null or ".pdf") && length > 32L * 1024 * 1024)
                throw new ArgumentException("网络图片超过 32 MB 导入上限。");
            await using var stream = await response.Content.ReadAsStreamAsync(downloadToken).ConfigureAwait(false);
            var prefix = new byte[1024];
            var count = await stream.ReadAtLeastAsync(prefix, prefix.Length, false, downloadToken)
                .ConfigureAwait(false);
            var extension = DetectFormat(prefix.AsSpan(0, count));
            if (advertised is not null && advertised != extension)
                throw new PrintException("网络文件内容与响应声明的类型不一致，请检查文件地址。");
            var limit = extension == ".pdf" ? 64 * 1024 * 1024 : 32 * 1024 * 1024;
            if (length > limit) throw new ArgumentException($"网络文件超过 {limit / 1024 / 1024} MB 导入上限。");
            var bytes = await PrintFileLoader.ReadLimitedAsync(stream, limit, downloadToken, prefix.AsMemory(0, count))
                .ConfigureAwait(false);
            return (extension, bytes);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new PrintException("网络打印源下载超时或请求已取消。");
        }
        catch (HttpRequestException error)
        {
            var status = error.StatusCode is { } code ? $"（HTTP {(int)code}）" : "，请检查网络连接";
            throw new PrintException($"网络打印源下载失败{status}。", error);
        }
        catch (IOException error)
        {
            throw new PrintException("网络文件读取失败，请检查网络连接。", error);
        }
    }

    private static string DetectFormat(ReadOnlySpan<byte> prefix)
    {
        if (prefix.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ".png";
        if (prefix.StartsWith(new byte[] { 255, 216, 255 })) return ".jpg";
        if (prefix.StartsWith("BM"u8)) return ".bmp";
        if (prefix.Length >= 12 && prefix.StartsWith("RIFF"u8) && prefix.Slice(8, 4).SequenceEqual("WEBP"u8))
            return ".webp";
        if (prefix.IndexOf("%PDF-"u8) >= 0) return ".pdf";
        throw new NotSupportedException("网络地址未返回有效的 PDF 或 PNG/JPEG/BMP/WebP 图片。");
    }
}