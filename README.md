# TheOne.PrintingTools.Avalonia

独立的 .NET 10 / Avalonia 12.1.4 桌面打印组件。统一 API 支持 PDF、图片、Avalonia Visual 和业务分页文档，可选择先预览或直接打印。

Cross-platform desktop printing for Avalonia with PDF import, image layouts, Visual snapshots, PDF export, and optional preview. Uses Windows GDI, macOS AppKit/PDFKit, and Linux CUPS.

[源码仓库](https://github.com/The1218/TheOne.PrintingTools.Avalonia) · [NuGet](https://www.nuget.org/packages/TheOne.PrintingTools.Avalonia) · [中文指南](https://github.com/The1218/TheOne.PrintingTools.Avalonia/blob/main/docs/USAGE.zh-cn.md) · [English guide](https://github.com/The1218/TheOne.PrintingTools.Avalonia/blob/main/docs/USAGE.en.md)

## 安装

当前版本为预览版：

```sh
dotnet add package TheOne.PrintingTools.Avalonia --version 1.0.0-preview.2
```

支持 Windows、macOS 14+ 和 Linux 桌面。系统打印服务、设备驱动、可用打印队列和应用权限由运行环境提供；Linux 需要 libcups.so.2。

## 三个参数，一次调用

```csharp
using TheOne.PrintingTools.Avalonia;

await Printing.PrintAsync("report.pdf");                       // PDF 单选，默认先预览
await Printing.PrintAsync(new[] { "a.jpg", "b.png" });            // 图片可多选
await Printing.PrintAsync("report.pdf", showPreview: false);    // 直接打印

await Printing.PrintAsync(
    source: "report.pdf",
    options: new PrintSettings { Copies = 2 },
    showPreview: true);
```

参数分别是**打印源、可选 PrintSettings、是否预览（默认 true）**。路径、路径数组、图片流、Visual、已有文档和 `PrintSource[]` 都可以传入。库管理自身创建的临时文档，调用方保留输入流和已有文档的所有权。

无需强制注册服务。MVVM 可以在应用组合根创建 `IPrintService printing = new PrintService(() => desktop.MainWindow);`，再注入 ViewModel。默认预览使用活动窗口或主窗口，应在窗口可见后调用。

`showPreview: false` 使用指定的 `PrinterId` 或系统默认打印机；没有默认设备时会报错。进入预览后不会自动提交，等待用户确认。

## 主题

使用预览界面时，在 App.axaml 显式注册主题：

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:printing="using:TheOne.PrintingTools.Avalonia">
  <Application.Styles>
    <printing:PrintingToolsTheme />
  </Application.Styles>
</Application>
```

主题包含本包色板、控件主题和 Fluent 原生控件基础样式；密度使用 `PrintPreview.Density`，可选 Small / Normal / Large。本包不依赖 TheOne.Avalonia 主库或 OneTheme。

## 打印源与图片排版

- **PDF**：单选；不与其他来源混选。
- **图片**：PNG/JPEG/BMP/WebP，单选或多选，自动校正 EXIF 方向。
- **Visual**：捕获已布局的视觉树区域；虚拟化或离屏数据需要业务显式分页。
- **分页文档**：支持 Skia 绘制回调、已有 PrintDocument，以及业务来源合并。
- **预览与导出**：缩略图、页码范围、纸张、方向、边距、缩放、份数、单双面、彩色／黑白和 PDF 导出。

纯图片任务可选择每页一张、上下／左右两张、四宫格、九宫格，以及填充、对齐、顺序、间距和自动旋转：

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

`Fit` 完整显示，`Fill` 铺满裁切，`Stretch` 拉伸，`ActualSize` 按 96 DPI 实际大小。页码范围、份数和双面作用于排版后的页面。Visual 和固定分页文档保持原有分页行为。

## 平台实现与限制

| 平台 | 提交方式 |
| --- | --- |
| Windows | Skia 页面位图条带 → GDI → 系统打印队列 |
| macOS | 临时 PDF → 随包提供的 AppKit/PDFKit 桥接 → 系统打印服务 |
| Linux | 临时 PDF → 系统 libcups → CUPS 队列 |

PDFium 原生依赖随 NuGet 部署，不需要安装 Office 或外部转换器。PDF 按需以 300 DPI 栅格化，大页面限制为 1600 万像素和单边 8192 像素；混合尺寸页面适配到首张纸张。导入后导出不保留原始矢量、可搜索文本或交互表单，暂不提供加密 PDF 密码输入。TXT、Word、HTML 不支持。

当前为预览版：macOS 临时验证宿主的原生文件选择器曾出现 Open 按钮禁用，完整选择到预览流程需在实际应用宿主复核。Windows / Linux 原生运行与真实出纸尚未验证。“提交成功”表示交给系统打印服务，不保证设备已经出纸；提交结果不明确时不要自动重试。

## 示例与开发

```sh
dotnet run --project design/PreviewHost/PreviewHost.csproj
dotnet build design/PreviewHost/PreviewHost.csproj --artifacts-path .preview/work/artifacts
```

`design/PreviewHost` 保留最简 View / ViewModel 演示：选择 PDF、单张／多张图片、Visual、报表或混合来源，设置可选参数并选择是否预览。历史一次性验证脚本与截图缓存不纳入仓库。

PreviewHost 仅通过固定版本 NuGet 包测试控件，不提供源码引用或条件切换；修改库源码后需先打包并更新实际消费的包，演示结果才会反映本次改动。

- [跨平台打印原理](https://github.com/The1218/TheOne.PrintingTools.Avalonia/blob/main/docs/PRINTING-PRINCIPLES.zh-cn.md)
- [开发与本地包消费](https://github.com/The1218/TheOne.PrintingTools.Avalonia/blob/main/docs/DEVELOPMENT.md)
- [打包与发布](https://github.com/The1218/TheOne.PrintingTools.Avalonia/blob/main/docs/PUBLISHING.md)

仓库仅保留源码、必要的演示与构建配置、公共文档和许可证。Agent 配置、凭据、bin / obj、.preview 与 NuGet 构建产物由 .gitignore 排除。

## 许可证

本项目使用 MIT 许可证。Avalonia、SkiaSharp、PDFium 等依赖保留各自许可证，见 LICENSE、THIRD-PARTY-NOTICES.md 和 docs/PDFium-LICENSE.txt。
