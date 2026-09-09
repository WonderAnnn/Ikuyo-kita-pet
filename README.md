# Ikuyo Pet

Ikuyo Pet 是一个面向 Windows 的离线健康提醒与有效工作时间记录工具。它默认通过透明、可拖动的桌宠提醒喝水和活动；关闭桌宠后，提醒会转为安静的 Windows 系统通知。

> 当前项目处于早期开发阶段，不是医疗器械，也不提供诊断或治疗建议。提醒间隔和活动方式应以个人感受及医生意见为准。

## 第一阶段范围

当前代码已经包含：

- 喝水与活动提醒、完成 / 稍后 / 跳过反馈及本地日志；
- 透明可拖动桌宠、语言气泡，以及关闭桌宠后的 Windows 通知路由；
- 系统托盘入口、暂停提醒和今日记录界面；
- 可替换桌宠皮肤的清单规范；
- 白名单应用有效工作时间统计；
- 当前用户级开机启动设置。

有效工作时间只在以下条件同时满足时累计：白名单应用位于前台、电脑未锁屏、最近 5 分钟内有人机操作。应用仅识别进程名以匹配白名单；后台运行和离开电脑不计时。

应用内 AI 桌宠生成工作室属于第二阶段，不在当前离线 MVP 中。第一阶段只支持导入用户已有、且有权使用的本地皮肤资源。

## 本地运行

开发环境要求：

- Windows 10 版本 2004（build 19041）或更高版本；
- .NET 10 SDK。

```powershell
dotnet restore .\IkuyoPet.sln
dotnet build .\IkuyoPet.sln --configuration Release --no-restore -warnaserror
dotnet test --solution .\IkuyoPet.sln --configuration Release --no-build --no-restore
dotnet run --project .\src\IkuyoPet.App\IkuyoPet.App.csproj
```

应用使用嵌入式 SQLite 文件保存设置和日志，不需要安装 MySQL、SQLite 服务或 SQLite 管理软件，也不要求管理员权限。首次运行时会在当前 Windows 用户的本地应用数据目录中创建数据库。

## 隐私边界

第一阶段默认离线工作，个人数据保存在本机，不上传云端。它不会记录：

- 睡眠时间或医疗报告；
- 窗口标题、文档内容或文件名；
- 键盘输入、鼠标输入内容；
- 屏幕截图或屏幕画面。

工作统计只保留白名单进程标识、时间段与累计时长。删除本地数据库即可删除这些本地记录；删除前请自行确认是否需要备份。

## 桌宠皮肤与版权

开源仓库不会包含“喜多郁代”或其他第三方商业动漫角色的图片、立绘、动画、语音等素材。MIT 许可证只覆盖本项目代码和仓库中明确标注可再分发的原创资源，不会授予任何第三方角色或作品的权利。

用户可以在遵守素材来源许可的前提下，在本机导入自定义皮肤。默认皮肤目录和清单说明见 [`assets/skins/default/README.md`](assets/skins/default/README.md)，清单格式见 [`schemas/skin-manifest.schema.json`](schemas/skin-manifest.schema.json)。

## 项目结构

```text
src/
  IkuyoPet.App/             WPF 主程序、今日界面与系统托盘
  IkuyoPet.Core/            提醒、工作统计和展示领域逻辑
  IkuyoPet.Infrastructure/  SQLite、Windows 通知与系统集成
  IkuyoPet.Pet/             透明桌宠窗口和气泡呈现
tests/
  IkuyoPet.Core.Tests/
  IkuyoPet.Infrastructure.Tests/
assets/skins/default/       可再分发默认皮肤的占位与清单
schemas/                    皮肤清单 JSON Schema
docs/superpowers/           设计规格与实现计划
```

主要设计资料：

- [`2026-09-06-ikuyo-pet-design.md`](docs/superpowers/specs/2026-09-06-ikuyo-pet-design.md)
- [`2026-09-06-ikuyo-pet-mvp.md`](docs/superpowers/plans/2026-09-06-ikuyo-pet-mvp.md)
- [`2026-09-07-presentation-and-pet-design.md`](docs/superpowers/specs/2026-09-07-presentation-and-pet-design.md)
- [`2026-09-07-main-window-tray-log-design.md`](docs/superpowers/specs/2026-09-07-main-window-tray-log-design.md)

## 发布

发布为自包含 Windows 应用时，目标电脑无需单独安装 .NET 或数据库环境：

```powershell
dotnet publish .\src\IkuyoPet.App\IkuyoPet.App.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output .\publish\win-x64
```

当前仓库尚未提供签名安装包。测试阶段建议直接运行开发构建或自行发布的目录。

## 医疗说明

Ikuyo Pet 只帮助执行和记录用户自行设置的日常提醒。腰椎、睡眠、饮水或活动相关的具体数值不应从软件默认值推断；如果医生给出新的建议，可以在后续版本调整规则，不影响项目的大方向。

## 开源许可

项目代码采用 [MIT License](LICENSE)。第三方依赖和用户导入的皮肤分别遵循其自身许可证。
