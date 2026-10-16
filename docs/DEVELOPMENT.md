# 开发与本地包消费

项目目标为 .NET 10 / Avalonia 12.1.4。公开仓库保留控件源码、最简 MVVM 示例和文档；历史一次性专项测试、截图和缓存已移除。

## 库源码与 NuGet 演示

```sh
dotnet build src/TheOne.PrintingTools.Avalonia/TheOne.PrintingTools.Avalonia.csproj --artifacts-path .preview/work/library-artifacts
dotnet build design/PreviewHost/PreviewHost.csproj --artifacts-path .preview/work/artifacts
dotnet run --project design/PreviewHost/PreviewHost.csproj
```

PreviewHost 的 Debug / Release 均为 WinExe。入口为 `Program.cs`，示例为 `PrintingDemoView.axaml` / `PrintingDemoViewModel.cs`。View 提供原生文件选择、网络 URL 输入和 Visual 来源，ViewModel 通过注入的 IPrintService 调用打印。网络源每行一个地址，点击“使用网络源”后，打印时才下载。默认启用预览；关闭预览会提交到真实打印队列。

PreviewHost 仅保留固定版本 PackageReference，不提供源码 ProjectReference 或引用模式开关，也不导入库包版本配置。普通还原、构建和运行均消费 NuGet 包；库源码改动不会自动进入演示。

## 线上包状态与复验

截至 2026-10-16，[1.0.0-preview.3](https://www.nuget.org/packages/TheOne.PrintingTools.Avalonia/1.0.0-preview.3) 已完成发布、验证和索引，可从 nuget.org 正常还原。PreviewHost 固定消费此版本。线上包的源码提交与发布信息见 [发布说明](PUBLISHING.md)。

本次使用全新 NUGET_PACKAGES 目录、禁用 HTTP 缓存并清空回退目录，显式指定官方源。下载包的 `.nupkg.metadata` 确认来源为 `https://api.nuget.org/v3/index.json`，assets 中本库类型为 package；构建输出 DLL 与线上包内 DLL 完全一致。

| macOS arm64 线上包验证 | 结果 |
| --- | --- |
| PreviewHost Release 还原与构建 | 成功，0 警告、0 错误 |
| 公网 HTTPS PDF | 1 页，正文与缩略图正常显示 “Dummy PDF file” |
| 5 个公网 HTTPS 图片地址 | PNG/JPEG/WebP 均加载，默认每页一张，共 5 页 |
| 彩色与四宫格 | 共 2 页，第一页 4 张、第二页 1 张，内容与输入顺序一致 |

验证全程仅预览并取消，未提交真实打印任务。发布前本地包已完成 macOS arm64 NativeAOT 实跑；这次线上包回归使用托管宿主，没有重新运行 NativeAOT 或完整异常专项。Windows / Linux 原生运行、真实出纸及沙箱部署仍未验证。

在仓库根目录复验；首次检查线上来源时，请为 NUGET_PACKAGES 选择此前未使用的目录：

```sh
NUGET_PACKAGES="$PWD/.preview/work/online-nuget-cache" dotnet restore design/PreviewHost/PreviewHost.csproj --source https://api.nuget.org/v3/index.json --no-http-cache --force -p:RestoreFallbackFolders= --artifacts-path .preview/work/online-artifacts
NUGET_PACKAGES="$PWD/.preview/work/online-nuget-cache" dotnet build design/PreviewHost/PreviewHost.csproj -c Release --no-restore --artifacts-path .preview/work/online-artifacts
dotnet .preview/work/online-artifacts/bin/PreviewHost/release/PreviewHost.dll
```

在“网络 PDF / 图片”中先单独输入下面的 PDF 地址，选择网络源后打开预览：

```text
https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf
```

再替换为下面 5 行图片地址，选择网络源、打开预览并切换为彩色及四宫格，检查两页内容：

```text
https://httpbin.org/image/png
https://httpbin.org/image/jpeg
https://httpbin.org/image/webp
https://httpbin.org/image/png?copy=2
https://httpbin.org/image/jpeg?copy=2
```

历史测试文件、构建缓存和日志已清理。复验时先还原、构建生成新的输出，再运行宿主。

## 打包与独立消费

```sh
dotnet pack src/TheOne.PrintingTools.Avalonia/TheOne.PrintingTools.Avalonia.csproj -c Release --artifacts-path .preview/work/pack-artifacts -o .preview/work/packages
NUGET_PACKAGES="$PWD/.preview/work/nuget-cache" dotnet build design/PreviewHost/PreviewHost.csproj -c Release --source "$PWD/.preview/work/packages" --source https://api.nuget.org/v3/index.json --artifacts-path .preview/work/consumer-artifacts
```

使用全新 NUGET_PACKAGES 目录避免同版本旧缓存。检查 project.assets.json 中本包类型为 package，确认消费 DLL 与 nupkg 内 DLL 一致，并检查 nuspec 仓库地址、版本、README、许可证、主题和 RID 原生资源。

本地包版本需与 PreviewHost 固定的引用版本匹配；调整测试版本时显式更新 PackageReference，不切换到源码引用。库的 PackageVersions.props 与演示消费版本分别维护。

后续源码改动仍需先使用本地源和隔离缓存验证，再按发布流程发布新版本；不能覆盖已发布的 `1.0.0-preview.3`，也不能用线上旧包的验证结果代替新源码验证。

## NativeAOT

在对应平台安装原生编译工具，再发布包消费模式的 PreviewHost。例如 macOS arm64：

```sh
NUGET_PACKAGES="$PWD/.preview/work/nuget-cache" dotnet publish design/PreviewHost/PreviewHost.csproj -c Release -r osx-arm64 -p:PublishAot=true --source "$PWD/.preview/work/packages" --source https://api.nuget.org/v3/index.json --artifacts-path .preview/work/aot-artifacts -o .preview/work/aot
```

macOS 的 clang 与 SDK 应来自同一 Xcode。发布后实际运行对应平台产物，核对 PDFium 和 macOS 桥接库加载。跨 RID 构建不能代替平台实跑。

PDFium 使用 bblanchon.PDFium.Win32 / macOS / Linux 152.0.7961，保持 SkiaSharp 3.119.4。消费者不需要编译 macOS Objective-C 桥接；桥接源码和维护方法见 src/TheOne.PrintingTools.Avalonia/native/README.md。

## 人工检查

选择一个 PDF、单张与多张图片，确认 PDF 原页数和图片布局分页；检查取消选择保留来源、Visual 只捕获已布局区域。图片设置应在 Light / Dark、三档密度和窄窗口可用。提交真实设备打印应由操作者明确决定，不要把进入队列当作真实出纸证明。

网络专项检查单个 PDF URL、多张图片 URL、无扩展名链接、HTTP 错误、下载超限和下载取消。多张图片沿用布局、填充、对齐和排版选项；PDF 不允许与图片混选。下载失败时应保留错误状态，不打开部分文档的预览，也不提交打印。

当前已知 macOS 临时验证宿主中的原生选择器可能出现 Open 禁用；需要在实际应用宿主复核。Windows / Linux 原生运行、真实出纸及沙箱部署仍需相应环境验证。
