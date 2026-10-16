using global::Avalonia;
using global::Avalonia.Threading;
using SkiaSharp;

namespace TheOne.PrintingTools.Avalonia;

/// <summary>A printable source. File paths, path arrays, image streams, visuals and documents convert
/// implicitly. Sources do not own caller streams, visuals or documents.</summary>
public abstract class PrintSource
{
    public abstract string Title { get; }
    internal virtual bool RequiresSingleSelection => false;

    /// <summary>Creates a new snapshot owned by the caller. PrintAsync calls this and disposes the
    /// result after preview/submission finishes. Custom sources must return a fresh owned document.</summary>
    public abstract Task<PrintDocument> CreateDocumentAsync(CancellationToken cancellationToken = default);

    public static PrintSource FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        PrintFileLoader.Validate(path);
        var fullPath = Path.GetFullPath(path);
        var name = Path.GetFileName(fullPath);
        return new Source(name, async token =>
        {
            await using var stream = File.OpenRead(fullPath);
            return await PrintFileLoader.LoadAsync(name, stream, token).ConfigureAwait(false);
        }, PrintFileLoader.IsDocument(name));
    }

    public static PrintSource FromFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var sources = new List<PrintSource>();
        foreach (var path in paths)
        {
            if (sources.Count == 100) throw new ArgumentException("一次最多导入 100 个文件。", nameof(paths));
            sources.Add(FromFile(path));
        }
        if (sources.Count > 1 && sources.Any(source => source.RequiresSingleSelection))
            throw new ArgumentException("PDF 只能单选，多个文件仅支持图片。", nameof(paths));
        return Combine(sources);
    }

    /// <summary>Reads an image or PDF from the current stream position. The file name
    /// selects the format. The caller keeps the stream open until PrintAsync completes.</summary>
    public static PrintSource FromStream(Stream stream, string fileName)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        PrintFileLoader.Validate(fileName);
        return new Source(fileName, token => PrintFileLoader.LoadAsync(fileName, stream, token), PrintFileLoader.IsDocument(fileName));
    }

    public static PrintSource FromImage(Stream image, string title = "图片")
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return new Source(title, token => PrintFileLoader.LoadAsync("image.png", image, token, title));
    }

    /// <summary>The visual must be arranged. Capture is dispatched to the UI thread.</summary>
    public static PrintSource FromControl(Visual visual, string title = "控件")
    {
        ArgumentNullException.ThrowIfNull(visual);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return new Source(title, async token =>
        {
            PrintDocument Capture() { token.ThrowIfCancellationRequested(); return PrintDocument.FromControl(title, visual); }
            return Dispatcher.UIThread.CheckAccess() ? Capture() : await Dispatcher.UIThread.InvokeAsync(Capture);
        });
    }

    public static PrintSource FromDocument(PrintDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new Source(document.Title, token =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(PrintDocument.Combine(document.Title, [document]));
        });
    }

    /// <summary>Draws explicit pages when the source is materialized. Drawing is synchronous and uses PDF points.</summary>
    public static PrintSource FromPages(string title, int pageCount, Action<SKCanvas, int> renderPage, PrintPaper? paper = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(renderPage);
        return new Source(title, token => Task.FromResult(PrintDocument.Create(title, pageCount, (canvas, page) =>
        {
            token.ThrowIfCancellationRequested(); renderPage(canvas, page);
        }, paper)));
    }

    /// <summary>Combines sources in input order, preserving aspect ratio on the first source's paper
    /// (or an explicitly supplied common paper). Intermediate snapshots are released on success or failure.</summary>
    public static PrintSource Combine(IEnumerable<PrintSource> sources, string? title = null, PrintPaper? paper = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var items = new List<PrintSource>();
        foreach (var source in sources)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (items.Count == 10000) throw new ArgumentException("来源数量不能超过 10000。", nameof(sources));
            items.Add(source);
        }
        if (items.Count == 0) throw new ArgumentException("请至少提供一个打印源。", nameof(sources));
        if (items.Count > 1 && items.Any(source => source.RequiresSingleSelection))
            throw new ArgumentException("PDF 只能作为单个打印源，不能与其他来源混选。", nameof(sources));
        if (items.Count == 1 && title is null && paper is null) return items[0];
        title ??= items.Count == 1 ? items[0].Title : $"合并打印 · {items.Count} 个来源";
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return new Source(title, async token =>
        {
            var documents = new List<PrintDocument>();
            long totalPages = 0;
            long imageBytes = 0;
            try
            {
                foreach (var item in items)
                {
                    token.ThrowIfCancellationRequested();
                    documents.Add(await item.CreateDocumentAsync(token));
                    totalPages += documents[^1].PageCount;
                    imageBytes += documents[^1].Images?.ByteCount ?? 0;
                    if (imageBytes > 256L * 1024 * 1024) throw new ArgumentException("图片总大小不能超过 256 MB。", nameof(sources));
                    if (totalPages > 10000)
                        throw new ArgumentException("合并文档不能超过 10000 页。", nameof(sources));
                }
                token.ThrowIfCancellationRequested();
                return PrintDocument.Combine(title, documents, paper);
            }
            finally { foreach (var document in documents) document.Dispose(); }
        }, items.Any(source => source.RequiresSingleSelection));
    }

    public static implicit operator PrintSource(string path) => FromFile(path);
    public static implicit operator PrintSource(string[] paths) => FromFiles(paths);
    public static implicit operator PrintSource(Stream image) => FromImage(image);
    public static implicit operator PrintSource(Visual visual) => FromControl(visual);
    public static implicit operator PrintSource(PrintDocument document) => FromDocument(document);
    public static implicit operator PrintSource(PrintSource[] sources) => Combine(sources);

    private sealed class Source(string title, Func<CancellationToken, Task<PrintDocument>> create, bool requiresSingleSelection = false) : PrintSource
    {
        public override string Title => title;
        internal override bool RequiresSingleSelection => requiresSingleSelection;
        public override Task<PrintDocument> CreateDocumentAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return create(cancellationToken);
        }
    }
}
