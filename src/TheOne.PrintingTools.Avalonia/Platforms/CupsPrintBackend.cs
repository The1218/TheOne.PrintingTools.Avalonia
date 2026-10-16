using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace TheOne.PrintingTools.Avalonia.Platforms;

internal partial class CupsPrintBackend : IPrintBackend
{
    internal CupsPrintBackend() => NativeLibraries.Initialize();
    public Task<IReadOnlyList<PrintPrinter>> GetPrintersAsync(CancellationToken token) => Task.Run<IReadOnlyList<PrintPrinter>>(() =>
    {
        token.ThrowIfCancellationRequested();
        var count = cupsGetDests2(IntPtr.Zero, out var destinations);
        try
        {
            if (count == 0 && cupsLastError() >= 0x400 && cupsLastError() != 0x406) throw new PrintException("无法读取系统打印队列：" + Text(cupsLastErrorString()));
            var result = new List<PrintPrinter>();
            for (var i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                var dest = Marshal.PtrToStructure<CupsDestination>(destinations + i * Marshal.SizeOf<CupsDestination>());
                if (dest.Instance != IntPtr.Zero) continue;
                var id = Text(dest.Name); var name = Text(cupsGetOption("printer-info", dest.Count, dest.Options));
                var state = Text(cupsGetOption("printer-state", dest.Count, dest.Options));
                result.Add(new(id, string.IsNullOrEmpty(name) ? id : name, dest.IsDefault != 0,
                    state == "5" ? "已暂停" : state == "4" ? "忙碌" : "系统队列"));
            }
            return result;
        }
        finally { if (destinations != IntPtr.Zero) cupsFreeDests(count, destinations); }
    }, token);

    public Task<PrinterCapabilities> GetCapabilitiesAsync(string printerId, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var destination = cupsGetNamedDest(IntPtr.Zero, printerId, null);
        if (destination == IntPtr.Zero) throw new PrintException("打印机不存在或无法访问：" + printerId);
        IntPtr http = IntPtr.Zero, info = IntPtr.Zero;
        try
        {
            unsafe
            {
                byte* resource = stackalloc byte[1024];
                http = cupsConnectDest(destination, 0, 5000, IntPtr.Zero, (IntPtr)resource, 1024, IntPtr.Zero, IntPtr.Zero);
            }
            token.ThrowIfCancellationRequested();
            if (http != IntPtr.Zero) httpSetTimeout(http, 5, IntPtr.Zero, IntPtr.Zero);
            if (http == IntPtr.Zero || (info = cupsCopyDestInfo(http, destination)) == IntPtr.Zero)
                throw new PrintException("无法读取打印机能力，请检查设备连接：" + Text(cupsLastErrorString()));
            var papers = new List<PrintPaper>();
            var margins = PrintMargins.Zero;
            var mediaCount = Math.Min(2048, cupsGetDestMediaCount(http, destination, info, 0));
            for (var i = 0; i < mediaCount; i++)
            {
                if (cupsGetDestMediaByIndex(http, destination, info, i, 0, out var media) != 0 && media.Width > 0 && media.Length > 0)
                {
                    var name = media.MediaName;
                    var display = (media.Width, media.Length) switch
                    { (21000, 29700) => "A4", (14800, 21000) => "A5", (29700, 42000) => "A3", (21590, 27940) => "Letter", (21590, 35560) => "Legal", _ => name };
                    var parts = name.Split('_');
                    if (parts.Length >= 3) display = parts[0] switch
                    { "iso" => parts[1].ToUpperInvariant(), "na" => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(parts[1]), "jis" => "JIS " + parts[1].ToUpperInvariant(), "custom" => "自定义", _ => display };
                    papers.Add(new(display, media.Width / 100d, media.Length / 100d, name)
                    { PrintableMargins = new(media.Left / 100d, media.Top / 100d, media.Right / 100d, media.Bottom / 100d) });
                }
            }
            if (cupsGetDestMediaDefault(http, destination, info, 0, out var preferred) != 0)
            {
                margins = new(preferred.Left / 100d, preferred.Top / 100d, preferred.Right / 100d, preferred.Bottom / 100d);
                papers = papers.OrderByDescending(p => p.NativeId == preferred.MediaName).ToList();
            }
            var colors = Strings(cupsFindDestSupported(http, destination, info, "print-color-mode"));
            var sides = Strings(cupsFindDestSupported(http, destination, info, "sides"));
            return new PrinterCapabilities(papers.Count > 0 ? papers : [PrintPaper.A4, PrintPaper.Letter],
                colors.Contains("color"), sides.Any(x => x.StartsWith("two-sided", StringComparison.Ordinal)), margins, papers.Count == 0)
            {
                DuplexModes = new[] { PrintDuplex.Simplex }.Concat(sides.Contains("two-sided-long-edge") ? new[] { PrintDuplex.LongEdge } : [])
                    .Concat(sides.Contains("two-sided-short-edge") ? new[] { PrintDuplex.ShortEdge } : []).ToArray()
            };
        }
        finally
        {
            if (info != IntPtr.Zero) cupsFreeDestInfo(info);
            if (http != IntPtr.Zero) httpClose(http);
            cupsFreeDests(1, destination);
        }
    }, token);

    public virtual Task<PrintResult> PrintAsync(PrintDocument document, PrintSettings settings, PrinterCapabilities capabilities, CancellationToken token)
        => PrintRendering.WithTemporaryPdfAsync(document, settings, PageMargins(settings, capabilities),
            path => Task.Run(() => Submit(path, document.Title, settings, token), token), token);

    protected static PrintMargins PageMargins(PrintSettings s, PrinterCapabilities c)
    {
        var m = s.Paper.PrintableMargins ?? c.HardMargins;
        return s.Orientation == PrintOrientation.Landscape ? new(m.Top, m.Right, m.Bottom, m.Left) : m;
    }

    private static PrintResult Submit(string path, string title, PrintSettings settings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        IntPtr options = IntPtr.Zero, connection = IntPtr.Zero; var count = 0;
        void Add(string key, string value) => count = cupsAddOption(key, value, count, ref options);
        try
        {
            // Page selection, scaling, copies and collation are already baked into the PDF exactly once.
            Add("copies", "1"); Add("print-scaling", "none"); Add("fit-to-page", "false");
            Add("media", settings.Paper.NativeId ?? $"Custom.{settings.Paper.WidthMm.ToString("0.##", CultureInfo.InvariantCulture)}x{settings.Paper.HeightMm.ToString("0.##", CultureInfo.InvariantCulture)}mm");
            Add("sides", settings.Duplex switch { PrintDuplex.LongEdge => "two-sided-long-edge", PrintDuplex.ShortEdge => "two-sided-short-edge", _ => "one-sided" });
            Add("print-color-mode", settings.Color == PrintColor.Color ? "color" : "monochrome");
            Add("orientation-requested", settings.Orientation == PrintOrientation.Landscape ? "4" : "3");
            token.ThrowIfCancellationRequested();
            connection = httpConnect2(Text(cupsServer()), ippPort(), IntPtr.Zero, 0, cupsEncryption(), 1, 5000, IntPtr.Zero);
            if (connection == IntPtr.Zero) throw new PrintException("无法连接系统打印服务：" + Text(cupsLastErrorString()));
            httpSetTimeout(connection, 30, IntPtr.Zero, IntPtr.Zero);
            token.ThrowIfCancellationRequested();
            var id = cupsPrintFile2(connection, settings.PrinterId!, path, title, count, options);
            if (id <= 0) throw new PrintException("提交 CUPS 打印任务失败：" + Text(cupsLastErrorString()));
            // Do not turn an accepted job into a cancelled task: callers must not retry an uncertain submission.
            return new(id.ToString(CultureInfo.InvariantCulture), "已提交到系统打印队列，任务 " + id + "；尚未确认纸张输出。");
        }
        finally { if (options != IntPtr.Zero) cupsFreeOptions(count, options); if (connection != IntPtr.Zero) httpClose(connection); }
    }

    private static List<string> Strings(IntPtr attribute)
    {
        var result = new List<string>();
        if (attribute != IntPtr.Zero) for (var i = 0; i < ippGetCount(attribute); i++) result.Add(Text(ippGetString(attribute, i, IntPtr.Zero)));
        return result;
    }
    private static string Text(IntPtr value) => Marshal.PtrToStringUTF8(value) ?? "";
    [StructLayout(LayoutKind.Sequential)]
    private struct CupsDestination { public IntPtr Name, Instance; public int IsDefault, Count; public IntPtr Options; }
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct CupsMedia
    {
        public fixed byte Media[128];
        public int Width, Length, Bottom, Left, Right, Top;
        public string MediaName { get { fixed (byte* value = Media) return Encoding.UTF8.GetString(value, new ReadOnlySpan<byte>(value, 128).IndexOf((byte)0) is var end && end >= 0 ? end : 128); } }
    }
    [LibraryImport("theone-cups")] private static partial int cupsGetDests2(IntPtr http, out IntPtr destinations);
    [LibraryImport("theone-cups")] private static partial void cupsFreeDests(int count, IntPtr destinations);
    [LibraryImport("theone-cups", StringMarshalling = StringMarshalling.Utf8)] private static partial IntPtr cupsGetNamedDest(IntPtr http, string name, string? instance);
    [LibraryImport("theone-cups", StringMarshalling = StringMarshalling.Utf8)] private static partial IntPtr cupsGetOption(string name, int count, IntPtr options);
    [LibraryImport("theone-cups")] private static partial IntPtr cupsConnectDest(IntPtr destination, uint flags, int msec, IntPtr cancel, IntPtr resource, nuint size, IntPtr callback, IntPtr data);
    [LibraryImport("theone-cups")] private static partial IntPtr cupsCopyDestInfo(IntPtr http, IntPtr destination);
    [LibraryImport("theone-cups")] private static partial void cupsFreeDestInfo(IntPtr info);
    [LibraryImport("theone-cups")] private static partial void httpClose(IntPtr http);
    [LibraryImport("theone-cups")] private static partial void httpSetTimeout(IntPtr http, double seconds, IntPtr callback, IntPtr data);
    [LibraryImport("theone-cups", StringMarshalling = StringMarshalling.Utf8)] private static partial IntPtr httpConnect2(string host, int port, IntPtr addresses, int family, int encryption, int blocking, int milliseconds, IntPtr cancel);
    [LibraryImport("theone-cups")] private static partial IntPtr cupsServer();
    [LibraryImport("theone-cups")] private static partial int cupsEncryption();
    [LibraryImport("theone-cups")] private static partial int ippPort();
    [LibraryImport("theone-cups")] private static partial int cupsGetDestMediaCount(IntPtr http, IntPtr destination, IntPtr info, uint flags);
    [LibraryImport("theone-cups")] private static partial int cupsGetDestMediaByIndex(IntPtr http, IntPtr destination, IntPtr info, int index, uint flags, out CupsMedia media);
    [LibraryImport("theone-cups")] private static partial int cupsGetDestMediaDefault(IntPtr http, IntPtr destination, IntPtr info, uint flags, out CupsMedia media);
    [LibraryImport("theone-cups", StringMarshalling = StringMarshalling.Utf8)] private static partial IntPtr cupsFindDestSupported(IntPtr http, IntPtr destination, IntPtr info, string option);
    [LibraryImport("theone-cups")] private static partial int ippGetCount(IntPtr attribute);
    [LibraryImport("theone-cups")] private static partial IntPtr ippGetString(IntPtr attribute, int index, IntPtr language);
    [LibraryImport("theone-cups", StringMarshalling = StringMarshalling.Utf8)] private static partial int cupsAddOption(string name, string value, int count, ref IntPtr options);
    [LibraryImport("theone-cups")] private static partial void cupsFreeOptions(int count, IntPtr options);
    [LibraryImport("theone-cups", StringMarshalling = StringMarshalling.Utf8)] private static partial int cupsPrintFile2(IntPtr http, string name, string file, string title, int count, IntPtr options);
    [LibraryImport("theone-cups")] private static partial int cupsLastError();
    [LibraryImport("theone-cups")] private static partial IntPtr cupsLastErrorString();
}
