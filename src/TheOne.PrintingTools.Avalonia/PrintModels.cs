namespace TheOne.PrintingTools.Avalonia;

public enum PrintOrientation { Portrait, Landscape }
public enum PrintColor { Monochrome, Color }
public enum PrintDuplex { Simplex, LongEdge, ShortEdge }
public enum PrintScaleMode { Fit, ActualSize, Custom }
public enum PrintDensity { Small, Normal, Large }
public enum PrintImageLayout { OnePerPage, TwoRows, TwoColumns, Grid2x2, Grid3x3 }
public enum PrintImageFill { Fit, Fill, Stretch, ActualSize }
public enum PrintImageOrder { AcrossThenDown, DownThenAcross }
public enum PrintImageAlignment { TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight }

/// <summary>Image-only pagination and placement. Dimensions are millimetres; actual size assumes 96 DPI.</summary>
public sealed record PrintImageOptions
{
    public PrintImageLayout Layout { get; init; }
    public PrintImageFill Fill { get; init; }
    public PrintImageOrder Order { get; init; }
    public PrintImageAlignment Alignment { get; init; } = PrintImageAlignment.Center;
    public double SpacingMm { get; init; } = 4;
    public bool AutoRotate { get; init; }
}

/// <summary>Physical sheet dimensions in millimetres. NativeId identifies the driver's media.</summary>
public sealed record PrintPaper(string Name, double WidthMm, double HeightMm, string? NativeId = null)
{
    public PrintMargins? PrintableMargins { get; init; }
    public static PrintPaper A4 { get; } = new("A4", 210, 297);
    public static PrintPaper Letter { get; } = new("Letter", 215.9, 279.4);
    public override string ToString() => $"{Name} ({WidthMm:0.#} × {HeightMm:0.#} mm)";
}

public sealed record PrintMargins(double Left, double Top, double Right, double Bottom)
{
    public static PrintMargins Default { get; } = new(10, 10, 10, 10);
    public static PrintMargins Zero { get; } = new(0, 0, 0, 0);
}

public sealed record PrintSettings
{
    public string? PrinterId { get; init; }
    /// <summary>One-based inclusive ranges, for example 1-3,5. Empty means all pages.</summary>
    public string PageRange { get; init; } = "";
    public int Copies { get; init; } = 1;
    public bool Collate { get; init; } = true;
    public PrintPaper Paper { get; init; } = PrintPaper.A4;
    public PrintOrientation Orientation { get; init; }
    public PrintMargins Margins { get; init; } = PrintMargins.Default;
    public PrintScaleMode ScaleMode { get; init; } = PrintScaleMode.Fit;
    public double ScalePercent { get; init; } = 100;
    public PrintColor Color { get; init; } = PrintColor.Monochrome;
    public PrintDuplex Duplex { get; init; }
    public int RasterDpi { get; init; } = 300;
    public PrintImageOptions ImageOptions { get; init; } = new();
}

public sealed record PrintPrinter(string Id, string Name, bool IsDefault = false, string Status = "系统队列")
{
    public override string ToString() => Name;
}

public sealed record PrinterCapabilities(IReadOnlyList<PrintPaper> Papers, bool SupportsColor,
    bool SupportsDuplex, PrintMargins HardMargins, bool IsEstimated = false)
{
    public IReadOnlyList<PrintDuplex> DuplexModes { get; init; } = SupportsDuplex ? [PrintDuplex.Simplex, PrintDuplex.LongEdge, PrintDuplex.ShortEdge] : [PrintDuplex.Simplex];
}

public sealed record PrintResult(string? JobId, string Message, bool IsCancelled = false)
{
    public static PrintResult Cancelled { get; } = new(null, "已取消打印。", true);
}

public sealed class PrintException(string message, Exception? inner = null) : Exception(message, inner);

public static class PrintPageRange
{
    /// <summary>Returns sorted unique zero-based page indexes. Rejects invalid or out-of-range input.</summary>
    public static IReadOnlyList<int> Parse(string? range, int pageCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageCount, 1);
        if (string.IsNullOrWhiteSpace(range)) return Enumerable.Range(0, pageCount).ToArray();
        var pages = new SortedSet<int>();
        foreach (var part in range.Split(','))
        {
            var ends = part.Trim().Split('-');
            if (ends.Length is < 1 or > 2 || !int.TryParse(ends[0], out var first) || first < 1 || first > pageCount)
                throw new ArgumentException($"页码范围无效，请输入 1–{pageCount} 内的页码，例如 1-3,5。", nameof(range));
            var last = first;
            if (ends.Length == 2 && (!int.TryParse(ends[1], out last) || last < first || last > pageCount))
                throw new ArgumentException($"结束页必须位于 {first}–{pageCount}。", nameof(range));
            for (var i = first; i <= last; i++) pages.Add(i - 1);
        }
        return pages.ToArray();
    }
}
