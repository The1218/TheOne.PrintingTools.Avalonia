using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace TheOne.PrintingTools.Avalonia.Platforms;

internal sealed partial class MacPrintBackend : CupsPrintBackend, IPageMarginsBackend
{
    public async Task<PrintMargins> GetPageMarginsAsync(PrintSettings settings, CancellationToken token)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            var error = Marshal.AllocHGlobal(2048);
            try
            {
                Marshal.WriteByte(error, 0);
                if (TheOneGetImageableArea(settings.PrinterId!, settings.Paper.WidthMm, settings.Paper.HeightMm,
                        settings.Orientation == PrintOrientation.Landscape ? 1 : 0,
                        out var left, out var top, out var right, out var bottom, error, 2048) != 1)
                    throw new PrintException(Marshal.PtrToStringUTF8(error) ?? "无法读取 macOS 可打印区域。");
                return new PrintMargins(left, top, right, bottom);
            }
            finally
            {
                Marshal.FreeHGlobal(error);
            }
        });
    }

    public override async Task<PrintResult> PrintAsync(PrintDocument document, PrintSettings settings,
        PrinterCapabilities capabilities, CancellationToken token)
    {
        var margins = await GetPageMarginsAsync(settings, token).ConfigureAwait(false);
        return await PrintRendering.WithTemporaryPdfAsync(document, settings, margins, async path =>
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                var error = Marshal.AllocHGlobal(2048);
                try
                {
                    Marshal.WriteByte(error, 0);
                    var result = TheOnePrintPdf(path, settings.PrinterId!, document.Title, settings.Paper.WidthMm,
                        settings.Paper.HeightMm,
                        settings.Orientation == PrintOrientation.Landscape ? 1 : 0, (int)settings.Duplex,
                        settings.Color == PrintColor.Color ? 1 : 0, error, 2048);
                    if (result != 1) throw new PrintException(Marshal.PtrToStringUTF8(error) ?? "macOS 打印提交失败。");
                    return new PrintResult(null, "已交给 macOS 打印系统；尚未确认纸张输出。");
                }
                finally
                {
                    Marshal.FreeHGlobal(error);
                }
            });
        }, token).ConfigureAwait(false);
    }

    [LibraryImport("TheOnePrinting", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int TheOneGetImageableArea(string printer, double widthMm, double heightMm, int landscape,
        out double left, out double top, out double right, out double bottom, IntPtr error, int errorSize);

    [LibraryImport("TheOnePrinting", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int TheOnePrintPdf(string path, string printer, string title, double widthMm,
        double heightMm,
        int landscape, int duplex, int color, IntPtr error, int errorSize);
}