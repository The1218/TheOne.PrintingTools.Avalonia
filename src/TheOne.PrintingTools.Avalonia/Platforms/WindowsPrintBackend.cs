using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace TheOne.PrintingTools.Avalonia.Platforms;

internal sealed partial class WindowsPrintBackend : IPrintBackend, IPageMarginsBackend
{
    public Task<PrintMargins> GetPageMarginsAsync(PrintSettings settings, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OpenPrinterW(settings.PrinterId!, out var printer, IntPtr.Zero) == 0) throw Error("无法打开打印机");
            IntPtr mode = IntPtr.Zero, dc = IntPtr.Zero;
            try
            {
                mode = ConfigureMode(printer, settings);
                dc = CreateDCW("WINSPOOL", settings.PrinterId!, null, mode);
                if (dc == IntPtr.Zero) throw Error("无法读取可打印区域");
                var dx = GetDeviceCaps(dc, 88);
                var dy = GetDeviceCaps(dc, 90);
                if (dx <= 0 || dy <= 0) throw new PrintException("打印设备分辨率无效。");
                var size = PrintLayout.Size(settings);
                if (Math.Abs(GetDeviceCaps(dc, 110) * 72d / dx - size.Width) > 6 ||
                    Math.Abs(GetDeviceCaps(dc, 111) * 72d / dy - size.Height) > 6)
                    throw new PrintException("驱动实际纸张与所选纸张不一致，请重新选择。");
                return new PrintMargins(GetDeviceCaps(dc, 112) * 25.4 / dx, GetDeviceCaps(dc, 113) * 25.4 / dy,
                    Math.Max(0, GetDeviceCaps(dc, 110) - GetDeviceCaps(dc, 112) - GetDeviceCaps(dc, 8)) * 25.4 / dx,
                    Math.Max(0, GetDeviceCaps(dc, 111) - GetDeviceCaps(dc, 113) - GetDeviceCaps(dc, 10)) * 25.4 / dy);
            }
            finally
            {
                if (dc != IntPtr.Zero) DeleteDC(dc);
                if (mode != IntPtr.Zero) Marshal.FreeHGlobal(mode);
                ClosePrinter(printer);
            }
        }, cancellationToken);
    }

    public Task<IReadOnlyList<PrintPrinter>> GetPrintersAsync(CancellationToken token)
    {
        return Task.Run<IReadOnlyList<PrintPrinter>>(() =>
        {
            token.ThrowIfCancellationRequested();
            var discovered = EnumPrintersW(6, null, 4, IntPtr.Zero, 0, out var needed, out _);
            if (needed == 0)
            {
                var error = Marshal.GetLastPInvokeError();
                if (discovered == 0 && error != 0) throw Error("无法读取 Windows 打印队列", error);
                return [];
            }

            var buffer = Marshal.AllocHGlobal(checked((int)needed));
            try
            {
                if (EnumPrintersW(6, null, 4, buffer, needed, out _, out var count) == 0)
                    throw Error("无法读取 Windows 打印队列");
                uint defaultSize = 0;
                GetDefaultPrinterW(IntPtr.Zero, ref defaultSize);
                string? preferred = null;
                if (defaultSize > 0)
                {
                    var name = Marshal.AllocHGlobal(checked((int)defaultSize * 2));
                    try
                    {
                        if (GetDefaultPrinterW(name, ref defaultSize) != 0) preferred = Marshal.PtrToStringUni(name);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(name);
                    }
                }

                var result = new List<PrintPrinter>();
                for (var i = 0; i < count; i++)
                {
                    var printer = Marshal.PtrToStructure<PrinterInfo4>(buffer + i * Marshal.SizeOf<PrinterInfo4>());
                    var name = Marshal.PtrToStringUni(printer.Name)!;
                    result.Add(new PrintPrinter(name, name,
                        string.Equals(name, preferred, StringComparison.OrdinalIgnoreCase)));
                }

                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }, token);
    }

    public Task<PrinterCapabilities> GetCapabilitiesAsync(string printerId, CancellationToken token)
    {
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var port = GetPort(printerId);
            var count = DeviceCapabilitiesW(printerId, port, 2, IntPtr.Zero, IntPtr.Zero);
            if (count <= 0 || count > 4096) throw Error("无法读取打印机纸张能力");
            var ids = Marshal.AllocHGlobal(count * 2);
            var sizes = Marshal.AllocHGlobal(count * 8);
            var names = Marshal.AllocHGlobal(count * 128);
            try
            {
                if (DeviceCapabilitiesW(printerId, port, 2, ids, IntPtr.Zero) != count ||
                    DeviceCapabilitiesW(printerId, port, 3, sizes, IntPtr.Zero) != count
                    || DeviceCapabilitiesW(printerId, port, 16, names, IntPtr.Zero) != count)
                    throw Error("打印机纸张列表发生变化，请刷新");
                var papers = new List<PrintPaper>();
                for (var i = 0; i < count; i++)
                {
                    var width = Marshal.ReadInt32(sizes, i * 8) / 10d;
                    var height = Marshal.ReadInt32(sizes, i * 8 + 4) / 10d;
                    if (width <= 0 || height <= 0) continue;
                    var name = (Marshal.PtrToStringUni(names + i * 128, 64) ?? "").TrimEnd('\0');
                    papers.Add(new PrintPaper(name, width, height,
                        ((ushort)Marshal.ReadInt16(ids, i * 2)).ToString(CultureInfo.InvariantCulture)));
                }

                if (papers.Count == 0) throw new PrintException("打印机没有报告有效纸张。");
                return new PrinterCapabilities(papers,
                    DeviceCapabilitiesW(printerId, port, 32, IntPtr.Zero, IntPtr.Zero) == 1,
                    DeviceCapabilitiesW(printerId, port, 7, IntPtr.Zero, IntPtr.Zero) == 1, PrintMargins.Zero);
            }
            finally
            {
                Marshal.FreeHGlobal(ids);
                Marshal.FreeHGlobal(sizes);
                Marshal.FreeHGlobal(names);
            }
        }, token);
    }

    public Task<PrintResult> PrintAsync(PrintDocument document, PrintSettings settings,
        PrinterCapabilities capabilities, CancellationToken token)
    {
        return Task.Run(() => Submit(document, settings, token), token);
    }

    private static string GetPort(string printerId)
    {
        if (OpenPrinterW(printerId, out var printer, IntPtr.Zero) == 0) throw Error("无法打开打印机");
        var buffer = IntPtr.Zero;
        try
        {
            GetPrinterW(printer, 5, IntPtr.Zero, 0, out var size);
            if (size == 0) throw Error("无法读取打印机端口");
            buffer = Marshal.AllocHGlobal(checked((int)size));
            if (GetPrinterW(printer, 5, buffer, size, out _) == 0) throw Error("无法读取打印机端口");
            return Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer, IntPtr.Size)) ??
                   throw new PrintException("打印机未报告端口。");
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            ClosePrinter(printer);
        }
    }

    private static IntPtr ConfigureMode(IntPtr printer, PrintSettings settings)
    {
        var length = DocumentPropertiesW(IntPtr.Zero, printer, settings.PrinterId!, IntPtr.Zero, IntPtr.Zero, 0);
        if (length < 220) throw Error("打印驱动未提供有效 DEVMODE");
        var mode = Marshal.AllocHGlobal(length);
        try
        {
            if (DocumentPropertiesW(IntPtr.Zero, printer, settings.PrinterId!, mode, IntPtr.Zero, 2) != 1)
                throw Error("无法读取打印驱动设置");
            var fields = Marshal.ReadInt32(mode, 72) | 1 | 16 | 256 | 2048 | 4096 | 32768;
            Marshal.WriteInt16(mode, 76, (short)(settings.Orientation == PrintOrientation.Landscape ? 2 : 1));
            if (short.TryParse(settings.Paper.NativeId, out var paper))
            {
                fields |= 2;
                fields &= ~12;
                Marshal.WriteInt16(mode, 78, paper);
            }
            else
            {
                fields |= 2 | 4 | 8;
                Marshal.WriteInt16(mode, 78, 256);
                Marshal.WriteInt16(mode, 80, checked((short)Math.Round(settings.Paper.HeightMm * 10)));
                Marshal.WriteInt16(mode, 82, checked((short)Math.Round(settings.Paper.WidthMm * 10)));
            }

            Marshal.WriteInt16(mode, 84, 100);
            Marshal.WriteInt16(mode, 86, 1);
            Marshal.WriteInt16(mode, 92, (short)(settings.Color == PrintColor.Color ? 2 : 1));
            var duplex = (short)(settings.Duplex == PrintDuplex.LongEdge ? 2 :
                settings.Duplex == PrintDuplex.ShortEdge ? 3 : 1);
            Marshal.WriteInt16(mode, 94, duplex);
            Marshal.WriteInt16(mode, 100, 0);
            Marshal.WriteInt32(mode, 72, fields);
            if (DocumentPropertiesW(IntPtr.Zero, printer, settings.PrinterId!, mode, mode, 10) != 1)
                throw Error("打印驱动拒绝设置");
            var accepted = Marshal.ReadInt32(mode, 72);
            if (((accepted & 1) != 0 && Marshal.ReadInt16(mode, 76) !=
                    (settings.Orientation == PrintOrientation.Landscape ? 2 : 1))
                || ((accepted & 4096) != 0 && Marshal.ReadInt16(mode, 94) != duplex) ||
                ((accepted & 4096) == 0 && duplex != 1)
                || ((accepted & 256) != 0 && Marshal.ReadInt16(mode, 86) != 1) ||
                ((accepted & 16) != 0 && Marshal.ReadInt16(mode, 84) != 100)
                || ((accepted & 2048) != 0 && settings.Color == PrintColor.Color && Marshal.ReadInt16(mode, 92) != 2))
                throw new PrintException("打印驱动更改了方向、双面、份数或比例设置，请重新选择。");
            return mode;
        }
        catch
        {
            Marshal.FreeHGlobal(mode);
            throw;
        }
    }

    private static PrintResult Submit(PrintDocument document, PrintSettings settings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (OpenPrinterW(settings.PrinterId!, out var printer, IntPtr.Zero) == 0) throw Error("无法打开打印机");
        IntPtr mode = IntPtr.Zero, dc = IntPtr.Zero, title = IntPtr.Zero;
        var started = false;
        var job = 0;
        try
        {
            mode = ConfigureMode(printer, settings);
            dc = CreateDCW("WINSPOOL", settings.PrinterId!, null, mode);
            if (dc == IntPtr.Zero) throw Error("无法创建打印设备上下文");
            var dx = GetDeviceCaps(dc, 88);
            var dy = GetDeviceCaps(dc, 90);
            var pw = GetDeviceCaps(dc, 110);
            var ph = GetDeviceCaps(dc, 111);
            var ox = GetDeviceCaps(dc, 112);
            var oy = GetDeviceCaps(dc, 113);
            if (dx <= 0 || dy <= 0 || pw <= 0 || ph <= 0) throw new PrintException("打印设备分辨率或纸张尺寸无效。");
            var (width, height) = PrintLayout.Size(settings);
            if (Math.Abs(pw * 72d / dx - width) > 6 || Math.Abs(ph * 72d / dy - height) > 6)
                throw new PrintException("驱动实际纸张与预览不一致，请检查纸张设置。");
            var hard = new PrintMargins(ox * 25.4 / dx, oy * 25.4 / dy,
                Math.Max(0, pw - ox - GetDeviceCaps(dc, 8)) * 25.4 / dx,
                Math.Max(0, ph - oy - GetDeviceCaps(dc, 10)) * 25.4 / dy);
            title = Marshal.StringToHGlobalUni(document.Title);
            var info = new DocumentInfo { Size = Marshal.SizeOf<DocumentInfo>(), Name = title };
            token.ThrowIfCancellationRequested();
            job = StartDocW(dc, ref info);
            if (job <= 0) throw Error("无法启动打印任务");
            started = true;
            var dpi = settings.RasterDpi;
            var rasterWidth = checked((int)Math.Ceiling(width * dpi / 72));
            var rasterHeight = checked((int)Math.Ceiling(height * dpi / 72));
            // Bound native pixel memory to about 16 MiB, even for large custom sheets.
            var bandHeight = Math.Clamp(4 * 1024 * 1024 / rasterWidth, 16, 512);
            SetStretchBltMode(dc, 4);
            foreach (var page in PrintLayout.OutputPages(document, settings))
            {
                token.ThrowIfCancellationRequested();
                if (StartPage(dc) <= 0) throw Error("无法开始打印页");
                for (var y = 0; y < rasterHeight; y += bandHeight)
                {
                    token.ThrowIfCancellationRequested();
                    var rows = Math.Min(bandHeight, rasterHeight - y);
                    using var bitmap =
                        new SKBitmap(new SKImageInfo(rasterWidth, rows, SKColorType.Bgra8888, SKAlphaType.Premul));
                    using (var canvas = new SKCanvas(bitmap))
                    {
                        canvas.Translate(0, -y);
                        canvas.Scale(dpi / 72f);
                        PrintLayout.DrawSheet(canvas, document, settings, page, hard);
                    }

                    var header = new BitmapInfo
                        { Size = 40, Width = rasterWidth, Height = -rows, Planes = 1, BitCount = 32 };
                    var top = (int)Math.Round(y * (double)ph / rasterHeight);
                    var bottom = (int)Math.Round((y + rows) * (double)ph / rasterHeight);
                    if (StretchDIBits(dc, -ox, top - oy, pw, bottom - top, 0, 0, rasterWidth, rows, bitmap.GetPixels(),
                            ref header, 0, 0x00CC0020) == -1)
                        throw Error("打印驱动无法接收页面图像");
                }

                if (EndPage(dc) <= 0) throw Error("无法结束打印页");
            }

            if (EndDoc(dc) <= 0) throw Error("无法结束打印任务");
            started = false;
            return new PrintResult(job.ToString(CultureInfo.InvariantCulture), $"已提交到 Windows 打印队列，任务 {job}；尚未确认纸张输出。");
        }
        catch (OperationCanceledException) when (started)
        {
            AbortDoc(dc);
            started = false;
            return new PrintResult(job.ToString(CultureInfo.InvariantCulture), "已请求取消剩余页面；打印机可能已输出部分页面。", true);
        }
        catch (Exception e) when (started)
        {
            throw new PrintException($"Windows 任务 {job} 提交中断，可能已输出部分页面；请先检查队列，避免重复提交。{e.Message}", e);
        }
        finally
        {
            if (started) AbortDoc(dc);
            if (dc != IntPtr.Zero) DeleteDC(dc);
            if (title != IntPtr.Zero) Marshal.FreeHGlobal(title);
            if (mode != IntPtr.Zero) Marshal.FreeHGlobal(mode);
            ClosePrinter(printer);
        }
    }

    private static PrintException Error(string message, int? code = null)
    {
        return new PrintException(message + "：" + new Win32Exception(code ?? Marshal.GetLastPInvokeError()).Message);
    }

    [LibraryImport("winspool.drv", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int EnumPrintersW(uint flags, string? name, uint level, IntPtr buffer, uint size,
        out uint needed, out uint returned);

    [LibraryImport("winspool.drv", SetLastError = true)]
    private static partial int GetDefaultPrinterW(IntPtr buffer, ref uint size);

    [LibraryImport("winspool.drv", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int OpenPrinterW(string printer, out IntPtr handle, IntPtr defaults);

    [LibraryImport("winspool.drv", SetLastError = true)]
    private static partial int ClosePrinter(IntPtr printer);

    [LibraryImport("winspool.drv", SetLastError = true)]
    private static partial int GetPrinterW(IntPtr printer, uint level, IntPtr buffer, uint size, out uint needed);

    [LibraryImport("winspool.drv", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int DocumentPropertiesW(IntPtr window, IntPtr printer, string name, IntPtr output,
        IntPtr input, uint mode);

    [LibraryImport("winspool.drv", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int DeviceCapabilitiesW(string name, string? port, short capability, IntPtr output,
        IntPtr mode);

    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr CreateDCW(string driver, string device, string? output, IntPtr mode);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDeviceCaps(IntPtr dc, int index);

    [LibraryImport("gdi32.dll")]
    private static partial int DeleteDC(IntPtr dc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial int StartDocW(IntPtr dc, ref DocumentInfo info);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial int StartPage(IntPtr dc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial int EndPage(IntPtr dc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial int EndDoc(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    private static partial int AbortDoc(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    private static partial int SetStretchBltMode(IntPtr dc, int mode);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial int StretchDIBits(IntPtr dc, int x, int y, int w, int h, int sx, int sy, int sw, int sh,
        IntPtr bits, ref BitmapInfo info, uint usage, uint operation);

    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterInfo4
    {
        public IntPtr Name, Server;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DocumentInfo
    {
        public int Size;
        public IntPtr Name, Output, DataType;
        public uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, ImageSize;
        public int XPels, YPels;
        public uint Used, Important;
    }
}
