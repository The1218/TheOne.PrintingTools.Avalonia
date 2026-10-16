using SkiaSharp;

namespace TheOne.PrintingTools.Avalonia;

internal static class PrintLayout
{
    internal const double PointsPerMm = 72 / 25.4;

    internal static void ValidatePaper(PrintPaper paper)
    {
        ArgumentNullException.ThrowIfNull(paper);
        if (!double.IsFinite(paper.WidthMm) || !double.IsFinite(paper.HeightMm) || paper.WidthMm is < 10 or > 2000 ||
            paper.HeightMm is < 10 or > 2000)
            throw new ArgumentException("纸张尺寸必须在 10–2000 mm 内。");
    }

    internal static void Validate(PrintDocument doc, PrintSettings settings)
    {
        ValidatePaper(settings.Paper);
        if (settings.Copies is < 1 or > 999) throw new ArgumentException("份数必须在 1–999 内。");
        if (!Enum.IsDefined(settings.Orientation) || !Enum.IsDefined(settings.Color) ||
            !Enum.IsDefined(settings.Duplex) || !Enum.IsDefined(settings.ScaleMode))
            throw new ArgumentException("打印设置包含无效选项。");
        if (!double.IsFinite(settings.ScalePercent) || settings.ScalePercent is < 10 or > 400)
            throw new ArgumentException("缩放比例必须在 10–400% 内。");
        if (settings.RasterDpi is < 72 or > 600) throw new ArgumentException("打印分辨率必须在 72–600 DPI 内。");
        var m = settings.Margins ?? throw new ArgumentException("缺少页边距。");
        if (new[] { m.Left, m.Top, m.Right, m.Bottom }.Any(x => !double.IsFinite(x) || x < 0))
            throw new ArgumentException("页边距必须为非负有限数值。");
        var (w, h) = Size(settings);
        if ((m.Left + m.Right) * PointsPerMm >= w || (m.Top + m.Bottom) * PointsPerMm >= h)
            throw new ArgumentException("页边距超出纸张尺寸。");
        if (doc.IsImageDocument)
        {
            ImagePrintDocument.Validate(settings.ImageOptions);
            var (rows, columns) = ImagePrintDocument.Grid(settings.ImageOptions.Layout);
            var widthMm = settings.Orientation == PrintOrientation.Portrait
                ? settings.Paper.WidthMm
                : settings.Paper.HeightMm;
            var heightMm = settings.Orientation == PrintOrientation.Portrait
                ? settings.Paper.HeightMm
                : settings.Paper.WidthMm;
            if (settings.ImageOptions.SpacingMm * (columns - 1) >= widthMm - m.Left - m.Right ||
                settings.ImageOptions.SpacingMm * (rows - 1) >= heightMm - m.Top - m.Bottom)
                throw new ArgumentException("图片间距超出可打印区域。");
        }

        _ = PrintPageRange.Parse(settings.PageRange, doc.GetPageCount(settings));
    }

    internal static (float Width, float Height) Size(PrintSettings s)
    {
        return s.Orientation == PrintOrientation.Portrait
            ? ((float)(s.Paper.WidthMm * PointsPerMm), (float)(s.Paper.HeightMm * PointsPerMm))
            : ((float)(s.Paper.HeightMm * PointsPerMm), (float)(s.Paper.WidthMm * PointsPerMm));
    }

    internal static IEnumerable<int> OutputPages(PrintDocument document, PrintSettings s)
    {
        var pages = PrintPageRange.Parse(s.PageRange, document.GetPageCount(s));
        if (s.Collate)
            for (var copy = 0; copy < s.Copies; copy++)
            {
                foreach (var page in pages) yield return page;
                // Keep each collated copy on its own sheet for an odd duplex selection.
                if (s.Duplex != PrintDuplex.Simplex && pages.Count % 2 != 0 && copy < s.Copies - 1) yield return -1;
            }
        else if (s.Duplex != PrintDuplex.Simplex)
            // Uncollated duplex copies repeat physical sheets, not individual sides.
            for (var i = 0; i < pages.Count; i += 2)
            for (var copy = 0; copy < s.Copies; copy++)
            {
                yield return pages[i];
                if (i + 1 < pages.Count) yield return pages[i + 1];
                else if (copy < s.Copies - 1) yield return -1;
            }
        else
            foreach (var page in pages)
                for (var copy = 0; copy < s.Copies; copy++)
                    yield return page;
    }

    internal static void DrawSheet(SKCanvas canvas, PrintDocument document, PrintSettings settings, int page,
        PrintMargins? hardMargins = null)
    {
        canvas.Clear(SKColors.White);
        if (page < 0) return;
        var (width, height) = Size(settings);
        var margins = settings.Margins;
        var hard = hardMargins ?? PrintMargins.Zero;
        var left = (float)(Math.Max(margins.Left, hard.Left) * PointsPerMm);
        var top = (float)(Math.Max(margins.Top, hard.Top) * PointsPerMm);
        var right = width - (float)(Math.Max(margins.Right, hard.Right) * PointsPerMm);
        var bottom = height - (float)(Math.Max(margins.Bottom, hard.Bottom) * PointsPerMm);
        if (right <= left || bottom <= top) throw new ArgumentException("打印机可打印区域为空。");
        var scale = settings.ScaleMode switch
        {
            PrintScaleMode.ActualSize => 1d,
            PrintScaleMode.Custom => settings.ScalePercent / 100,
            _ => Math.Min((right - left) / document.WidthPoints, (bottom - top) / document.HeightPoints)
        };
        var restoreCount = canvas.Save();
        try
        {
            canvas.ClipRect(new SKRect(left, top, right, bottom));
            using var filter = settings.Color == PrintColor.Monochrome
                ? SKColorFilter.CreateColorMatrix([
                    .2126f, .7152f, .0722f, 0, 0, .2126f, .7152f, .0722f, 0, 0, .2126f, .7152f, .0722f, 0, 0, 0, 0, 0,
                    1, 0
                ])
                : null;
            using var paint = filter is null ? null : new SKPaint { ColorFilter = filter };
            if (paint is not null) canvas.SaveLayer(paint);
            if (document.Images is not null)
            {
                if (settings.ScaleMode == PrintScaleMode.Custom)
                {
                    canvas.Translate((left + right) / 2, (top + bottom) / 2);
                    canvas.Scale((float)(settings.ScalePercent / 100));
                    canvas.Translate(-(left + right) / 2, -(top + bottom) / 2);
                }

                var imageOptions = settings.ScaleMode == PrintScaleMode.ActualSize
                    ? settings.ImageOptions with { Fill = PrintImageFill.ActualSize }
                    : settings.ImageOptions;
                document.DrawImages(canvas, page, new SKRect(left, top, right, bottom), imageOptions);
                return;
            }

            canvas.Translate(left + (float)((right - left - document.WidthPoints * scale) / 2),
                top + (float)((bottom - top - document.HeightPoints * scale) / 2));
            canvas.Scale((float)scale);
            document.Draw(canvas, page);
        }
        finally
        {
            canvas.RestoreToCount(restoreCount);
        }
    }
}