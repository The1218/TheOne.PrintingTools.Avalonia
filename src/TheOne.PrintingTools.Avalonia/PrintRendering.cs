using System.Diagnostics;
using SkiaSharp;

namespace TheOne.PrintingTools.Avalonia;

internal static class PrintRendering
{
    internal static Task ExportPdfAsync(PrintDocument document, Stream destination, PrintSettings settings,
        PrintMargins hardMargins, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("PDF 目标流不可写。", nameof(destination));
        PrintLayout.Validate(document, settings);
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using var pdf = SKDocument.CreatePdf(destination) ?? throw new PrintException("无法创建 PDF。");
            var (width, height) = PrintLayout.Size(settings);
            foreach (var page in PrintLayout.OutputPages(document, settings))
            {
                token.ThrowIfCancellationRequested();
                var canvas = pdf.BeginPage(width, height);
                PrintLayout.DrawSheet(canvas, document, settings, page, hardMargins);
                pdf.EndPage();
            }

            pdf.Close();
        }, token);
    }

    internal static byte[] Preview(PrintDocument document, PrintSettings settings, int page, PrintMargins? hardMargins,
        int maxEdge = 1600)
    {
        PrintLayout.Validate(document, settings);
        var (width, height) = PrintLayout.Size(settings);
        // Screen preview is bounded independently of printer DPI.
        var scale = Math.Min(1.5f, maxEdge / Math.Max(width, height));
        using var bitmap = new SKBitmap(Math.Max(1, (int)Math.Ceiling(width * scale)),
            Math.Max(1, (int)Math.Ceiling(height * scale)));
        using var canvas = new SKCanvas(bitmap);
        canvas.Scale(scale);
        PrintLayout.DrawSheet(canvas, document, settings, page, hardMargins);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    internal static async Task<T> WithTemporaryPdfAsync<T>(PrintDocument document, PrintSettings settings,
        PrintMargins margins,
        Func<string, Task<T>> submit, CancellationToken token)
    {
        // A private per-job directory also works on multi-user Linux desktops.
        var directory = Directory.CreateTempSubdirectory("theone-printing-").FullName;
        var path = Path.Combine(directory, "document.pdf");
        try
        {
            var options = new FileStreamOptions
                { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(path, options))
            {
                await ExportPdfAsync(document, stream, settings, margins, token).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();
            return await submit(path).ConfigureAwait(false);
        }
        finally
        {
            // Cleanup errors must not turn an accepted submission into a retryable failure.
            try
            {
                File.Delete(path);
                Directory.Delete(directory);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning("Printing temporary document cleanup failed: " + e.Message);
            }
        }
    }
}