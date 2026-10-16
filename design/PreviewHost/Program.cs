using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using SkiaSharp;
using TheOne.PrintingTools.Avalonia;

namespace TheOne.DesignPreview;

// Minimal desktop example. Printing is provided by the injected IPrintService.
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppBuilder.Configure<PreviewApp>().UseSkia().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}

internal sealed partial class PreviewApp : Application
{
    public override void Initialize() => global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var view = new PrintingDemoView();
            // Composition root: optional DI registration can use this same IPrintService instance.
            IPrintService printing = new PrintService(() => desktop.MainWindow);
            var model = new PrintingDemoViewModel(view, printing);
            view.DataContext = model;
            desktop.MainWindow = new Window
            {
                Title = "PrintingTools · MVVM 示例", Width = 980, Height = 960,
                MinWidth = 480, MinHeight = 720, Content = view
            };
            desktop.MainWindow.Closing += (_, e) =>
            {
                if (model.IsBusy) { e.Cancel = true; model.Cancel(); }
            };
            desktop.MainWindow.Closed += (_, _) => { view.DataContext = null; model.Dispose(); };
        }
        base.OnFrameworkInitializationCompleted();
    }
}

internal static class SampleReport
{
    internal static PrintSource CreateSource() => PrintSource.FromPages("销售明细报表 · 6 页", 6, DrawPage);
    private static void DrawPage(SKCanvas canvas, int page)
    {
        using var face = SKFontManager.Default.MatchCharacter('销') ?? SKTypeface.Default;
        using var font = new SKFont(face, 11); using var ink = new SKPaint { IsAntialias = true, Color = SKColors.Black };
        void Text(string text, float x, float y, float size = 11) { font.Size = size; canvas.DrawText(text, x, y, SKTextAlign.Left, font, ink); }
        ink.Color = SKColor.Parse("#006061"); canvas.DrawRect(28, 28, 539, 2, ink); ink.Color = SKColors.Black;
        Text("销售明细报表", 32, 68, 24); Text("SALES DETAIL REPORT", 32, 90, 10);
        Text("XX 科技有限公司", 425, 62); Text("统计期间：2026.10.01 — 2026.10.15", 32, 116, 10);
        ink.Color = SKColor.Parse("#006061"); canvas.DrawRect(28, 134, 539, 28, ink); ink.Color = SKColors.White;
        Text("日期           订单编号             客户        产品名称           数量       金额", 36, 152, 10);
        for (var row = 0; row < 18; row++)
        {
            var y = 184 + row * 30;
            ink.Color = row % 2 == 0 ? SKColors.White : SKColor.Parse("#F0F6F6"); canvas.DrawRect(28, y - 16, 539, 29, ink);
            ink.Color = SKColors.Black; Text($"2026.10.{row % 15 + 1:00}   SO2610{page * 18 + row + 1:0000}       客户 A       笔记本电脑          1       4,999.00", 36, y, 9);
            ink.Color = SKColor.Parse("#D2D5D5"); ink.StrokeWidth = .5f; canvas.DrawLine(28, y + 14, 567, y + 14, ink);
        }
        ink.Color = SKColor.Parse("#006061"); canvas.DrawLine(28, 770, 567, 770, ink); ink.Color = SKColors.Black;
        Text("销售管理中心", 28, 793, 8); Text($"第 {page + 1} 页 / 共 6 页", 468, 793, 8);
    }
}
