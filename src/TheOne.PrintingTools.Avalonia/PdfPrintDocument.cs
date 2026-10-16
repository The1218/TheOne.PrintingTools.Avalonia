using System.Runtime.InteropServices;
using SkiaSharp;
using TheOne.PrintingTools.Avalonia.Platforms;

namespace TheOne.PrintingTools.Avalonia;

// PDFium is process-global and not thread-safe. All initialization, page operations and disposal
// share this gate. The source bytes stay pinned until the owning PrintDocument is disposed.
internal sealed partial class PdfPrintDocument : IDisposable
{
    private static readonly object Gate = new();
    private static bool _initialized;
    private readonly PrintPaper _paper;
    private readonly (double Width, double Height)[] _sizes;
    private GCHandle _bytes;
    private IntPtr _document;

    private PdfPrintDocument(byte[] bytes, CancellationToken token)
    {
        lock (Gate)
        {
            NativeLibraries.Initialize();
            if (!_initialized)
            {
                FPDF_InitLibrary();
                _initialized = true;
            }

            _bytes = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                _document = FPDF_LoadMemDocument64(_bytes.AddrOfPinnedObject(), (nuint)bytes.Length, IntPtr.Zero);
                if (_document == IntPtr.Zero) throw new PrintException("无法读取 PDF（文件可能损坏或需要密码）。");
                var count = FPDF_GetPageCount(_document);
                if (count is < 1 or > 10000) throw new PrintException("PDF 页数必须在 1–10000 内。");
                _sizes = new (double Width, double Height)[count];
                for (var page = 0; page < count; page++)
                {
                    token.ThrowIfCancellationRequested();
                    if (FPDF_GetPageSizeByIndex(_document, page, out var width, out var height) != 1 ||
                        !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
                        throw new PrintException($"无法读取 PDF 第 {page + 1} 页的尺寸。");
                    _sizes[page] = (width, height);
                }

                _paper = new PrintPaper("PDF", _sizes[0].Width * 25.4 / 72, _sizes[0].Height * 25.4 / 72);
                PrintLayout.ValidatePaper(_paper);
            }
            catch
            {
                Dispose();
                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (_document != IntPtr.Zero)
            {
                FPDF_CloseDocument(_document);
                _document = IntPtr.Zero;
            }

            if (_bytes.IsAllocated) _bytes.Free();
            GC.SuppressFinalize(this);
        }
    }

    ~PdfPrintDocument()
    {
        Dispose();
    }

    internal static PrintDocument Create(string title, byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var source = new PdfPrintDocument(bytes, token);
        return new PrintDocument(title, source._paper, source._sizes.Length, source.DrawPage, source);
    }

    private void DrawPage(SKCanvas canvas, int index)
    {
        lock (Gate)
        {
            ObjectDisposedException.ThrowIf(_document == IntPtr.Zero, this);
            var (width, height) = _sizes[index];
            // 300 DPI for normal pages; bound unusually large pages to 16M pixels / 8192 per edge.
            var rasterScale = Math.Min(300d / 72,
                Math.Min(8192 / Math.Max(width, height), Math.Sqrt(16_000_000 / (width * height))));
            var pixelWidth = Math.Max(1, (int)Math.Floor(width * rasterScale));
            var pixelHeight = Math.Max(1, (int)Math.Floor(height * rasterScale));
            using var bitmap = new SKBitmap(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Opaque);
            var page = FPDF_LoadPage(_document, index);
            if (page == IntPtr.Zero) throw new PrintException($"无法加载 PDF 第 {index + 1} 页。");
            var pdfBitmap = IntPtr.Zero;
            try
            {
                pdfBitmap = FPDFBitmap_CreateEx(pixelWidth, pixelHeight, 4, bitmap.GetPixels(), bitmap.RowBytes);
                if (pdfBitmap == IntPtr.Zero) throw new PrintException("无法创建 PDF 页面位图。");
                FPDFBitmap_FillRect(pdfBitmap, 0, 0, pixelWidth, pixelHeight, 0xFFFFFFFF);
                FPDF_RenderPageBitmap(pdfBitmap, page, 0, 0, pixelWidth, pixelHeight, 0,
                    0x801); // annotations + printing
                var targetWidth = _paper.WidthMm * 72 / 25.4;
                var targetHeight = _paper.HeightMm * 72 / 25.4;
                var fit = Math.Min(targetWidth / width, targetHeight / height);
                var left = (targetWidth - width * fit) / 2;
                var top = (targetHeight - height * fit) / 2;
                using var paint = new SKPaint { IsAntialias = true };
                canvas.DrawBitmap(bitmap, new SKRect((float)left, (float)top,
                    (float)(left + width * fit), (float)(top + height * fit)), paint);
            }
            finally
            {
                if (pdfBitmap != IntPtr.Zero) FPDFBitmap_Destroy(pdfBitmap);
                FPDF_ClosePage(page);
            }
        }
    }

    [LibraryImport("pdfium")]
    private static partial void FPDF_InitLibrary();

    [LibraryImport("pdfium")]
    private static partial IntPtr FPDF_LoadMemDocument64(IntPtr data, nuint length, IntPtr password);

    [LibraryImport("pdfium")]
    private static partial int FPDF_GetPageCount(IntPtr document);

    [LibraryImport("pdfium")]
    private static partial int FPDF_GetPageSizeByIndex(IntPtr document, int page, out double width, out double height);

    [LibraryImport("pdfium")]
    private static partial IntPtr FPDF_LoadPage(IntPtr document, int page);

    [LibraryImport("pdfium")]
    private static partial void FPDF_ClosePage(IntPtr page);

    [LibraryImport("pdfium")]
    private static partial void FPDF_CloseDocument(IntPtr document);

    [LibraryImport("pdfium")]
    private static partial IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr pixels, int stride);

    [LibraryImport("pdfium")]
    private static partial void
        FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);

    [LibraryImport("pdfium")]
    private static partial void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int x, int y, int width, int height,
        int rotate, int flags);

    [LibraryImport("pdfium")]
    private static partial void FPDFBitmap_Destroy(IntPtr bitmap);
}