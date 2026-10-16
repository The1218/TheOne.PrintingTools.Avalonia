# 打包与发布

源码仓库：[TheOne.PrintingTools.Avalonia](https://github.com/The1218/TheOne.PrintingTools.Avalonia)

NuGet 包：[TheOne.PrintingTools.Avalonia](https://www.nuget.org/packages/TheOne.PrintingTools.Avalonia)

## 1.0.0-preview.3 发布状态

截至 2026-10-16，[1.0.0-preview.3](https://www.nuget.org/packages/TheOne.PrintingTools.Avalonia/1.0.0-preview.3) 已通过 NuGet 网站上传并完成服务端验证和索引，可正常安装与还原。

包内 RepositoryCommit 为 [`087010fb810fc52154072130f5a0e9d8c6ef8293`](https://github.com/The1218/TheOne.PrintingTools.Avalonia/commit/087010fb810fc52154072130f5a0e9d8c6ef8293)，对应网络打印功能的发布源码。线上包包含 NuGet 签名，上传原包的所有条目与线上包一致。

已使用全新缓存从官方源还原 PreviewHost，确认实际消费 DLL 与线上包一致；macOS arm64 托管宿主构建无警告、无错误，公网 PDF 和 5 张公网图片预览通过，四宫格正确生成 2 页。线上包没有重复运行 NativeAOT，也未提交真实打印任务。验证边界及复验步骤见 [开发说明](DEVELOPMENT.md)。

## 后续版本发布流程

版本由 PackageVersions.props 独立管理，当前为 `1.0.0-preview.3`。该版本已经发布，不能再次覆盖；升级时先确定新的版本号，再重新构建和验证。

1. 只提交源码、演示、公共文档、构建配置与许可证；不提交 .preview、bin / obj、Agent 配置和凭据。
2. 按 [开发说明](DEVELOPMENT.md) 构建、打包、验证独立 NuGet 消费及当前平台 NativeAOT。
3. 检查 nupkg 的仓库地址、版本、作者、README、许可证、依赖和原生资源。README 明确预览状态及已知限制。
4. 提交并推送源码后，从干净工作区重新打包，核对 nuspec 的 RepositoryCommit 对应该次源码提交，再发布对应的新版本包。API Key 通过本机安全凭据或临时环境变量提供，不写入源码、日志或提交。

下面的 CLI 示例用于后续新版本；先将 `PACKAGE_PATH` 设置为待发布的新版本 nupkg 路径，再执行：

```sh
git push -u origin main
dotnet nuget push "$PACKAGE_PATH" --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY"
```

API Key 需要该包 ID 的 Push 权限。也可登录 NuGet 网站上传对应的 nupkg 并提交。本次 `1.0.0-preview.3` 使用网站上传。

发布后确认包页面、版本与仓库信息，再从官方源使用新缓存执行实际还原和消费验证。上传成功后仍可能等待服务端验证和索引；只有还原验证通过后，才能确认该版本可安装。
