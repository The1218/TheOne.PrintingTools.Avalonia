using SkiaSharp;

namespace TheOne.PrintingTools.Avalonia;

internal sealed class ImagePrintDocument
{
    internal byte[][] Images { get; }
    internal long ByteCount { get; }
    internal ImagePrintDocument(byte[][] images)
    {
        Images = images;
        ByteCount = images.Sum(image => (long)image.Length);
        if (ByteCount > 256L * 1024 * 1024) throw new ArgumentException("图片总大小不能超过 256 MB。");
    }

    internal static PrintDocument Create(string title, byte[] bytes)
    {
        using var input = new SKMemoryStream(bytes);
        using var codec = SKCodec.Create(input) ?? throw new ArgumentException("无法读取图片。");
        if (codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 32_000_000)
            throw new ArgumentException("图片像素不能超过 3200 万，请先缩小图片。");
        return new PrintDocument(title, PrintPaper.A4, new ImagePrintDocument([bytes]));
    }

    internal static (int Rows, int Columns) Grid(PrintImageLayout layout) => layout switch
    {
        PrintImageLayout.OnePerPage => (1, 1), PrintImageLayout.TwoRows => (2, 1),
        PrintImageLayout.TwoColumns => (1, 2), PrintImageLayout.Grid2x2 => (2, 2),
        PrintImageLayout.Grid3x3 => (3, 3), _ => throw new ArgumentException("图片布局无效。")
    };

    internal static void Validate(PrintImageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _ = Grid(options.Layout);
        if (!Enum.IsDefined(options.Fill) || !Enum.IsDefined(options.Order) || !Enum.IsDefined(options.Alignment))
            throw new ArgumentException("图片排版选项无效。");
        if (!double.IsFinite(options.SpacingMm) || options.SpacingMm is < 0 or > 100)
            throw new ArgumentException("图片间距必须在 0–100 mm 内。");
    }

    internal void Draw(SKCanvas canvas, int sheet, SKRect area, PrintImageOptions options)
    {
        Validate(options);
        var (rows, columns) = Grid(options.Layout);
        var gap = (float)(options.SpacingMm * 72 / 25.4);
        var cellWidth = (area.Width - gap * (columns - 1)) / columns;
        var cellHeight = (area.Height - gap * (rows - 1)) / rows;
        if (cellWidth <= 0 || cellHeight <= 0) throw new ArgumentException("图片间距超出可打印区域。");
        for (var slot = 0; slot < rows * columns; slot++)
        {
            var index = sheet * rows * columns + slot;
            if (index >= Images.Length) break;
            var row = options.Order == PrintImageOrder.AcrossThenDown ? slot / columns : slot % rows;
            var column = options.Order == PrintImageOrder.AcrossThenDown ? slot % columns : slot / rows;
            var left = area.Left + column * (cellWidth + gap); var top = area.Top + row * (cellHeight + gap);
            DrawImage(canvas, Images[index], new SKRect(left, top, left + cellWidth, top + cellHeight), options);
        }
    }

    private static void DrawImage(SKCanvas canvas, byte[] bytes, SKRect cell, PrintImageOptions options)
    {
        using var image = Decode(bytes);
        var rotate = options.AutoRotate && options.Fill != PrintImageFill.Stretch &&
            Math.Min(cell.Width / image.Height, cell.Height / image.Width) > Math.Min(cell.Width / image.Width, cell.Height / image.Height);
        var width = rotate ? image.Height : image.Width; var height = rotate ? image.Width : image.Height;
        var scale = options.Fill == PrintImageFill.Fill ? Math.Max(cell.Width / width, cell.Height / height)
            : options.Fill == PrintImageFill.ActualSize ? .75f : Math.Min(cell.Width / width, cell.Height / height);
        var drawWidth = options.Fill == PrintImageFill.Stretch ? cell.Width : width * scale;
        var drawHeight = options.Fill == PrintImageFill.Stretch ? cell.Height : height * scale;
        var alignment = (int)options.Alignment;
        var x = cell.Left + (cell.Width - drawWidth) * (alignment % 3) / 2;
        var y = cell.Top + (cell.Height - drawHeight) * (alignment / 3) / 2;
        canvas.Save();
        try
        {
            canvas.ClipRect(cell);
            canvas.Translate(x, y); canvas.Scale(drawWidth / width, drawHeight / height);
            if (rotate) { canvas.Translate(image.Height, 0); canvas.RotateDegrees(90); }
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawBitmap(image, 0, 0, paint);
        }
        finally { canvas.Restore(); }
    }

    private static SKBitmap Decode(byte[] bytes)
    {
        using var input = new SKMemoryStream(bytes);
        using var codec = SKCodec.Create(input) ?? throw new PrintException("无法解码图片。");
        var image = SKBitmap.Decode(codec) ?? throw new PrintException("无法解码图片像素。");
        var origin = (int)codec.EncodedOrigin;
        if (origin == 1) return image;
        try
        {
            var width = image.Width; var height = image.Height;
            var result = new SKBitmap(origin >= 5 ? height : width, origin >= 5 ? width : height);
            try
            {
                using var canvas = new SKCanvas(result);
                var matrix = origin switch
                {
                    2 => new SKMatrix(-1, 0, width, 0, 1, 0, 0, 0, 1),
                    3 => new SKMatrix(-1, 0, width, 0, -1, height, 0, 0, 1),
                    4 => new SKMatrix(1, 0, 0, 0, -1, height, 0, 0, 1),
                    5 => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
                    6 => new SKMatrix(0, -1, height, 1, 0, 0, 0, 0, 1),
                    7 => new SKMatrix(0, -1, height, -1, 0, width, 0, 0, 1),
                    8 => new SKMatrix(0, 1, 0, -1, 0, width, 0, 0, 1),
                    _ => SKMatrix.Identity
                };
                canvas.SetMatrix(matrix); canvas.DrawBitmap(image, 0, 0);
                return result;
            }
            catch { result.Dispose(); throw; }
        }
        finally { image.Dispose(); }
    }
}
