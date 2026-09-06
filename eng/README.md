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

安装脚本将在实施计划任务 1 中加入；执行前会再次向用户说明将要写入的 G 盘路径和环境变量。
