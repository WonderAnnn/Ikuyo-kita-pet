# Ikuyo Pet 离线 MVP 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法跟踪进度。

**目标：** 构建一个低打扰、默认桌宠提醒、可切换 Windows 原生通知、能记录提醒响应与白名单应用有效工作时长的离线 Windows 应用。

**架构：** WPF 只负责窗口与交互，`IkuyoPet.Core` 保存可独立测试的提醒状态机和时间计算，`IkuyoPet.Infrastructure` 适配 SQLite 与 Windows API，`IkuyoPet.Pet` 承载透明可拖动桌宠。呈现层通过接口切换桌宠和原生通知，二者共用同一提醒事件与日志仓储。

**技术栈：** C# 14、.NET 10 LTS、WPF、Windows App SDK 2.4.0、Microsoft.Data.Sqlite 10.0.11、H.NotifyIcon.Wpf 2.4.1、xUnit.net v3 4.0.0、Microsoft.NET.Test.Sdk 18.9.0、coverlet.collector 10.0.1。

---

## 文件结构

```text
F:\Health\
  IkuyoPet.sln
  global.json
  Directory.Build.props
  Directory.Packages.props
  .editorconfig
  .gitignore
  LICENSE
  README.md
  eng/
    toolchain.psd1
    setup-dev.ps1
    verify-dev.ps1
  docs/superpowers/specs/2026-09-06-ikuyo-pet-design.md
  docs/superpowers/plans/2026-09-06-ikuyo-pet-mvp.md
  src/
    IkuyoPet.App/
      IkuyoPet.App.csproj
      App.xaml
      App.xaml.cs
      MainWindow.xaml
      MainWindow.xaml.cs
    IkuyoPet.Core/
      IkuyoPet.Core.csproj
      Reminders/ReminderModels.cs
      Reminders/ReminderStateMachine.cs
      WorkTracking/WorkTrackingModels.cs
      WorkTracking/WorkSessionAccumulator.cs
      Presentation/IReminderPresenter.cs
      Storage/IEventRepository.cs
    IkuyoPet.Infrastructure/
      IkuyoPet.Infrastructure.csproj
      Storage/DatabaseMigrator.cs
      Storage/SqliteEventRepository.cs
      Windows/ForegroundActivityProbe.cs
      Windows/WindowsNotificationPresenter.cs
      Windows/TrayIconHost.cs
    IkuyoPet.Pet/
      IkuyoPet.Pet.csproj
      PetWindow.xaml
      PetWindow.xaml.cs
      PetReminderPresenter.cs
      Skins/SkinManifest.cs
      Skins/SkinPackageValidator.cs
  tests/
    IkuyoPet.Core.Tests/
      IkuyoPet.Core.Tests.csproj
      Reminders/ReminderStateMachineTests.cs
      WorkTracking/WorkSessionAccumulatorTests.cs
    IkuyoPet.Infrastructure.Tests/
      IkuyoPet.Infrastructure.Tests.csproj
      Storage/SqliteEventRepositoryTests.cs
      Skins/SkinPackageValidatorTests.cs
  assets/skins/default/
    manifest.json
    README.md
  schemas/skin-manifest.schema.json
  .github/workflows/windows-ci.yml
```

## 任务 1：固定 G 盘工具链与创建解决方案骨架

**文件：**
- 创建：`global.json`
- 创建：`Directory.Build.props`
- 创建：`Directory.Packages.props`
- 创建：`.editorconfig`
- 创建：`eng/toolchain.psd1`
- 创建：`eng/setup-dev.ps1`
- 创建：`eng/verify-dev.ps1`
- 创建：`IkuyoPet.sln`
- 创建：四个 `src/*/*.csproj` 和两个 `tests/*/*.csproj`

- [x] **步骤 1：写入固定工具目录配置**

```powershell
@{
    Root = 'G:\IkuyoPetDev'
    DotNetRoot = 'G:\IkuyoPetDev\dotnet'
    NuGetPackages = 'G:\IkuyoPetDev\nuget\packages'
    SqliteTools = 'G:\IkuyoPetDev\tools\sqlite'
}
```

- [x] **步骤 2：实现开发环境安装脚本**

`eng/setup-dev.ps1` 必须先创建上述目录，再从 `https://dot.net/v1/dotnet-install.ps1` 下载官方脚本并以 `-Channel 10.0 -InstallDir G:\IkuyoPetDev\dotnet` 安装 SDK；随后把用户级 `NUGET_PACKAGES` 设置为 `G:\IkuyoPetDev\nuget\packages`。SQLite CLI 仅作为人工查看数据库的可选工具，应用运行不依赖它。

- [x] **步骤 3：实现环境验证脚本**

```powershell
$config = Import-PowerShellDataFile "$PSScriptRoot\toolchain.psd1"
$dotnet = Join-Path $config.DotNetRoot 'dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { throw 'Ikuyo Pet .NET SDK is not installed.' }
& $dotnet --version
if ($LASTEXITCODE -ne 0) { throw 'Ikuyo Pet .NET SDK verification failed.' }
```

- [x] **步骤 4：创建项目并建立单向引用**

```powershell
& G:\IkuyoPetDev\dotnet\dotnet.exe new sln --name IkuyoPet
& G:\IkuyoPetDev\dotnet\dotnet.exe new wpf --name IkuyoPet.App --output src\IkuyoPet.App --framework net10.0-windows
& G:\IkuyoPetDev\dotnet\dotnet.exe new classlib --name IkuyoPet.Core --output src\IkuyoPet.Core --framework net10.0
& G:\IkuyoPetDev\dotnet\dotnet.exe new classlib --name IkuyoPet.Infrastructure --output src\IkuyoPet.Infrastructure --framework net10.0-windows
& G:\IkuyoPetDev\dotnet\dotnet.exe new classlib --name IkuyoPet.Pet --output src\IkuyoPet.Pet --framework net10.0-windows
```

允许的引用方向：`App -> Core, Infrastructure, Pet`；`Infrastructure -> Core`；`Pet -> Core`；Core 不引用任何其他项目。

- [x] **步骤 5：验证骨架并提交**

运行：`G:\IkuyoPetDev\dotnet\dotnet.exe build IkuyoPet.sln --no-restore`

预期：安装并还原包前若缺少 `project.assets.json`，命令明确失败；执行 `dotnet restore` 后再次构建必须为 0 error、0 warning。

Commit：`chore: scaffold Ikuyo Pet solution`

## 任务 2：用 TDD 实现提醒状态机

**文件：**
- 创建：`tests/IkuyoPet.Core.Tests/Reminders/ReminderStateMachineTests.cs`
- 创建：`src/IkuyoPet.Core/Reminders/ReminderModels.cs`
- 创建：`src/IkuyoPet.Core/Reminders/ReminderStateMachine.cs`

- [x] **步骤 1：编写失败测试**

```csharp
[Fact]
public void NoResponseOnThirdAttemptEndsAsUnanswered()
{
    var machine = new ReminderStateMachine(maxAttempts: 3);
    var state = ReminderState.Pending(attempt: 3);

    var result = machine.Apply(state, ReminderAction.NoResponse);

    Assert.Equal(ReminderOutcome.Unanswered, result.Outcome);
    Assert.False(result.ShouldRetry);
}
```

- [x] **步骤 2：运行测试并确认因类型尚不存在而失败**

运行：`dotnet test tests/IkuyoPet.Core.Tests --filter NoResponseOnThirdAttemptEndsAsUnanswered`

预期：FAIL，编译器报告 `ReminderStateMachine` 未定义。

- [x] **步骤 3：实现最小状态模型和转换**

```csharp
public sealed class ReminderStateMachine(int maxAttempts)
{
    public ReminderTransition Apply(ReminderState state, ReminderAction action) => action switch
    {
        ReminderAction.Complete => new(ReminderOutcome.Completed, false),
        ReminderAction.Snooze => new(ReminderOutcome.Snoozed, true),
        ReminderAction.Skip => new(ReminderOutcome.Skipped, false),
        ReminderAction.NoResponse when state.Attempt >= maxAttempts => new(ReminderOutcome.Unanswered, false),
        ReminderAction.NoResponse => new(ReminderOutcome.None, true),
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
}
```

- [x] **步骤 4：增加完成、稍后、跳过和前两次无响应测试并运行全部 Core 测试**

运行：`dotnet test tests/IkuyoPet.Core.Tests`

预期：全部 PASS，0 failed。

- [x] **步骤 5：提交**

Commit：`feat: add reminder state machine`

## 任务 3：用 TDD 实现有效工作时间累计

**文件：**
- 创建：`tests/IkuyoPet.Core.Tests/WorkTracking/WorkSessionAccumulatorTests.cs`
- 创建：`src/IkuyoPet.Core/WorkTracking/WorkTrackingModels.cs`
- 创建：`src/IkuyoPet.Core/WorkTracking/WorkSessionAccumulator.cs`

- [x] **步骤 1：编写有效与无效采样测试**

```csharp
[Fact]
public void CountsOnlyWhitelistedForegroundUnlockedAndRecentlyActiveSamples()
{
    var accumulator = new WorkSessionAccumulator(TimeSpan.FromMinutes(5));
    var now = new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.FromHours(8));

    accumulator.Observe(new("PyCharm", true, false, TimeSpan.FromMinutes(1), now));
    accumulator.Observe(new("PyCharm", false, false, TimeSpan.Zero, now.AddSeconds(5)));

    Assert.Equal(TimeSpan.FromSeconds(5), accumulator.ActiveTime);
}
```

- [x] **步骤 2：运行并确认测试因累计器缺失而失败**

运行：`dotnet test tests/IkuyoPet.Core.Tests --filter WorkSessionAccumulatorTests`

预期：FAIL，编译器报告 `WorkSessionAccumulator` 未定义。

- [x] **步骤 3：实现最小累计器**

```csharp
public sealed class WorkSessionAccumulator(TimeSpan idleLimit)
{
    private ActivitySample? previous;
    public TimeSpan ActiveTime { get; private set; }

    public void Observe(ActivitySample current)
    {
        if (previous is { } last && last.IsWhitelistedForeground && !last.IsLocked && last.IdleTime < idleLimit)
            ActiveTime += current.ObservedAt - last.ObservedAt;
        previous = current;
    }
}
```

- [x] **步骤 4：增加锁屏、空闲达到 5 分钟、跨应用和系统时钟倒退测试**

运行：`dotnet test tests/IkuyoPet.Core.Tests`

预期：全部 PASS；负时间差不累计。

- [x] **步骤 5：提交**

Commit：`feat: calculate foreground active work time`

## 任务 4：用 TDD 实现 SQLite 数据层

**文件：**
- 创建：`src/IkuyoPet.Core/Storage/IEventRepository.cs`
- 创建：`src/IkuyoPet.Infrastructure/Storage/DatabaseMigrator.cs`
- 创建：`src/IkuyoPet.Infrastructure/Storage/SqliteEventRepository.cs`
- 创建：`tests/IkuyoPet.Infrastructure.Tests/Storage/SqliteEventRepositoryTests.cs`

- [x] **步骤 1：写入内存 SQLite 的失败测试**

```csharp
[Fact]
public async Task SavesAndReadsReminderOutcomeWithoutWindowContent()
{
    await using var database = TestDatabase.CreateInMemory();
    var repository = new SqliteEventRepository(database.ConnectionString);
    var item = ReminderEvent.Completed(Guid.NewGuid(), DateTimeOffset.Parse("2026-09-06T10:20:00+08:00"), "pet");

    await repository.AppendReminderAsync(item, CancellationToken.None);
    var stored = await repository.ReadReminderEventsAsync(DateOnly.Parse("2026-09-06"), CancellationToken.None);

    Assert.Single(stored);
    Assert.Equal(ReminderOutcome.Completed, stored[0].Outcome);
}
```

- [x] **步骤 2：运行并确认仓储缺失导致失败**

运行：`dotnet test tests/IkuyoPet.Infrastructure.Tests --filter SavesAndReadsReminderOutcomeWithoutWindowContent`

预期：FAIL，编译器报告仓储类型未定义。

- [x] **步骤 3：创建五张表与参数化写入**

迁移脚本必须创建 `reminder_rules`、`reminder_events`、`tracked_apps`、`work_sessions` 和 `app_settings`，开启 WAL 与外键，并对时间范围查询建立索引。所有值通过 `SqliteParameter` 绑定。

- [x] **步骤 4：增加重复迁移、日期过滤、取消令牌与不保存窗口标题的测试**

运行：`dotnet test tests/IkuyoPet.Infrastructure.Tests`

预期：全部 PASS；迁移连续执行两次不报错。

- [x] **步骤 5：提交**

Commit：`feat: persist reminder and work events in sqlite`

## 任务 5：实现 Windows 活动探针与会话调度

**文件：**
- 创建：`src/IkuyoPet.Core/WorkTracking/IActivityProbe.cs`
- 创建：`src/IkuyoPet.Infrastructure/Windows/ForegroundActivityProbe.cs`
- 创建：`src/IkuyoPet.Infrastructure/Windows/WorkTrackingService.cs`
- 创建：`tests/IkuyoPet.Infrastructure.Tests/Windows/WorkTrackingServiceTests.cs`

- [ ] **步骤 1：用伪探针编写失败测试**

测试固定返回 `pycharm64`、未锁屏、空闲 1 分钟，推进两次 5 秒采样，断言仓储只写入 5 秒有效会话。

- [ ] **步骤 2：运行并确认服务缺失导致失败**

运行：`dotnet test tests/IkuyoPet.Infrastructure.Tests --filter WorkTrackingServiceTests`

- [ ] **步骤 3：实现 Windows API 适配**

使用 `GetForegroundWindow`、`GetWindowThreadProcessId`、`GetLastInputInfo` 和会话通知读取进程、空闲与锁屏状态。不得调用窗口文本 API，不得截屏，不得安装键盘或鼠标钩子。

- [ ] **步骤 4：验证 5 秒采样、切换应用、锁屏和空闲停止**

运行：`dotnet test tests/IkuyoPet.Infrastructure.Tests`

预期：全部 PASS。

- [ ] **步骤 5：提交**

Commit：`feat: track whitelisted foreground activity`

## 任务 6：实现提醒呈现路由和原生通知

**文件：**
- 创建：`src/IkuyoPet.Core/Presentation/IReminderPresenter.cs`
- 创建：`src/IkuyoPet.Core/Presentation/ReminderPresentationRouter.cs`
- 创建：`src/IkuyoPet.Infrastructure/Windows/WindowsNotificationPresenter.cs`
- 创建：`tests/IkuyoPet.Core.Tests/Presentation/ReminderPresentationRouterTests.cs`

- [ ] **步骤 1：编写桌宠开关路由失败测试**

```csharp
[Theory]
[InlineData(true, "pet")]
[InlineData(false, "notification")]
public async Task RoutesToConfiguredPresenter(bool petEnabled, string expected)
{
    var recorder = new RecordingPresenterFactory();
    var router = new ReminderPresentationRouter(recorder);
    await router.ShowAsync(ReminderDue.Sample(), petEnabled, CancellationToken.None);
    Assert.Equal(expected, recorder.LastChannel);
}
```

- [ ] **步骤 2：运行并确认路由类型缺失导致失败**

运行：`dotnet test tests/IkuyoPet.Core.Tests --filter ReminderPresentationRouterTests`

- [ ] **步骤 3：实现路由和 Windows App SDK 通知动作**

通知包含 `完成`、`10 分钟后`、`跳过` 三个按钮；参数携带事件 ID 和动作枚举。激活处理器只写入动作并更新调度，不主动打开主窗口。

- [ ] **步骤 4：运行 Core 测试并人工触发一条本地通知**

自动验证：`dotnet test tests/IkuyoPet.Core.Tests`

人工验证：点击三个动作各一次，时间线分别显示 `completed`、`snoozed`、`skipped`。

- [ ] **步骤 5：提交**

Commit：`feat: route reminders to pet or app notification`

## 任务 7：用 TDD 校验皮肤包并实现透明桌宠

**文件：**
- 创建：`src/IkuyoPet.Pet/Skins/SkinManifest.cs`
- 创建：`src/IkuyoPet.Pet/Skins/SkinPackageValidator.cs`
- 创建：`tests/IkuyoPet.Infrastructure.Tests/Skins/SkinPackageValidatorTests.cs`
- 创建：`src/IkuyoPet.Pet/PetWindow.xaml`
- 创建：`src/IkuyoPet.Pet/PetWindow.xaml.cs`
- 创建：`src/IkuyoPet.Pet/PetReminderPresenter.cs`
- 创建：`schemas/skin-manifest.schema.json`

- [ ] **步骤 1：编写透明图片与 manifest 校验失败测试**

有效包必须包含 `manifest.json`、`idle.png`、`remind.png`，且 PNG 存在 alpha 通道；缺文件、尺寸越界、帧率不在 1–30 或图片无透明通道时返回明确错误且不切换当前皮肤。

- [ ] **步骤 2：运行并确认校验器缺失导致失败**

运行：`dotnet test tests/IkuyoPet.Infrastructure.Tests --filter SkinPackageValidatorTests`

- [ ] **步骤 3：实现校验器和原子导入**

先复制到 `%LocalAppData%\IkuyoPet\skins\.staging\<guid>`，完成全部验证后移动到最终皮肤目录；同 ID 已存在时创建版本目录，不覆盖原文件。

- [ ] **步骤 4：实现透明可拖动窗口**

`PetWindow` 设置 `WindowStyle=None`、`AllowsTransparency=True`、`Background=Transparent`、`ShowInTaskbar=False`。角色可见区域接受拖动和点击，透明区域不遮挡桌面；全屏、锁屏和演示状态隐藏窗口。

- [ ] **步骤 5：运行测试与手工窗口检查并提交**

运行：`dotnet test IkuyoPet.sln`

人工检查：拖动、靠边、160 px 默认尺寸、多显示器 DPI、全屏隐藏、恢复定位。

Commit：`feat: add transparent draggable pet and skin packs`

## 任务 8：实现主窗口、托盘与日志页面

**文件：**
- 修改：`src/IkuyoPet.App/App.xaml`
- 修改：`src/IkuyoPet.App/App.xaml.cs`
- 修改：`src/IkuyoPet.App/MainWindow.xaml`
- 修改：`src/IkuyoPet.App/MainWindow.xaml.cs`
- 创建：`src/IkuyoPet.Infrastructure/Windows/TrayIconHost.cs`

- [ ] **步骤 1：编写 ViewModel 失败测试**

测试今日卡片只汇总当天事件；时间线区分已完成、已延后、已跳过、未响应和静默；应用页按显示名汇总有效秒数。

- [ ] **步骤 2：运行并确认 ViewModel 类型缺失导致失败**

运行：`dotnet test tests/IkuyoPet.Core.Tests --filter ViewModel`

- [ ] **步骤 3：实现清爽数据卡片 UI**

导航固定为今日、时间线、应用、桌宠、设置。关闭主窗口只隐藏到托盘；托盘菜单提供打开、暂停提醒、切换桌宠、立即记录喝水和退出。

- [ ] **步骤 4：实现 CSV 导出与本地数据清理**

导出列固定为日期、计划时间、显示时间、类型、渠道、动作、动作时间、延后次数。清理操作先显示数据库路径和日期范围，再由用户确认。

- [ ] **步骤 5：运行测试并提交**

运行：`dotnet test IkuyoPet.sln`

Commit：`feat: add tray shell and local activity dashboard`

## 任务 9：打包、开机启动、开源文档与 CI

**文件：**
- 创建：`LICENSE`
- 创建：`README.md`
- 创建：`.github/workflows/windows-ci.yml`
- 创建：`assets/skins/default/manifest.json`
- 创建：`assets/skins/default/README.md`
- 修改：`.gitignore`

- [ ] **步骤 1：加入 MIT 代码许可证与资产许可说明**

默认仓库不包含喜多郁代或其他商业角色图片；原创默认皮肤单独标注作者和许可，用户导入素材不进入 Git。

- [ ] **步骤 2：实现用户级开机启动开关**

使用当前用户启动项或 MSIX StartupTask；不开管理员权限。设置页显示真实启用状态，失败时给出可恢复提示。

- [ ] **步骤 3：创建 Windows CI**

CI 在 `windows-latest` 上安装 .NET 10，执行 `dotnet restore`、`dotnet build --no-restore -warnaserror`、`dotnet test --no-build --collect:"XPlat Code Coverage"`。

- [ ] **步骤 4：发布自包含测试包**

运行：`dotnet publish src/IkuyoPet.App -c Release -r win-x64 --self-contained true -o publish/win-x64`

预期：发布目录可在未安装 .NET 的普通 Windows 账户启动；SQLite 不需要服务器。

- [ ] **步骤 5：完整验收并提交**

运行：`dotnet build IkuyoPet.sln -c Release -warnaserror`、`dotnet test IkuyoPet.sln -c Release --no-build`。

逐项检查设计规格第 11 节的十条验收标准。

Commit：`chore: package and document Ikuyo Pet MVP`

## 第二阶段边界

应用内 AI 皮肤工作室单独建立实现计划，不纳入本 MVP。其输入为用户明确选择的参考图片，输出必须是通过第一版 `SkinPackageValidator` 的标准皮肤包；API 密钥存入 Windows 凭据管理器，生成前显示网络与费用确认。Codex 开发阶段使用 `imagegen` 制作原创透明桌宠素材，应用运行时不能直接调用 Codex skill。
