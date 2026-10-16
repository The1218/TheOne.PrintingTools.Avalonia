using global::Avalonia.Controls;
using global::Avalonia.Controls.ApplicationLifetimes;
using global::Avalonia.Threading;

namespace TheOne.PrintingTools.Avalonia;

/// <summary>Optional backend injection; ordinary applications can use Printing directly.</summary>
public interface IPrintBackend
{
    Task<IReadOnlyList<PrintPrinter>> GetPrintersAsync(CancellationToken cancellationToken);
    Task<PrinterCapabilities> GetCapabilitiesAsync(string printerId, CancellationToken cancellationToken);
    Task<PrintResult> PrintAsync(PrintDocument document, PrintSettings settings, PrinterCapabilities capabilities, CancellationToken cancellationToken);
}

internal interface IPageMarginsBackend
{
    Task<PrintMargins> GetPageMarginsAsync(PrintSettings settings, CancellationToken cancellationToken);
}

/// <summary>Application-facing printing API. Register an instance in the host's DI container if desired.</summary>
public interface IPrintService
{
    Task<PrintResult> PrintAsync(PrintSource source, PrintSettings? options = null, bool showPreview = true, CancellationToken cancellationToken = default);
}

public sealed class PrintService : IPrintService
{
    private readonly Lazy<IPrintBackend> _backend;
    private readonly Func<Window?>? _ownerProvider;
    public PrintService(Func<Window?>? ownerProvider = null) => (_backend, _ownerProvider) = (new(CreateBackend), ownerProvider);
    public PrintService(IPrintBackend backend, Func<Window?>? ownerProvider = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        (_backend, _ownerProvider) = (new(() => backend), ownerProvider);
    }

    /// <summary>Prints any supported source. Preview is enabled by default. Without preview, PrinterId
    /// is used when supplied; otherwise the system default printer is required. Owns temporary snapshots only.</summary>
    public async Task<PrintResult> PrintAsync(PrintSource source, PrintSettings? options = null, bool showPreview = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (showPreview)
        {
            if (!Dispatcher.UIThread.CheckAccess())
                return await Dispatcher.UIThread.InvokeAsync(() => PrintAsync(source, options, true, cancellationToken));
            var desktop = global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var owner = _ownerProvider is not null ? _ownerProvider() : desktop?.Windows.FirstOrDefault(w => w.IsActive) ?? desktop?.MainWindow;
            if (owner?.IsVisible != true) throw new PrintException("预览需要可见的所属窗口；请在窗口打开后调用，或创建 new PrintService(() => ownerWindow)。");
            using var document = await source.CreateDocumentAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return await ShowDialogAsync(owner, document, options ?? new PrintSettings { Paper = document.Paper }, cancellationToken);
        }
        using var directDocument = await source.CreateDocumentAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var settings = options ?? new PrintSettings { Paper = directDocument.Paper };
        if (string.IsNullOrWhiteSpace(settings.PrinterId))
        {
            var printers = await GetPrintersAsync(cancellationToken).ConfigureAwait(false);
            var printer = printers.FirstOrDefault(p => p.IsDefault)
                ?? throw new PrintException("没有可用的系统默认打印机，请指定 options.PrinterId 或启用预览选择设备。");
            settings = settings with { PrinterId = printer.Id };
        }
        return await SubmitAsync(directDocument, settings, cancellationToken).ConfigureAwait(false);
    }
    private static IPrintBackend CreateBackend()
    {
        if (OperatingSystem.IsWindows()) return new Platforms.WindowsPrintBackend();
        if (OperatingSystem.IsMacOS()) return new Platforms.MacPrintBackend();
        if (OperatingSystem.IsLinux()) return new Platforms.CupsPrintBackend();
        throw new PlatformNotSupportedException("仅支持 Windows、macOS 和 Linux 桌面打印。");
    }
    public Task<IReadOnlyList<PrintPrinter>> GetPrintersAsync(CancellationToken cancellationToken = default)
        => _backend.Value.GetPrintersAsync(cancellationToken);
    public Task<PrinterCapabilities> GetCapabilitiesAsync(string printerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        return _backend.Value.GetCapabilitiesAsync(printerId, cancellationToken);
    }
    /// <summary>Reads the actual hardware margins for the selected sheet and orientation.</summary>
    public async Task<PrintMargins> GetPageMarginsAsync(PrintSettings settings, PrinterCapabilities capabilities, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings); ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.PrinterId); PrintLayout.ValidatePaper(settings.Paper);
        if (_backend.Value is IPageMarginsBackend specific)
            return await specific.GetPageMarginsAsync(settings, cancellationToken).ConfigureAwait(false);
        var m = settings.Paper.PrintableMargins ?? capabilities.HardMargins;
        return settings.Orientation == PrintOrientation.Landscape ? new(m.Top, m.Right, m.Bottom, m.Left) : m;
    }
    /// <summary>Low-level submission of a caller-owned snapshot. Requires an explicit PrinterId and never opens preview.</summary>
    public async Task<PrintResult> SubmitAsync(PrintDocument document, PrintSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(settings);
        PrintLayout.Validate(document, settings); cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(settings.PrinterId)) throw new ArgumentException("请明确选择打印机。", nameof(settings));
        var capabilities = await GetCapabilitiesAsync(settings.PrinterId, cancellationToken).ConfigureAwait(false);
        var media = capabilities.Papers.FirstOrDefault(p => p.NativeId == settings.Paper.NativeId && Math.Abs(p.WidthMm - settings.Paper.WidthMm) < 1 && Math.Abs(p.HeightMm - settings.Paper.HeightMm) < 1)
            ?? capabilities.Papers.FirstOrDefault(p => Math.Abs(p.WidthMm - settings.Paper.WidthMm) < 1 && Math.Abs(p.HeightMm - settings.Paper.HeightMm) < 1);
        if (media is not null) settings = settings with { Paper = media };
        if (settings.Color == PrintColor.Color && !capabilities.SupportsColor) throw new PrintException("打印机未报告彩色支持，请选择黑白打印。");
        if (!capabilities.DuplexModes.Contains(settings.Duplex)) throw new PrintException("打印机未报告此双面方式的支持，请重新选择。");
        if (!capabilities.IsEstimated && !capabilities.Papers.Any(p => Math.Abs(p.WidthMm - settings.Paper.WidthMm) < 1 && Math.Abs(p.HeightMm - settings.Paper.HeightMm) < 1))
            throw new PrintException("所选打印机不支持此纸张，请重新选择。");
        return await _backend.Value.PrintAsync(document, settings, capabilities, cancellationToken).ConfigureAwait(false);
    }
    /// <summary>Exports selected pages once, without requiring a printer. The destination is left open.</summary>
    public Task ExportPdfAsync(PrintDocument document, Stream destination, PrintSettings? settings = null, CancellationToken cancellationToken = default)
        => PrintRendering.ExportPdfAsync(document, destination, (settings ?? new PrintSettings { Paper = document.Paper }) with { Copies = 1, Duplex = PrintDuplex.Simplex }, PrintMargins.Zero, cancellationToken);

    public Task<PrintResult> ShowDialogAsync(Window owner, PrintDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        return ShowDialogAsync(owner, document, new PrintSettings { Paper = document.Paper }, cancellationToken);
    }

    public async Task<PrintResult> ShowDialogAsync(Window owner, PrintDocument document, PrintSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(settings);
        PrintLayout.Validate(document, settings);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(() => ShowDialogAsync(owner, document, settings, cancellationToken));
        var dialog = new PrintPreviewWindow(document, this, settings);
        using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(dialog.Cancel));
        var result = await dialog.ShowDialog<PrintResult?>(owner);
        if (result is null || result.IsCancelled) cancellationToken.ThrowIfCancellationRequested();
        return result ?? PrintResult.Cancelled;
    }
}

/// <summary>Zero-registration entry point. Native libraries are loaded only for the active OS.</summary>
public static class Printing
{
    public static PrintService Default { get; } = new();
    public static Task<PrintResult> ShowDialogAsync(Window owner, PrintDocument document, CancellationToken cancellationToken = default)
        => Default.ShowDialogAsync(owner, document, cancellationToken);
    public static Task<PrintResult> PrintAsync(PrintSource source, PrintSettings? options = null, bool showPreview = true, CancellationToken cancellationToken = default)
        => Default.PrintAsync(source, options, showPreview, cancellationToken);
    public static Task<PrintResult> SubmitAsync(PrintDocument document, PrintSettings settings, CancellationToken cancellationToken = default)
        => Default.SubmitAsync(document, settings, cancellationToken);
    public static Task ExportPdfAsync(PrintDocument document, Stream destination, PrintSettings? settings = null, CancellationToken cancellationToken = default)
        => Default.ExportPdfAsync(document, destination, settings, cancellationToken);
}
