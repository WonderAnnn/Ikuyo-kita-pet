# Ikuyo Pet

Ikuyo Pet 是一个面向 Windows 的离线健康提醒与有效工作时间记录工具。它默认通过透明、可拖动的桌宠提醒喝水和活动；关闭桌宠后，提醒会转为安静的 Windows 系统通知。

> 当前项目处于早期开发阶段，不是医疗器械，也不提供诊断或治疗建议。提醒间隔和活动方式应以个人感受及医生意见为准。

## 第一阶段范围

当前代码已经包含：

- 喝水与活动提醒、完成 / 稍后 / 跳过反馈及本地日志；
- 界面内可编辑的提醒规则：喝水/活动间隔区间、建议活动时长、生效时段、免打扰时段与提醒文案，保存后最多 30 秒生效，无需重启或改代码；
- 状态诊断面板：当前前台是否在白名单、本轮已累计有效工作、下一次喝水/活动预计时间，以及“为什么没有计时或提醒”的具体原因；
- 数据备份与恢复：每天首次启动自动备份、可配置保留份数、手动备份、恢复（自动保留恢复前安全副本）、全部数据 JSON 导出；
- 透明可拖动桌宠、语言气泡，以及关闭桌宠后的 Windows 通知路由；
- 系统托盘入口、暂停提醒和今日记录界面；
- 可替换桌宠皮肤的清单规范；
- 白名单应用有效工作时间统计；
- 设置页读取当前运行进程（进程名/PID），支持一键填入白名单并用 5 秒测试核对前台、解锁和最近输入条件；
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

应用使用嵌入式 SQLite 文件保存设置和日志，不需要安装 MySQL、SQLite 服务或 SQLite 管理软件，也不要求管理员权限。首次运行时会在当前 Windows 用户的本地应用数据目录中创建数据库。每天首次启动会自动在数据库同级的 `backups` 目录写入一份备份，默认保留最近 14 份，可在设置页调整或手动备份、恢复、导出 JSON。

## 隐私边界

第一阶段默认离线工作，个人数据保存在本机，不上传云端。它不会记录：

- 睡眠时间或医疗报告；
- 窗口标题、文档内容或文件名；
- 键盘输入、鼠标输入内容；
- 屏幕截图或屏幕画面。

工作统计只保留白名单进程标识、时间段与累计时长。删除本地数据库即可删除这些本地记录；删除前请自行确认是否需要备份。

## 桌宠皮肤与版权

仓库现在包含项目作者授权公开发布的原创资源：`assets/skins/kita-original/`、`assets/bubbles/kita/`、`assets/interactions/kita-click.zh-CN.json` 和 `assets/branding/icon/`。这些素材可以被 Git 跟踪并随 Release 一起分发，但素材授权与代码许可证相互独立，具体边界见各目录的 `NOTICE.md`。

如果素材中出现受原作启发的角色名称、设定或同人表达，本项目仍是非官方同人软件，与原作版权方、出版社、动画制作方或音乐版权方无关联；项目不主张拥有第三方原作的角色、名称、世界观或商标权利。若收到可信版权、署名或下架通知，将暂停分发并及时核查、替换或删除有争议的文件。

用户可以在遵守素材来源许可的前提下，在本机导入自定义皮肤。个人覆盖素材仍放在被 Git 忽略的 `local-assets/`、`local-skins/`，不会自动进入公开发布包。默认皮肤目录和清单说明见 `assets/skins/default/README.md`，清单格式见 `schemas/skin-manifest.schema.json`。
## 本机私有素材与点击互动

`local-assets/` 是只供当前电脑构建和发布使用的目录，已被 Git 忽略。请只放入自己有权使用的素材：

```text
local-assets/
  branding/icon/icon256.ico
  interactions/ikuyo-click.zh-CN.json
```

公开互动文案位于 `assets/interactions/kita-click.zh-CN.json`，构建后映射为 `interactions/kita-click.json`；通用回退文案位于 `assets/interactions/default/click.zh-CN.json`，构建后映射为 `interactions/default/click.json`。本机覆盖文案仍可放在 `local-assets/interactions/ikuyo-click.zh-CN.json`，构建后映射为 `interactions/ikuyo-click.json`。覆盖文案缺失或损坏时，程序会静默使用公开文案，不阻止启动。

互动文件遵循 [`schemas/pet-interaction.schema.json`](schemas/pet-interaction.schema.json)。最小示例：

```json
{
  "character": "自定义角色",
  "event": "click",
  "language": "zh-CN",
  "messages": [
    { "id": 1, "text": "今天也按自己的节奏来吧～", "mood": "cheerful" }
  ]
}
```

`text` 仅作为纯文本显示；程序不执行其中的 HTML、Markdown、URL、命令或模板表达式。单击桌宠会随机显示一条文案，连续两次不会选择同一 ID；首次点击立即显示，随后 5 秒内的频繁点击只保留最后一条文案，不叠加气泡或计时器；普通互动约 4 秒后以不超过 0.5 秒的渐变收起，也不会写入 SQLite、健康日志或 CSV。

## 窗口与托盘行为

正常启动会直接显示主页面。点击主窗口的最小化或关闭按钮只会隐藏主页面和任务栏按钮，后台提醒、有效工作统计、系统托盘与已启用桌宠继续运行。单击托盘图标或选择“打开今日”可恢复窗口；只有托盘菜单“退出 Ikuyo Pet”才结束进程。

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
assets/skins/kita-original/ 项目作者授权公开的原创皮肤
assets/bubbles/             三套气泡主题及原创源素材
assets/interactions/        公开互动文案
assets/branding/icon/       发布与应用图标
local-assets/, local-skins/ 本机私有覆盖（Git 忽略）
schemas/                    皮肤清单 JSON Schema
docs/superpowers/           设计规格与实现计划
```

主要设计资料：

- [`2026-09-06-ikuyo-pet-design.md`](docs/superpowers/specs/2026-09-06-ikuyo-pet-design.md)
- [`2026-09-06-ikuyo-pet-mvp.md`](docs/superpowers/plans/2026-09-06-ikuyo-pet-mvp.md)
- [`2026-09-07-presentation-and-pet-design.md`](docs/superpowers/specs/2026-09-07-presentation-and-pet-design.md)
- [`2026-09-07-main-window-tray-log-design.md`](docs/superpowers/specs/2026-09-07-main-window-tray-log-design.md)
- [`2026-09-11-reliability-rules-diagnostics-backup-design.md`](docs/superpowers/specs/2026-09-11-reliability-rules-diagnostics-backup-design.md)

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
也可以使用仓库脚本发布和检查。`publish-latest.ps1` 会原子替换固定的 `artifacts/publish/latest` 目录，并写入版本、构建时间、Git 提交和资源 SHA-256 校验；桌面快捷方式应始终指向该目录中的 `IkuyoPet.exe`：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-latest.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\eng\publish-local.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\eng\verify-dev.ps1 -PublishRoot .\artifacts\publish\latest
```

替换 latest 前请退出正在运行的 latest\IkuyoPet.exe；脚本检测到目录被锁定时会回滚并给出明确提示。

脚本默认使用 `G:\IkuyoPetDev\dotnet\dotnet.exe` 和 `G:\IkuyoPetDev\nuget\packages`；也可以通过 `IKUYO_PET_DOTNET` 指定其他 .NET 10 SDK。公开皮肤、气泡、互动文案和图标来自 `assets/`，全新克隆后即可构建。`local-assets/`、`local-skins/`、`tmp/`、`output/` 和 `artifacts/` 继续被 `.gitignore` 忽略，不会被上传或复制到公开 Release。

## 医疗说明

Ikuyo Pet 只帮助执行和记录用户自行设置的日常提醒。腰椎、睡眠、饮水或活动相关的具体数值不应从软件默认值推断；如果医生给出新的建议，可以在“提醒规则”页直接调整间隔、时长、生效时段和免打扰时段，保存后立即生效，不影响项目的大方向。

## 开源许可

项目代码采用 [MIT License](LICENSE)。第三方依赖和用户导入的皮肤分别遵循其自身许可证。
