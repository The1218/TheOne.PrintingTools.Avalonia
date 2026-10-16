namespace TheOne.PrintingTools.Avalonia;

internal static class PrintFileLoader
{
    internal static bool IsImage(string name) => Path.GetExtension(name).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp";
    internal static bool IsDocument(string name) => Path.GetExtension(name).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    internal static void Validate(string name)
    {
        if (!IsImage(name) && !IsDocument(name))
            throw new NotSupportedException($"{name}：仅支持 PDF 及 PNG/JPEG/BMP/WebP 图片，不支持 Word / TXT。");
    }

    // The caller keeps the input stream. Snapshots never reference its buffers.
    internal static async Task<PrintDocument> LoadAsync(string name, Stream stream, CancellationToken token,
        string? title = null)
    {
        Validate(name); token.ThrowIfCancellationRequested();
        var input = await ReadLimitedAsync(stream, IsDocument(name) ? 64 * 1024 * 1024 : 32 * 1024 * 1024, token).ConfigureAwait(false);
        if (IsDocument(name))
            return await Task.Run(() => PdfPrintDocument.Create(title ?? name, input, token), token).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            return ImagePrintDocument.Create(title ?? name, input);
        }, token).ConfigureAwait(false);
    }

    internal static async Task<byte[]> ReadLimitedAsync(Stream stream, int limit, CancellationToken token)
    {
        using var input = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            if (input.Length + count > limit) throw new ArgumentException($"文件超过 {limit / 1024 / 1024} MB 导入上限。");
            input.Write(buffer, 0, count);
        }
        token.ThrowIfCancellationRequested();
        return input.ToArray();
    }
}
