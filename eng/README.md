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

`publish-local.ps1` 生成 `artifacts/publish/win-x64/IkuyoPet.exe`，使用自包含 `win-x64` 发布，不要求目标机器另装 .NET 或 SQLite 服务。发布前会还原 NuGet 包；网络不可用时应确保 `G:\IkuyoPetDev\nuget\packages` 已有缓存，并可传入 `-SkipRestore`。脚本只复制 Git 忽略的 `local-skins/`，不会复制数据库、日志或测试输出。
