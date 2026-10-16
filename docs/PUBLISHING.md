# 打包与发布

源码仓库：https://github.com/The1218/TheOne.PrintingTools.Avalonia

NuGet 包：https://www.nuget.org/packages/TheOne.PrintingTools.Avalonia

版本由 PackageVersions.props 独立管理，当前为 `1.0.0-preview.2`。NuGet 已发布的版本不能覆盖；升级时先确定新的版本号，再重新构建和验证。

1. 只提交源码、演示、公共文档、构建配置与许可证；不提交 .preview、bin / obj、Agent 配置和凭据。
2. 按 [开发说明](DEVELOPMENT.md) 构建、打包、验证独立 NuGet 消费及当前平台 NativeAOT。
3. 检查 nupkg 的仓库地址、版本、作者、README、许可证、依赖和原生资源。README 明确预览状态及已知限制。
4. 提交并推送源码后，再发布对应的 NuGet 包。API Key 通过本机安全凭据或临时环境变量提供，不写入源码、日志或提交。

```sh
git push -u origin main
dotnet nuget push .preview/release/packages/TheOne.PrintingTools.Avalonia.1.0.0-preview.2.nupkg --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY"
```

API Key 需要该包 ID 的 Push 权限。发布成功后确认 nuget.org 包页面、版本与仓库链接；索引可能有延迟。未执行 push 或服务端未接受前，只能称为“已打包”，不能称为“已发布”。
