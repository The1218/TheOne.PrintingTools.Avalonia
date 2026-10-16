# PrintingTools 使用指南

目标：.NET 10 / Avalonia 12.1.4。日常使用只需要记住一个入口：**打印源、可选配置、是否预览**。

截至 2026-10-16，[1.0.0-preview.3](https://www.nuget.org/packages/TheOne.PrintingTools.Avalonia/1.0.0-preview.3) 已发布，可从 nuget.org 正常安装和还原。

## 先看这三个调用

```csharp
using TheOne.PrintingTools.Avalonia;

await Printing.PrintAsync("report.pdf");                       // 单个 PDF，默认先预览
await Printing.PrintAsync(new[] { "a.jpg", "b.png" });            // 单张或多张图片
await Printing.PrintAsync("report.pdf", showPreview: false);    // 直接打印到系统默认设备
```

需要配置时再传第二个参数：

```csharp
await Printing.PrintAsync(
    filePath,
    options: new PrintSettings { Copies = 2, PageRange = "1-3" },
    showPreview: true);
```

完整签名为 `PrintAsync(PrintSource source, PrintSettings? options = null, bool showPreview = true, CancellationToken cancellationToken = default)`。取消令牌只在需要取消操作时传入。

`showPreview: true` 打开预览并等待用户打印、取消或关闭，不会自动提交；用户可在预览里修改配置。返回的 `PrintResult` 表示提交结果或取消结果。选择文件本身不会打印。

`showPreview: false` 不打开窗口，直接提交。指定了 `options.PrinterId` 就使用该设备，否则使用系统默认打印机；没有默认设备时明确报错，不会静默改用列表中的第一台。默认直接打印使用源纸张、一份、黑白、单面、逐份、适合可打印区域及 10 mm 边距；传入配置对象时其 `Paper` 默认是 A4。

## 是否需要注册服务

**不需要强制注册服务，也不需要 AppBuilder 扩展。** 普通页面直接使用上面的 `Printing.PrintAsync`。

使用预览前，需要在 App.axaml 显式注册一次主题；直接打印无需加载界面主题：

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:printing="using:TheOne.PrintingTools.Avalonia">
  <Application.Styles>
    <printing:PrintingToolsTheme />
  </Application.Styles>
</Application>
```

预览默认使用当前活动桌面窗口或主窗口作为所属窗口，应在窗口打开后调用。多窗口应用建议明确指定所属窗口提供器。

MVVM 项目推荐在应用启动时创建一次 `IPrintService`，注入 ViewModel：

```csharp
// App / 组合根：desktop 是 IClassicDesktopStyleApplicationLifetime。
IPrintService printing = new PrintService(() => desktop.MainWindow);
var viewModel = new ReportViewModel(printing);

// ViewModel：不需要 Window、PrintPreview 或手动释放临时文档。
public sealed class ReportViewModel(IPrintService printing)
{
    public Task<PrintResult> PrintAsync(string[] files)
        => printing.PrintAsync(files, new PrintSettings { Copies = 2 });
}
```

如果宿主已有 Microsoft.Extensions.DependencyInjection，可使用：
`services.AddSingleton<IPrintService>(_ => new PrintService(() => desktop.MainWindow));`
本包不强制引入 DI 框架。测试时实现 `IPrintService` 即可替换整个打印流程；需要验证提交链路时，可以通过 `new PrintService(recordingBackend, () => ownerWindow)` 注入 `IPrintBackend`。

## 可以传入哪些源

以下参数会隐式转换成 `PrintSource`，都使用同一个 `PrintAsync`：

| 参数 | 行为 |
| --- | --- |
| `string` 本地路径或 HTTP/HTTPS URL | 读取或下载单个 PDF 或图片 |
| `Uri` / `Uri[]` | 单个网络文件或多张网络图片 |
| `string[]` 路径或 URL | 图片按数组顺序合并，支持本地／网络图片混合；包含 PDF 时必须只有一个来源 |
| `Stream` 图片流 | 从当前位置读取一张图片 |
| Avalonia `Visual` / `Control` | 在 UI 线程捕获已布局控件 |
| `PrintDocument` | 使用已有分页文档，仍由调用方持有 |
| `PrintSource[]` 混合来源 | 按数组顺序合并；PDF 不能混选 |

```csharp
await Printing.PrintAsync(imageStream);
await Printing.PrintAsync(reportControl);
await Printing.PrintAsync(existingDocument);

PrintSource[] sources = ["photo.jpg", reportControl, existingDocument];
await Printing.PrintAsync(sources);
```

需要明确格式、名称或接入其他集合时，使用工厂方法：

```csharp
var pdf = PrintSource.FromStream(pdfStream, "report.pdf");
await Printing.PrintAsync(pdf);

var image = PrintSource.FromImage(imageStream, "照片");
var files = PrintSource.FromFiles(filePathEnumerable);
var mixed = PrintSource.Combine(
    [image, PrintSource.FromControl(reportControl, "表单")],
    title: "本次打印",
    paper: PrintPaper.A4);
await Printing.PrintAsync(mixed);
```

本地文件支持 PDF 和 PNG/JPEG/BMP/WebP。PDF 只能单选，不能与其他来源混选；图片可单选或多选。TXT、Word、HTML 不支持。文件列表最多 100 个，单 PDF 最大 64 MB、单图片最大 32 MB 且最多 3200 万像素，图片总量最大 256 MB；PDF 最多 10000 页。

PDF 使用随 NuGet 依赖部署的 PDFium，不要求安装 Office 或其他转换软件。按需以 300 DPI 栅格化每页（大页面限制为 1600 万像素和单边 8192 像素），因此导入后导出 PDF 不保留原始矢量、表单或可搜索文本。加密 PDF 暂不提供密码输入。默认使用 PDF 第一页的纸张，混合尺寸页面等比居中适配到该纸张。

图片默认每页一张；只有纯图片任务提供下面的图片排版设置。Visual、已有固定分页文档及混合业务来源保持原来的分页行为，默认按第一来源纸张等比居中。

文件流、图片流、控件和已有文档仍由调用方持有，至少保持到 `await PrintAsync` 结束。服务会释放自身创建的临时快照；路径输入不需要调用方创建或释放 `PrintDocument`。流从当前位置读取，重复使用前需要自行复位，不要并发共享同一个流。控件只捕获已布局区域，不会展开虚拟化或离屏数据。

业务分页可直接提供绘制源：

```csharp
var source = PrintSource.FromPages("报表", pageCount, (canvas, pageIndex) =>
{
    // 绘制第 pageIndex 页；索引从 0 开始，坐标为 PDF point（72/in）。
});
await Printing.PrintAsync(source);
```

先加载业务数据，再在同步绘制回调中分页。高级自定义源可以继承 `PrintSource`，实现 `Title` 和 `CreateDocumentAsync`；每次返回新的文档，服务将在本次操作结束后释放它。调用方自己调用 `CreateDocumentAsync` 时，则负责释放返回的文档。

## 网络 PDF 与图片

三参数 API 不变，URL 可以直接作为第一个参数：

```csharp
await Printing.PrintAsync("https://example.com/report.pdf");
await Printing.PrintAsync(new[]
{
    "https://example.com/a.jpg?size=large",
    "https://example.com/b.png"
}, new PrintSettings { ImageOptions = new() { Layout = PrintImageLayout.TwoColumns } });
await Printing.PrintAsync(new Uri("https://example.com/download?id=123"), showPreview: false);
```

也可显式使用 `PrintSource.FromUrl(url)` 或 `FromUrls(urls)`。需要请求头、鉴权、代理或自定义超时时，可以提供应用管理的 HttpClient：

```csharp
var source = PrintSource.FromUrl(url, httpClient);
await printing.PrintAsync(source, options, showPreview: true, cancellationToken: ct);
```

- 仅接受完整 HTTP/HTTPS 地址；不需要注册网络服务。默认客户端复用连接、不保存 Cookie，最多自动重定向 5 次。传入的 HttpClient 不会被库释放。
- 下载发生在 `PrintAsync` 准备文档时，预览出现前完成。支持取消；下载总时限沿用 HttpClient.Timeout（默认 100 秒），覆盖响应头和正文读取，InfiniteTimeSpan 表示由调用方取消控制。
- 根据文件签名识别 PDF／图片，支持查询参数、重定向和没有扩展名的 URL。响应 Content-Type 必须为支持的 PDF／图片类型、通用二进制类型或未提供；网页、未知格式及声明与内容不符的响应会报错。
- PDF 仍只能作为单个来源；无扩展名地址在下载识别后也会检查，不能通过组合源绕过。图片地址数组最多 100 个，顺序与输入一致；纯图片任务沿用所有布局和填充配置。
- 网络与本地输入沿用 64 MB/PDF、32 MB/图片和 256 MB 图片总量限制。服务先完整读取内容，不做边下载边预览，也不自动重试失败请求。
- 字符串隐式转换会识别 URL；`FromFile / FromFiles` 工厂仍专用于本地路径。网络响应流由库释放；调用方显式提供的流仍遵循原来的所有权规则。

PreviewHost 的“网络 PDF / 图片”支持每行输入一个 URL，点击“使用网络源”选择来源，再由统一“打印”按钮按预览开关执行；下载中可取消。

## 图片内容与排版

```csharp
await Printing.PrintAsync(new[] { "a.jpg", "b.png", "c.jpg" }, new PrintSettings
{
    ImageOptions = new PrintImageOptions
    {
        Layout = PrintImageLayout.Grid2x2,       // 四宫格
        Fill = PrintImageFill.Fit,              // 完整显示
        Alignment = PrintImageAlignment.Center,
        Order = PrintImageOrder.AcrossThenDown,
        SpacingMm = 4,
        AutoRotate = true
    }
});
```

预览中的“图片内容与排版”允许修改同一组参数；`showPreview: false` 直接使用这些参数打印。

| 选项 | 可选值与含义 |
| --- | --- |
| `Layout` | `OnePerPage`、`TwoRows`、`TwoColumns`、`Grid2x2`、`Grid3x3` |
| `Fill` | `Fit` 完整显示、`Fill` 铺满并裁切、`Stretch` 拉伸、`ActualSize` 按 96 DPI 实际大小 |
| `Alignment` | 左上至右下九个对齐位置，控制留白或裁切方向 |
| `Order` | `AcrossThenDown` 先横后纵、`DownThenAcross` 先纵后横 |
| `SpacingMm` | 单元格间距，0–100 mm，不能挤占全部可打印区域 |
| `AutoRotate` | 必要时旋转 90° 以提高单元格利用率；拉伸模式忽略此项 |

自动校正图片 EXIF 方向。布局基于纸张方向、用户边距及打印机硬边距计算，预览、导出和打印使用同一排版。`PageRange` 指排版后的页码，份数和双面也作用于排版后的页面。`document.PageCount` 为原始图片数，`document.GetPageCount(settings)` 返回排版后的页数。PDF 和 Visual 不显示图片选项。

## 可选配置与高级入口

`PrintSettings` 提供 `PrinterId`、`Copies`、`PageRange`、`Paper`、`Orientation`、`Color`、`Duplex`、`Collate`、`Margins`、`ScaleMode`、`ScalePercent`、`RasterDpi` 和 `ImageOptions`。纸张及边距为 mm，页码为从 1 开始的闭区间，如 `1-3,5`；空范围表示全部。无效参数及不支持的设备能力会在提交前拒绝。预览会初始化这些配置，并允许用户按设备能力调整。硬件边距与用户边距取较大值；屏幕缩放不改变物理打印尺寸。

需要自己管理快照或嵌入预览时，仍可使用：

- `PrintDocument.Create / FromImage / FromControl / Combine`。
- `new PrintPreview(document, service, settings)`，通过 `CloseRequested` 接收结果；宿主持有文档，密度通过 `Density` 设置。
- `service.ShowDialogAsync(owner, document, settings)`，明确指定所属窗口。
- `service.SubmitAsync(document, settings)`，底层直接提交，必须明确指定 `PrinterId`。
- `Printing.ExportPdfAsync(document, outputStream, settings)`，不需要打印机；每个所选页只导出一份，目标流不关闭。

**旧 API 迁移：** 原 `PrintAsync(document, settings, cancellationToken)` 的底层提交语义已移到 `SubmitAsync`。现在 `PrintAsync(document, options)` 默认预览；旧代码若需直接打印，应显式添加 `showPreview: false`，取消令牌使用命名参数 `cancellationToken:`。

## 平台与结果

Windows 使用 GDI / 系统驱动；macOS 使用随包部署的 AppKit / PDFKit 桥接；Linux 使用系统 libcups.so.2。包不依赖 TheOne 主控件库，不读取 One.* 资源。消费者显式注册 PrintingToolsTheme，密度由本包 PrintDensity 控制。

已安装设备、驱动、系统打印服务和权限仍由宿主环境提供。App Sandbox、Flatpak、Snap 权限需另行验证。“已提交”不等于真实出纸；提交开始后取消为尽力操作，错误结果不明确时不要自动重试。PDF 导出取消可能留下不完整目标文件，临时打印文件会清理。

PreviewHost 默认打开最简 MVVM 示例页：选择 PDF（单选）/ 图片（单张或多张）/ 网络 URL / Visual / 报表 / 混合源，填写可选份数和打印机 ID，切换“先打开预览”，点击“打印”。网络 URL 每行一个，先点击“使用网络源”再打印。ViewModel 只调用注入的 `IPrintService.PrintAsync`；View 提供原生文件选择和控件来源，URL 输入绑定 ViewModel。打印解析与释放全部由库负责。取消选择或选择失败保留原来源，操作过程中禁止重入。

2026-10-16 已在 macOS arm64 使用全新缓存从 nuget.org 还原 `1.0.0-preview.3`，PreviewHost Release 构建 0 警告、0 错误。真实窗口验证公网 HTTPS PDF 正文及缩略图正常，5 张公网 PNG/JPEG/WebP 图片由默认 5 页切换四宫格后为 2 页，彩色内容和顺序正确。验证仅打开并取消预览，未提交真实打印任务。

本次线上包验证使用托管宿主；此前本地包已完成 macOS arm64 NativeAOT 实跑，线上包未重复验证 NativeAOT。Windows / Linux 原生运行、真实出纸和沙箱部署尚未验证。macOS 临时宿主的原生文件选择器曾出现 Open 禁用，网络 URL 预览通过不代表该本地文件选择问题已解决。

PreviewHost 保留最简 MVVM 示例；历史专项验证入口已移除。本地 NuGet 消费和 NativeAOT 命令见 [开发说明](DEVELOPMENT.md)。Debug / Release 保持 WinExe。
