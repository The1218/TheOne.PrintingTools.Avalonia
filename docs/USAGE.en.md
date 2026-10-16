# PrintingTools usage

Target: .NET 10 / Avalonia 12.1.4. The main API has three inputs: **source, optional settings, and whether to preview**.

As of 2026-10-16, [1.0.0-preview.3](https://www.nuget.org/packages/TheOne.PrintingTools.Avalonia/1.0.0-preview.3) is published and available for installation and restore from nuget.org.

## Start here

```csharp
using TheOne.PrintingTools.Avalonia;

await Printing.PrintAsync("report.pdf");                       // One PDF; preview by default.
await Printing.PrintAsync(new[] { "a.jpg", "b.png" });            // One or multiple images.
await Printing.PrintAsync("report.pdf", showPreview: false);    // Submit to the system default printer.
```

Add settings only when needed:

```csharp
await Printing.PrintAsync(
    filePath,
    options: new PrintSettings { Copies = 2, PageRange = "1-3" },
    showPreview: true);
```

Signature: `PrintAsync(PrintSource source, PrintSettings? options = null, bool showPreview = true, CancellationToken cancellationToken = default)`.

With preview enabled, the call waits for the user to print, cancel or close. It never submits automatically. Settings initialize the preview and the user can adjust them. File selection alone does not print. The returned `PrintResult` reports submission or cancellation.

With `showPreview: false`, the service submits directly without a dialog. `options.PrinterId` selects a device; otherwise a system default printer is required. It never silently picks the first queue. With no settings, direct printing uses the source paper, one copy, monochrome, simplex, collated output, fit scaling and 10 mm margins. A supplied settings object's default paper is A4.

## Registration and MVVM

**Service registration is optional.** Ordinary views can call the static entry point. No AppBuilder extension is required.

For preview UI, explicitly register the package theme once in App.axaml; direct printing does not need UI styles:

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:printing="using:TheOne.PrintingTools.Avalonia">
  <Application.Styles>
    <printing:PrintingToolsTheme />
  </Application.Styles>
</Application>
```

Preview resolves the active desktop window or main window. Call it after the window is visible. Multi-window hosts should supply an owner provider.

For MVVM, create one service in the application composition root and inject `IPrintService`:

```csharp
IPrintService printing = new PrintService(() => desktop.MainWindow);
var viewModel = new ReportViewModel(printing);

public sealed class ReportViewModel(IPrintService printing)
{
    public Task<PrintResult> PrintAsync(string[] files)
        => printing.PrintAsync(files, new PrintSettings { Copies = 2 });
}
```

Hosts already using Microsoft.Extensions.DependencyInjection may register
`services.AddSingleton<IPrintService>(_ => new PrintService(() => desktop.MainWindow));`
The package does not require a DI framework. Implement `IPrintService` to replace printing in ViewModel tests; inject `IPrintBackend` into `PrintService` to test the submission pipeline.

## Supported source parameters

All these convert implicitly to `PrintSource` and use the same method:

| Input | Behavior |
| --- | --- |
| `string` local path or HTTP/HTTPS URL | Load or download one PDF or image |
| `Uri` / `Uri[]` | One remote file or multiple remote images |
| `string[]` paths or URLs | Combine local/remote images in array order; PDF requires exactly one source |
| `Stream` image | Read one image at the current position |
| Avalonia `Visual` / `Control` | Capture an arranged visual on the UI thread |
| `PrintDocument` | Use an existing paginated document; caller retains ownership |
| `PrintSource[]` | Combine heterogeneous sources in array order; PDF cannot be mixed |

```csharp
await Printing.PrintAsync(imageStream);
await Printing.PrintAsync(reportControl);
await Printing.PrintAsync(existingDocument);

PrintSource[] sources = ["photo.jpg", reportControl, existingDocument];
await Printing.PrintAsync(sources);
```

Explicit factories allow stream formats, titles, other collections, and common paper:

```csharp
var pdf = PrintSource.FromStream(pdfStream, "report.pdf");
await Printing.PrintAsync(pdf);

var image = PrintSource.FromImage(imageStream, "Photo");
var files = PrintSource.FromFiles(filePathEnumerable);
var mixed = PrintSource.Combine(
    [image, PrintSource.FromControl(reportControl, "Form")],
    title: "Combined job",
    paper: PrintPaper.A4);
await Printing.PrintAsync(mixed);
```

Local files support PDF and PNG/JPEG/BMP/WebP. PDF requires single selection and cannot be mixed with other sources. Images allow single or multiple selection. TXT, Word and HTML are unsupported. File lists allow up to 100 files; each PDF is limited to 64 MB and 10,000 pages; each image to 32 MB and 32 million pixels, with a combined image budget of 256 MB.

PDFium is deployed through native NuGet dependencies; Office or an external converter is unnecessary. Pages are rasterized on demand at 300 DPI, capped at 16 million pixels and 8192 pixels per edge for large pages. Re-exporting imported PDF does not preserve original vectors, forms or searchable text. Password input for encrypted PDFs is not supported. The first page determines the common paper; mixed page sizes fit and center on that paper.

Images default to one per page. Only pure image jobs expose image layout options. Visuals, existing fixed-page documents and mixed business sources keep their original pagination, fitted and centered on the first source's paper by default.

The caller owns input streams, visuals and existing documents and keeps them alive until `PrintAsync` finishes. The service disposes its temporary snapshots. Path inputs require no manual document creation or disposal. Streams are read from their current position: rewind before reuse and do not share one stream between concurrent requests. Visual capture does not expand virtualized or offscreen content.

For business pagination:

```csharp
var source = PrintSource.FromPages("Report", pageCount, (canvas, pageIndex) =>
{
    // Draw one page. Indexes start at zero; coordinates use PDF points (72/in).
});
await Printing.PrintAsync(source);
```

Load data before the synchronous draw callback. Custom sources can derive from `PrintSource`, implementing `Title` and `CreateDocumentAsync`. Return a fresh document each time; the service owns and disposes that result. Callers invoking `CreateDocumentAsync` themselves own its result.

## Remote PDF and images

The three-argument API accepts URLs directly:

```csharp
await Printing.PrintAsync("https://example.com/report.pdf");
await Printing.PrintAsync(new[]
{
    "https://example.com/a.jpg?size=large",
    "https://example.com/b.png"
}, new PrintSettings { ImageOptions = new() { Layout = PrintImageLayout.TwoColumns } });
await Printing.PrintAsync(new Uri("https://example.com/download?id=123"), showPreview: false);
```

Use `PrintSource.FromUrl(url)` or `FromUrls(urls)` for explicit sources. An optional caller-owned HttpClient supplies headers, authentication, proxy settings and timeout:

```csharp
var source = PrintSource.FromUrl(url, httpClient);
await printing.PrintAsync(source, options, showPreview: true, cancellationToken: ct);
```

- Only absolute HTTP/HTTPS URLs are accepted. No network service registration is required. The shared client reuses connections, stores no cookies and follows at most five redirects. Supplied clients remain caller-owned.
- Download occurs during document preparation, before preview opens. Cancellation is supported. HttpClient.Timeout bounds the entire download including response-body reads (100 seconds by default); InfiniteTimeSpan leaves cancellation to the caller.
- File signatures identify PDF and supported images, including URLs without extensions or with query strings and redirects. Content-Type must be a supported PDF/image type, generic binary type, or absent. HTML, unknown formats and mismatched declarations fail explicitly.
- PDF must be the only source, including PDFs detected after download or inside nested combinations. Arrays accept up to 100 image URLs in input order; pure image jobs retain existing layout and fill settings.
- Existing limits apply: 64 MB per PDF, 32 MB per image and 256 MB combined image data. Content is fully buffered before parsing; there is no download preview or automatic retry.
- String conversions recognize URLs; explicit `FromFile / FromFiles` factories remain local-path APIs. The library disposes HTTP responses and response streams; ownership of explicitly supplied input streams is unchanged.

PreviewHost accepts one URL per line in its network source entry. Select the source, then use the common Print button and preview switch. Active downloads can be cancelled.

## Image content and layout

```csharp
await Printing.PrintAsync(new[] { "a.jpg", "b.png", "c.jpg" }, new PrintSettings
{
    ImageOptions = new PrintImageOptions
    {
        Layout = PrintImageLayout.Grid2x2,
        Fill = PrintImageFill.Fit,
        Alignment = PrintImageAlignment.Center,
        Order = PrintImageOrder.AcrossThenDown,
        SpacingMm = 4,
        AutoRotate = true
    }
});
```

The image section in preview edits the same settings. With `showPreview: false`, the service uses them directly.

| Option | Values and behavior |
| --- | --- |
| `Layout` | `OnePerPage`, `TwoRows`, `TwoColumns`, `Grid2x2`, `Grid3x3` |
| `Fill` | `Fit` contains the full image; `Fill` covers and crops; `Stretch` fills without preserving aspect ratio; `ActualSize` assumes 96 DPI |
| `Alignment` | Nine positions from `TopLeft` through `BottomRight`, controlling whitespace or crop placement |
| `Order` | `AcrossThenDown` or `DownThenAcross` |
| `SpacingMm` | Cell gap, 0–100 mm; must leave a positive printable cell |
| `AutoRotate` | Rotate 90 degrees when it improves cell utilization; ignored for stretch |

EXIF orientation is corrected automatically. Layout uses paper orientation, user margins and hardware margins. Preview, export and submission share composition. Page ranges, copies and duplex operate on composed pages. `document.PageCount` is the original image count; `document.GetPageCount(settings)` gives the composed count. PDF and Visual sources do not show image controls.

## Settings and advanced entry points

`PrintSettings` provides `PrinterId`, `Copies`, `PageRange`, `Paper`, `Orientation`, `Color`, `Duplex`, `Collate`, `Margins`, `ScaleMode`, `ScalePercent`, `RasterDpi` and `ImageOptions`. Paper and margins use millimetres. Page ranges are one-based inclusive values such as `1-3,5`; empty means all pages. Invalid settings or unsupported device capabilities fail before submission. Preview initializes these settings and lets the user adjust to device capabilities. Hardware and user margins use the larger value. Screen zoom does not affect physical size.

Advanced APIs remain available:

- `PrintDocument.Create / FromImage / FromControl / Combine` for explicitly owned snapshots.
- `new PrintPreview(document, service, settings)` for embedded UI; handle `CloseRequested`, retain the document and set `Density` as needed.
- `service.ShowDialogAsync(owner, document, settings)` for an explicit owner.
- `service.SubmitAsync(document, settings)` for low-level direct submission; requires an explicit `PrinterId`.
- `Printing.ExportPdfAsync(document, stream, settings)` for printer-free export; each selected page appears once, and the stream stays open.

**Migration:** the old direct `PrintAsync(document, settings, cancellationToken)` API is now `SubmitAsync`. The current `PrintAsync(document, options)` defaults to preview. Add `showPreview: false` for direct printing and pass cancellation as `cancellationToken:`.

## Platforms and validation

Windows uses GDI and installed drivers; macOS uses bundled AppKit/PDFKit bridges; Linux uses system libcups.so.2. The package is independent of the TheOne main library and One.* resources. Theme registration is explicit; density uses the package's PrintDensity.

Installed devices, drivers, OS printing services and access rights remain host prerequisites. App Sandbox, Flatpak and Snap require separate validation. Accepted submission does not prove physical paper output. Cancellation is best effort once submission starts; do not automatically retry uncertain results. Cancelled PDF export may leave an incomplete destination; temporary spool files are cleaned up.

PreviewHost opens the simple MVVM example: choose one PDF, one or multiple images, network URLs, a Visual, report or mixed sources, set optional copies and printer ID, toggle preview, then print. Enter one network URL per line and select the network source before printing. The ViewModel calls injected `IPrintService.PrintAsync`; the View handles native file selection and visual sources, while URL input binds to the ViewModel. The library prepares and releases documents. Cancellation or picker failure retains the selected source; active requests prevent reentry.

On 2026-10-16, PreviewHost restored `1.0.0-preview.3` from nuget.org using a fresh cache on macOS arm64 and built in Release with zero warnings or errors. Real desktop checks confirmed a public HTTPS PDF rendered correctly in the main preview and thumbnail. Five public PNG/JPEG/WebP image URLs produced five pages by default and two pages with the 2×2 layout, with correct colors and order. Previews were cancelled; no physical print jobs were submitted.

This online-package check used a managed host. The local package had previously passed macOS arm64 NativeAOT execution; NativeAOT was not repeated with the online package. Windows/Linux native execution, physical output and sandbox deployment remain unverified. A disabled Open button was previously observed in the native file picker of a temporary macOS host; successful URL previews do not establish that the local picker issue is resolved.

PreviewHost retains the minimal MVVM example; historical verification entry points have been removed. Local package consumption and NativeAOT commands are in [development instructions](DEVELOPMENT.md). Debug and Release remain WinExe.
