using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SkiaSharp;
using TheOne.PrintingTools.Avalonia;

namespace TheOne.DesignPreview;

public sealed partial class PrintingDemoView : UserControl, IPrintingDemoPlatform
{
    public PrintingDemoView()
    {
        InitializeComponent();
    }

    public Task<PrintSource?> PickSourceAsync(bool allowMultiple)
    {
        return PickAsync(PickerOptions(allowMultiple));
    }

    public Task<PrintSource?> PickDocumentAsync()
    {
        return PickAsync(DocumentPickerOptions());
    }

    public PrintSource CreateVisualSource()
    {
        return PrintSource.FromControl(CaptureSample, "Visual 视觉树");
    }

    public PrintSource CreateMixedSource()
    {
        return PrintSource.Combine(
        [
            SampleReport.CreateSource(),
            PrintSource.FromPages("图形来源", 1, (canvas, _) =>
            {
                canvas.Clear(SKColor.Parse("#006061"));
                using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };
                canvas.DrawCircle(300, 360, 120, ink);
            }, PrintPaper.Letter),
            CreateVisualSource()
        ], "混合来源：报表 6 页 + 图形 + 控件");
    }

    internal static FilePickerOpenOptions PickerOptions(bool multiple)
    {
        return new FilePickerOpenOptions
        {
            Title = multiple ? "选择多张图片" : "选择一张图片",
            AllowMultiple = multiple,
            FileTypeFilter =
            [
                new FilePickerFileType("图片")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"],
                    MimeTypes = ["image/png", "image/jpeg", "image/bmp", "image/webp"],
                    AppleUniformTypeIdentifiers =
                        ["public.png", "public.jpeg", "com.microsoft.bmp", "org.webmproject.webp"]
                }
            ]
        };
    }

    internal static FilePickerOpenOptions DocumentPickerOptions()
    {
        return new FilePickerOpenOptions
        {
            Title = "选择一个 PDF 文档", AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("PDF")
                {
                    Patterns = ["*.pdf"], MimeTypes = ["application/pdf"],
                    AppleUniformTypeIdentifiers = ["com.adobe.pdf"]
                }
            ]
        };
    }

    private async Task<PrintSource?> PickAsync(FilePickerOpenOptions options)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider
                      ?? throw new InvalidOperationException("示例页尚未连接窗口。");
        if (!storage.CanOpen) throw new NotSupportedException("当前平台没有可用的文件选择器。");
        var files = await storage.OpenFilePickerAsync(options);
        try
        {
            if (files.Count == 0) return null;
            if (!options.AllowMultiple && files.Count != 1) throw new ArgumentException("文档只允许单选。");
            if (files.Any(file => !file.Path.IsFile)) throw new NotSupportedException("此入口请选择本地文件。");
            return PrintSource.FromFiles(files.Select(file => file.Path.LocalPath));
        }
        finally
        {
            foreach (var file in files) file.Dispose();
        }
    }
}