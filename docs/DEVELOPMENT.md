# 开发与本地包消费

项目目标为 .NET 10 / Avalonia 12.1.4。公开仓库保留控件源码、最简 MVVM 示例和文档；历史一次性专项测试、截图和缓存已移除。

## 源码构建

```sh
dotnet build design/PreviewHost/PreviewHost.csproj --artifacts-path .preview/work/artifacts
dotnet run --project design/PreviewHost/PreviewHost.csproj
```

PreviewHost 的 Debug / Release 均为 WinExe。入口为 `Program.cs`，示例为 `PrintingDemoView.axaml` / `PrintingDemoViewModel.cs`。View 提供原生文件选择和 Visual 来源，ViewModel 通过注入的 IPrintService 调用打印。默认启用预览；关闭预览会提交到真实打印队列。

## 打包与独立消费

```sh
dotnet pack src/TheOne.PrintingTools.Avalonia/TheOne.PrintingTools.Avalonia.csproj -c Release --artifacts-path .preview/work/pack-artifacts -o .preview/work/packages
NUGET_PACKAGES="$PWD/.preview/work/nuget-cache" dotnet build design/PreviewHost/PreviewHost.csproj -c Release -p:UsePackageReferences=true --source "$PWD/.preview/work/packages" --source https://api.nuget.org/v3/index.json --artifacts-path .preview/work/consumer-artifacts
```

使用全新 NUGET_PACKAGES 目录避免同版本旧缓存。检查 project.assets.json 中本包类型为 package，确认消费 DLL 与 nupkg 内 DLL 一致，并检查 nuspec 仓库地址、版本、README、许可证、主题和 RID 原生资源。

## NativeAOT

在对应平台安装原生编译工具，再发布包消费模式的 PreviewHost。例如 macOS arm64：

```sh
NUGET_PACKAGES="$PWD/.preview/work/nuget-cache" dotnet publish design/PreviewHost/PreviewHost.csproj -c Release -r osx-arm64 -p:UsePackageReferences=true -p:PublishAot=true --source "$PWD/.preview/work/packages" --source https://api.nuget.org/v3/index.json --artifacts-path .preview/work/aot-artifacts -o .preview/work/aot
```

macOS 的 clang 与 SDK 应来自同一 Xcode。发布后实际运行对应平台产物，核对 PDFium 和 macOS 桥接库加载。跨 RID 构建不能代替平台实跑。

PDFium 使用 bblanchon.PDFium.Win32 / macOS / Linux 152.0.7961，保持 SkiaSharp 3.119.4。消费者不需要编译 macOS Objective-C 桥接；桥接源码和维护方法见 src/TheOne.PrintingTools.Avalonia/native/README.md。

## 人工检查

选择一个 PDF、单张与多张图片，确认 PDF 原页数和图片布局分页；检查取消选择保留来源、Visual 只捕获已布局区域。图片设置应在 Light / Dark、三档密度和窄窗口可用。提交真实设备打印应由操作者明确决定，不要把进入队列当作真实出纸证明。

当前已知 macOS 临时验证宿主中的原生选择器可能出现 Open 禁用；需要在实际应用宿主复核。Windows / Linux 原生运行、真实出纸及沙箱部署仍需相应环境验证。
