using global::Avalonia;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Platform;
using global::Avalonia.Threading;
using SkiaSharp;

namespace TheOne.PrintingTools.Avalonia;

/// <summary>An immutable print snapshot. Coordinates are PDF points (72 per inch), not screen pixels.
/// Keep the document alive until the dialog / export / submission completes.</summary>
public sealed class PrintDocument : IDisposable
{
    private readonly SKPicture[] _pages;
    private readonly Action<SKCanvas, int>? _renderPage;
    private readonly IDisposable? _source;
    private readonly int _pageCount;
    internal ImagePrintDocument? Images { get; }
    public bool IsImageDocument => Images is not null;
    private bool _disposed;
    private readonly object _sync = new();
    public string Title { get; }
    public PrintPaper Paper { get; }
    public int PageCount => _pageCount;
    /// <summary>Physical page count with the supplied image layout. PageCount uses one image per page.</summary>
    public int GetPageCount(PrintSettings? settings = null)
    {
        if (Images is null || settings is null) return PageCount;
        ImagePrintDocument.Validate(settings.ImageOptions);
        var (rows, columns) = ImagePrintDocument.Grid(settings.ImageOptions.Layout);
        return (PageCount + rows * columns - 1) / (rows * columns);
    }
    public double WidthPoints => Paper.WidthMm * 72 / 25.4;
    public double HeightPoints => Paper.HeightMm * 72 / 25.4;

    private PrintDocument(string title, PrintPaper paper, SKPicture[] pages)
        => (Title, Paper, _pages, _pageCount) = (title, paper, pages, pages.Length);

    // PDF sources retain their immutable bytes and render only requested pages, avoiding an eager
    // bitmap for every page. This lifetime remains private to the owned PrintDocument.
    internal PrintDocument(string title, PrintPaper paper, int pageCount, Action<SKCanvas, int> renderPage, IDisposable source)
        => (Title, Paper, _pages, _pageCount, _renderPage, _source) = (title, paper, [], pageCount, renderPage, source);

    internal PrintDocument(string title, PrintPaper paper, ImagePrintDocument images)
        => (Title, Paper, _pages, _pageCount, Images) = (title, paper, [], images.Images.Length, images);

    /// <summary>Calls renderPage exactly once per page immediately, then owns the captured pictures.</summary>
    public static PrintDocument Create(string title, int pageCount, Action<SKCanvas, int> renderPage, PrintPaper? paper = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(renderPage);
        if (pageCount is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(pageCount));
        paper ??= PrintPaper.A4;
        PrintLayout.ValidatePaper(paper);
        var pages = new List<SKPicture>();
        try
        {
            for (var i = 0; i < pageCount; i++)
            {
                using var recorder = new SKPictureRecorder();
                var canvas = recorder.BeginRecording(new SKRect(0, 0, (float)(paper.WidthMm * 72 / 25.4), (float)(paper.HeightMm * 72 / 25.4)));
                renderPage(canvas, i);
                pages.Add(recorder.EndRecording());
            }
            return new(title, paper, pages.ToArray());
        }
        catch { foreach (var page in pages) page.Dispose(); throw; }
    }

    public static PrintDocument FromImage(string title, Stream image, PrintPaper? paper = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var input = new SKManagedStream(image, disposeManagedStream: false);
        using var bitmap = SKBitmap.Decode(input) ?? throw new ArgumentException("无法读取图片。", nameof(image));
        paper ??= PrintPaper.A4;
        return Create(title, 1, (canvas, _) =>
        {
            using var paint = new SKPaint { IsAntialias = true };
            var width = (float)(paper.WidthMm * 72 / 25.4); var height = (float)(paper.HeightMm * 72 / 25.4);
            var scale = Math.Min(width / bitmap.Width, height / bitmap.Height);
            canvas.DrawBitmap(bitmap, new SKRect((width - bitmap.Width * scale) / 2, (height - bitmap.Height * scale) / 2,
                (width + bitmap.Width * scale) / 2, (height + bitmap.Height * scale) / 2), paint);
        }, paper);
    }

    /// <summary>Snapshots sources in enumeration/page order into one job. Each page is fitted and centered
    /// on the target paper (the first source's paper by default). Sources remain caller-owned and may
    /// be disposed after this method returns. Page ranges and duplex operate across the combined document.</summary>
    public static PrintDocument Combine(string title, IEnumerable<PrintDocument> sources, PrintPaper? paper = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(sources);
        var pages = new List<(PrintDocument Source, int Page)>();
        var images = new List<byte[]>(); var allImages = true;
        foreach (var source in sources)
        {
            ArgumentNullException.ThrowIfNull(source);
            lock (source._sync) ObjectDisposedException.ThrowIf(source._disposed, source);
            paper ??= source.Paper;
            if (source.Images is not null) images.AddRange(source.Images.Images);
            else allImages = false;
            if (pages.Count + source.PageCount > 10000)
                throw new ArgumentException("合并文档不能超过 10000 页。", nameof(sources));
            for (var page = 0; page < source.PageCount; page++) pages.Add((source, page));
        }
        if (pages.Count == 0) throw new ArgumentException("请至少提供一个文档。", nameof(sources));
        var target = paper!;
        PrintLayout.ValidatePaper(target);
        if (allImages) return new PrintDocument(title, target, new ImagePrintDocument(images.ToArray()));
        return Create(title, pages.Count, (canvas, index) =>
        {
            var (source, page) = pages[index];
            var width = target.WidthMm * 72 / 25.4;
            var height = target.HeightMm * 72 / 25.4;
            var scale = Math.Min(width / source.WidthPoints, height / source.HeightPoints);
            canvas.Save();
            try
            {
                canvas.Translate((float)((width - source.WidthPoints * scale) / 2),
                    (float)((height - source.HeightPoints * scale) / 2));
                canvas.Scale((float)scale);
                canvas.ClipRect(new SKRect(0, 0, (float)source.WidthPoints, (float)source.HeightPoints));
                source.DrawPage(canvas, page);
            }
            finally { canvas.Restore(); }
        }, target);
    }

    /// <summary>Captures the arranged visual only; virtualized / offscreen records require explicit pagination.</summary>
    public static PrintDocument FromControl(string title, Visual visual, PrintPaper? paper = null)
    {
        Dispatcher.UIThread.VerifyAccess(); ArgumentNullException.ThrowIfNull(visual);
        var size = visual.Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) throw new ArgumentException("控件必须先完成布局。", nameof(visual));
        if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width * size.Height > 8_000_000)
            throw new ArgumentException("控件截图过大，请使用显式分页文档。", nameof(visual));
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width * 2), (int)Math.Ceiling(size.Height * 2)), new Vector(192, 192));
        bitmap.Render(visual);
        using var stream = new MemoryStream(); bitmap.Save(stream, PngBitmapEncoderOptions.Default); stream.Position = 0;
        return FromImage(title, stream, paper);
    }

    internal void Draw(SKCanvas canvas, int page)
        => DrawPage(canvas, page);

    internal void DrawImages(SKCanvas canvas, int page, SKRect area, PrintImageOptions options)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var (rows, columns) = ImagePrintDocument.Grid(options.Layout);
            if (page < 0 || page >= (PageCount + rows * columns - 1) / (rows * columns)) throw new ArgumentOutOfRangeException(nameof(page));
            Images!.Draw(canvas, page, area, options);
        }
    }

    /// <summary>Draws a captured page at its original point size. The page index is zero-based.</summary>
    public void DrawPage(SKCanvas canvas, int page)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (page < 0 || page >= PageCount) throw new ArgumentOutOfRangeException(nameof(page));
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Images is not null) Images.Draw(canvas, page, new SKRect(0, 0, (float)WidthPoints, (float)HeightPoints), new PrintImageOptions());
            else if (_renderPage is not null) _renderPage(canvas, page);
            else canvas.DrawPicture(_pages[page]);
        }
    }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true; foreach (var page in _pages) page.Dispose(); _source?.Dispose();
        }
    }
}
