# 开发环境目录

Ikuyo Pet 的源码保存在 `F:\Health`，可下载的开发工具和缓存统一放在 `G:\IkuyoPetDev`：

```text
G:\IkuyoPetDev\
  dotnet\             .NET 10 SDK
  nuget\packages\     NuGet 全局包缓存
  tools\sqlite\       可选的 SQLite 命令行工具
  downloads\           安装包下载缓存
```

应用运行不依赖 SQLite 服务或 SQLite 命令行工具。`Microsoft.Data.Sqlite` 会通过 NuGet 随应用一起发布。

开发工具安装由用户自行控制；发布脚本只读取已存在的 G 盘 .NET 和 NuGet 缓存，不会安装系统服务。

## 本地验证与发布

从仓库根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\eng\verify-dev.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\eng\publish-local.ps1
```

`publish-local.ps1` 现在发布包含 `assets/` 公开资源的自包含 win-x64 应用，并调用 `verify-dev.ps1` 校验发布目录。公开皮肤、气泡、互动文案和图标均来自 `assets/`；`local-assets/`、`local-skins/`、`tmp/`、`output/` 和 `artifacts/` 继续保持本机私有或生成物属性，不会被复制到公开发布包。固定版本发布使用 `scripts/publish-latest.ps1`，它原子替换 `artifacts/publish/latest` 并写入构建信息和资源 SHA-256。