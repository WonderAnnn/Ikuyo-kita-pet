# 主窗口、托盘、离线提醒与日志实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）来跟踪进度。

**目标：** 在现有提醒呈现、透明桌宠、皮肤导入和 SQLite 基础上，完成可运行的 Ikuyo Pet 第一阶段应用：A1a 今日优先主窗口、T1 极简托盘、L1 今日时间线、离线提醒调度、白名单前台工作统计和用户级启动项。

**架构：** `IkuyoPet.Core` 提供提醒规则、调度、日志时间线和纯数据状态；`IkuyoPet.Infrastructure` 负责 SQLite 查询、Windows 前台/锁屏探针、用户启动项和 CSV 导出；`IkuyoPet.App` 负责 WPF 主窗口、托盘和服务组合；`IkuyoPet.Pet` 继续负责透明可拖动桌宠。主窗口关闭只隐藏到托盘，桌宠关闭时由既有路由切换到 Windows 通知。

**技术栈：** C# 14、.NET 10、WPF、Windows App SDK 2.4.0、Microsoft.Data.Sqlite 10.0.11、H.NotifyIcon.Wpf 2.4.1、xUnit v3。所有开发命令使用 `G:\IkuyoPetDev\dotnet\dotnet.exe` 和 `G:\IkuyoPetDev\nuget\packages`。

---

## 文件结构与职责

### 将创建的文件

- `src/IkuyoPet.Core/Reminders/ReminderRule.cs`：可配置提醒规则数据。
- `src/IkuyoPet.Core/Reminders/ReminderScheduleCalculator.cs`：纯函数式到期计算，不访问系统 UI 或数据库。
- `src/IkuyoPet.Core/Dashboard/DashboardModels.cs`：A1a 首屏和 L1 时间线只读模型。
- `src/IkuyoPet.Core/Dashboard/DashboardQueryService.cs`：从仓储组合当天提醒事件和工作会话。
- `src/IkuyoPet.Core/Dashboard/MainWindowState.cs`：主窗口默认页和导航状态。
- `src/IkuyoPet.Infrastructure/Storage/CsvLogExporter.cs`：按固定列导出提醒日志 CSV。
- `src/IkuyoPet.Infrastructure/Windows/WorkTrackingLoop.cs`：每 30 秒采样并在退出时冲刷有效工作会话。
- `src/IkuyoPet.Infrastructure/Windows/WindowsStartupManager.cs`：HKCU 用户级启动项读写。
- `src/IkuyoPet.App/MainWindowViewModel.cs`：WPF 绑定所需的可观察页面数据。
- `src/IkuyoPet.App/TrayIconHost.cs`：T1 托盘生命周期和四项命令。
- `src/IkuyoPet.App/Views/TodayView.xaml`、`TodayView.xaml.cs`：A1a 今日优先视图。
- `src/IkuyoPet.App/Views/LogView.xaml`、`LogView.xaml.cs`：L1 时间线视图。
- `src/IkuyoPet.App/Views/SettingsView.xaml`、`SettingsView.xaml.cs`：桌宠开关、白名单、启动项和导出入口。
- `tests/IkuyoPet.Core.Tests/Reminders/ReminderScheduleCalculatorTests.cs`：到期与静默规则测试。
- `tests/IkuyoPet.Core.Tests/Dashboard/DashboardQueryServiceTests.cs`：当天汇总、时间线和工作时长测试。
- `tests/IkuyoPet.Core.Tests/Dashboard/MainWindowStateTests.cs`：默认页与导航测试。
- `tests/IkuyoPet.Infrastructure.Tests/Storage/CsvLogExporterTests.cs`：CSV 列、转义和隐私边界测试。
- `tests/IkuyoPet.Infrastructure.Tests/Windows/WorkTrackingLoopTests.cs`：采样周期、停止冲刷和取消测试。
- `tests/IkuyoPet.Infrastructure.Tests/Windows/WindowsStartupManagerTests.cs`：使用内存替身验证启动项，不触碰真实注册表。

### 将修改的文件

- `src/IkuyoPet.Core/Storage/IEventRepository.cs`：增加规则、工作会话和白名单查询/写入契约。
- `src/IkuyoPet.Core/WorkTracking/WorkTrackingModels.cs`：增加 `TrackedApplication`。
- `src/IkuyoPet.Infrastructure/Storage/SqliteEventRepository.cs`：实现新增查询和参数化写入。
- `src/IkuyoPet.Infrastructure/Storage/DatabaseMigrator.cs`：为规则、白名单和设置补充索引与默认数据迁移。
- `src/IkuyoPet.App/App.xaml`、`App.xaml.cs`：移除 `StartupUri`，组合服务、托盘和生命周期。
- `src/IkuyoPet.App/MainWindow.xaml`、`MainWindow.xaml.cs`：实现导航壳、A1a 视觉和关闭到托盘。
- `src/IkuyoPet.App/IkuyoPet.App.csproj`：保持 WPF 与 H.NotifyIcon.Wpf 引用。
- `tests/IkuyoPet.Infrastructure.Tests/Storage/SqliteEventRepositoryTests.cs`：增加新增仓储契约的集成测试。
- `tests/IkuyoPet.Infrastructure.Tests/Windows/WorkTrackingServiceTests.cs`：适配扩展后的仓储替身。

## 任务 1：扩展核心规则、仓储契约和 Dashboard 只读模型

**文件：**
- 创建：`src/IkuyoPet.Core/Reminders/ReminderRule.cs`
- 创建：`src/IkuyoPet.Core/Reminders/ReminderScheduleCalculator.cs`
- 创建：`src/IkuyoPet.Core/Dashboard/DashboardModels.cs`
- 创建：`src/IkuyoPet.Core/Dashboard/DashboardQueryService.cs`
- 创建：`src/IkuyoPet.Core/Dashboard/MainWindowState.cs`
- 修改：`src/IkuyoPet.Core/Storage/IEventRepository.cs`
- 修改：`src/IkuyoPet.Core/WorkTracking/WorkTrackingModels.cs`
- 创建：`tests/IkuyoPet.Core.Tests/Reminders/ReminderScheduleCalculatorTests.cs`
- 创建：`tests/IkuyoPet.Core.Tests/Dashboard/DashboardQueryServiceTests.cs`
- 创建：`tests/IkuyoPet.Core.Tests/Dashboard/MainWindowStateTests.cs`

- [ ] **步骤 1：编写到期、静默和 Dashboard 测试**

```csharp
[Fact]
public void ProducesDueReminderInsideWindowAtInterval()
{
    var rule = new ReminderRule(
        Guid.NewGuid(), "water", "喝口水再继续吧～",
        new TimeOnly(9, 0), new TimeOnly(18, 0), 60, true);

    var due = ReminderScheduleCalculator.GetDue(
        rule, new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.FromHours(8)),
        lastDisplayedAt: new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.FromHours(8)));

    Assert.NotNull(due);
    Assert.Equal("water", due!.Kind);
}

[Fact]
public void DashboardCountsOutcomesAndWorkSessionsForOneDay()
{
    var day = new DateOnly(2026, 9, 7);
    var repository = new FakeEventRepository(
        [CreateReminder(ReminderOutcome.Completed, day, 9, 0),
         CreateReminder(ReminderOutcome.Skipped, day, 10, 0)],
        [new WorkSession(Guid.NewGuid(), "pycharm64", "PyCharm", day.ToDateTime(new(9, 0)),
            day.ToDateTime(new(10, 30)), 5400, "app-switched")]);

    var snapshot = new DashboardQueryService(repository).GetAsync(
        day, TestContext.Current.CancellationToken).GetAwaiter().GetResult();

    Assert.Equal(1, snapshot.CompletedCount);
    Assert.Equal(1, snapshot.SkippedCount);
    Assert.Equal(TimeSpan.FromHours(1.5), snapshot.WorkTime);
    Assert.Equal(2, snapshot.Timeline.Count);
}
```

测试替身实现新增契约：`ReadReminderRulesAsync`、`ReadWorkSessionsAsync`、`ReadTrackedApplicationsAsync`、`UpsertReminderRuleAsync`、`UpsertTrackedApplicationAsync`。所有异步测试使用 `TestContext.Current.CancellationToken`。

- [ ] **步骤 2：运行 Core 测试确认类型和方法缺失**

运行：

```powershell
$env:Path='G:\IkuyoPetDev\dotnet;' + $env:Path
$env:DOTNET_ROOT='G:\IkuyoPetDev\dotnet'
$env:NUGET_PACKAGES='G:\IkuyoPetDev\nuget\packages'
G:\IkuyoPetDev\dotnet\dotnet.exe test G:\testPet\tests\IkuyoPet.Core.Tests\IkuyoPet.Core.Tests.csproj --filter "FullyQualifiedName~ReminderScheduleCalculatorTests|FullyQualifiedName~DashboardQueryServiceTests|FullyQualifiedName~MainWindowStateTests"
```

预期：FAIL，编译器报告 `ReminderRule`、`DashboardQueryService` 或仓储新增成员不存在。

- [ ] **步骤 3：实现最小核心模型和计算服务**

`ReminderRule` 固定为：

```csharp
public sealed record ReminderRule(
    Guid Id, string Kind, string Message,
    TimeOnly StartLocal, TimeOnly EndLocal,
    int IntervalMinutes, bool Enabled);
```

`ReminderScheduleCalculator.GetDue` 只在 `Enabled`、当前本地时间位于起止窗口、未处于跨午夜静默窗口且距离上次显示至少 `IntervalMinutes` 时返回 `ReminderDue`；动作固定为完成、延后和跳过。`DashboardQueryService.GetAsync(DateOnly, CancellationToken)` 查询当天两类数据，按 `ScheduledAt` 升序建立 `TimelineItem`，并计算完成、延后、跳过、未响应计数和 `WorkTime`。`MainWindowState` 的默认页必须是 `Today`，只接受 `Today`、`Log`、`Rules`、`Pet`、`Settings` 五个导航值。

仓储契约使用以下签名：

```csharp
Task<IReadOnlyList<ReminderRule>> ReadReminderRulesAsync(CancellationToken cancellationToken);
Task UpsertReminderRuleAsync(ReminderRule rule, CancellationToken cancellationToken);
Task<IReadOnlyList<WorkSession>> ReadWorkSessionsAsync(DateOnly day, CancellationToken cancellationToken);
Task<IReadOnlyList<TrackedApplication>> ReadTrackedApplicationsAsync(CancellationToken cancellationToken);
Task UpsertTrackedApplicationAsync(TrackedApplication application, CancellationToken cancellationToken);
```

- [ ] **步骤 4：运行 Core 测试确认通过**

运行同一条 `dotnet test` 命令，预期新测试和原有提醒、工作跟踪测试全部 PASS。

- [ ] **步骤 5：Commit**

```powershell
git -C G:\testPet add src\IkuyoPet.Core tests\IkuyoPet.Core.Tests
git -C G:\testPet commit -m "feat: add reminder schedule and dashboard models"
```

## 任务 2：实现 SQLite 查询、白名单数据和 CSV 导出

**文件：**
- 修改：`src/IkuyoPet.Infrastructure/Storage/DatabaseMigrator.cs`
- 修改：`src/IkuyoPet.Infrastructure/Storage/SqliteEventRepository.cs`
- 创建：`src/IkuyoPet.Infrastructure/Storage/CsvLogExporter.cs`
- 修改：`tests/IkuyoPet.Infrastructure.Tests/Storage/SqliteEventRepositoryTests.cs`
- 创建：`tests/IkuyoPet.Infrastructure.Tests/Storage/CsvLogExporterTests.cs`
- 修改：`tests/IkuyoPet.Infrastructure.Tests/Windows/WorkTrackingServiceTests.cs`

- [ ] **步骤 1：编写 SQLite 和 CSV 失败测试**

```csharp
[Fact]
public async Task ReadsRulesWorkSessionsAndTrackedAppsWithoutWindowContent()
{
    await using var database = TestDatabase.CreateInMemory();
    var repository = new SqliteEventRepository(database.ConnectionString);
    var rule = new ReminderRule(Guid.NewGuid(), "water", "喝水", new(9, 0), new(18, 0), 60, true);
    await repository.UpsertReminderRuleAsync(rule, TestContext.Current.CancellationToken);
    await repository.UpsertTrackedApplicationAsync(
        new TrackedApplication("pycharm64", "PyCharm", true),
        TestContext.Current.CancellationToken);

    var rules = await repository.ReadReminderRulesAsync(TestContext.Current.CancellationToken);
    var apps = await repository.ReadTrackedApplicationsAsync(TestContext.Current.CancellationToken);

    Assert.Single(rules);
    Assert.Equal("PyCharm", apps.Single().DisplayName);
}

[Fact]
public void EscapesCsvFieldsAndUsesFixedColumns()
{
    var csv = CsvLogExporter.Serialize([
        new CsvLogRow("2026-09-07", "09:00", "water", "pet", "completed", "他说，喝水")]);

    Assert.StartsWith("日期,时间,类型,渠道,动作,备注", csv, StringComparison.Ordinal);
    Assert.Contains("\"他说，喝水\"", csv, StringComparison.Ordinal);
}
```

- [ ] **步骤 2：运行基础设施测试确认接口未实现**

运行：

```powershell
G:\IkuyoPetDev\dotnet\dotnet.exe test G:\testPet\tests\IkuyoPet.Infrastructure.Tests\IkuyoPet.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SqliteEventRepositoryTests|FullyQualifiedName~CsvLogExporterTests"
```

预期：FAIL，报告新增仓储方法和 `CsvLogExporter` 不存在。

- [ ] **步骤 3：实现参数化查询和固定 CSV 序列化**

`SqliteEventRepository` 为新增表字段使用参数绑定；规则查询读取 `reminder_rules` 的全部列，工作会话查询使用半开区间 `started_at >= dayStart AND started_at < nextDayStart`，白名单查询读取 `tracked_apps WHERE enabled = 1`。`TrackedApplication` 定义为：

```csharp
public sealed record TrackedApplication(string ProcessName, string DisplayName, bool Enabled);
```

`CsvLogExporter.Serialize` 固定输出 `日期,时间,类型,渠道,动作,备注`，字段包含逗号、双引号或换行时按 RFC 4180 双引号转义；不得输出窗口标题、项目名、文件名、输入内容、截图或睡眠字段。迁移保持幂等，并为 `reminder_rules.enabled` 和 `tracked_apps.process_name` 保留索引。

- [ ] **步骤 4：运行 SQLite、CSV 与全量基础设施测试**

运行：

```powershell
G:\IkuyoPetDev\dotnet\dotnet.exe test G:\testPet\tests\IkuyoPet.Infrastructure.Tests\IkuyoPet.Infrastructure.Tests.csproj
```

预期：所有 SQLite、工作跟踪、通知和皮肤测试 PASS。

- [ ] **步骤 5：Commit**

```powershell
git -C G:\testPet add src\IkuyoPet.Infrastructure\Storage tests\IkuyoPet.Infrastructure.Tests\Storage tests\IkuyoPet.Infrastructure.Tests\Windows\WorkTrackingServiceTests.cs
git -C G:\testPet commit -m "feat: query dashboard data and export local logs"
```

## 任务 3：接入离线提醒循环和统一动作处理

**文件：**
- 创建：`src/IkuyoPet.Infrastructure/Windows/ReminderLoop.cs`
- 创建：`src/IkuyoPet.Infrastructure/Windows/ReminderActionCoordinator.cs`
- 创建：`tests/IkuyoPet.Infrastructure.Tests/Windows/ReminderLoopTests.cs`
- 修改：`src/IkuyoPet.App/App.xaml.cs`

- [ ] **步骤 1：编写循环和动作协调测试**

```csharp
[Fact]
public async Task LoopPresentsDueRuleAndStopsOnCancellation()
{
    var presenter = new RecordingPresenter();
    var repository = new FakeRuleRepository([EnabledWaterRule()]);
    using var cts = new CancellationTokenSource();
    var loop = new ReminderLoop(repository, presenter, new FixedClock("2026-09-07T10:00:00+08:00"), TimeSpan.Zero);

    var run = loop.RunAsync(cts.Token);
    await presenter.WaitForCallAsync(TestContext.Current.CancellationToken);
    cts.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    Assert.Equal("water", presenter.LastDue!.Kind);
}
```

- [ ] **步骤 2：运行测试确认循环类型缺失**

运行：

```powershell
G:\IkuyoPetDev\dotnet\dotnet.exe test G:\testPet\tests\IkuyoPet.Infrastructure.Tests\IkuyoPet.Infrastructure.Tests.csproj --filter ReminderLoopTests
```

预期：FAIL，报告 `ReminderLoop`、`ReminderActionCoordinator` 未定义。

- [ ] **步骤 3：实现 PeriodicTimer 循环和动作协调器**

`ReminderLoop` 使用注入的 `TimeProvider`、规则仓储和 `IReminderPresenter`，每次 tick 读取启用规则，调用 `ReminderScheduleCalculator.GetDue`，为每个到期规则创建唯一事件 ID 并交给既有 `ReminderPresentationRouter`。默认采样间隔为 30 秒；测试可注入 `TimeSpan.Zero` 和固定时钟。取消令牌结束循环，不吞掉取消异常。

`ReminderActionCoordinator` 接收 `(Guid eventId, ReminderAction action)`，调用既有 `ReminderStateMachine`，把结果写入 `IEventRepository.AppendReminderAsync`；完成、跳过和达到重试上限结束事件，延后或未响应按规则重新排程。每个事件 ID 只允许写入一次最终动作，重复激活直接忽略。

- [ ] **步骤 4：运行循环和全量测试**

运行 `dotnet test G:\testPet\IkuyoPet.sln`，预期新增循环测试以及现有 38 个测试全部 PASS。

- [ ] **步骤 5：Commit**

```powershell
git -C G:\testPet add src\IkuyoPet.Infrastructure\Windows\ReminderLoop.cs src\IkuyoPet.Infrastructure\Windows\ReminderActionCoordinator.cs tests\IkuyoPet.Infrastructure.Tests\Windows\ReminderLoopTests.cs src\IkuyoPet.App\App.xaml.cs
git -C G:\testPet commit -m "feat: run offline reminders and coordinate actions"
```

## 任务 4：实现 A1a 主窗口与 L1 今日时间线

**文件：**
- 创建：`src/IkuyoPet.App/MainWindowViewModel.cs`
- 创建：`src/IkuyoPet.App/Views/TodayView.xaml`
- 创建：`src/IkuyoPet.App/Views/TodayView.xaml.cs`
- 创建：`src/IkuyoPet.App/Views/LogView.xaml`
- 创建：`src/IkuyoPet.App/Views/LogView.xaml.cs`
- 创建：`src/IkuyoPet.App/Views/SettingsView.xaml`
- 创建：`src/IkuyoPet.App/Views/SettingsView.xaml.cs`
- 修改：`src/IkuyoPet.App/MainWindow.xaml`
- 修改：`src/IkuyoPet.App/MainWindow.xaml.cs`
- 修改：`src/IkuyoPet.App/App.xaml`

- [ ] **步骤 1：先实现可绑定状态和导航测试**

在 `MainWindowViewModel` 中暴露 `CurrentPage`、`TodaySnapshot`、`TimelineItems`、`PetEnabled`、`NextReminderText` 和 `NavigateCommand`；`LoadTodayAsync` 只调用 `DashboardQueryService`。默认构造状态来自 `MainWindowState.Default`，并以 `INotifyPropertyChanged` 通知集合刷新。

- [ ] **步骤 2：运行 UI 依赖构建确认视图尚不存在**

运行：

```powershell
G:\IkuyoPetDev\dotnet\dotnet.exe build G:\testPet\IkuyoPet.sln --configuration Debug --no-restore
```

预期：FAIL，XAML 引用的视图和绑定成员尚未定义。

- [ ] **步骤 3：实现 A1a XAML 与 L1 时间线**

`MainWindow.xaml` 使用固定左侧导航和右侧 `Frame`；关闭事件取消默认关闭并改为 `Hide()`。`TodayView.xaml` 使用浅蓝粉背景、圆角卡片：第一张卡显示皮肤预览、下一次提醒和一句活泼文案；喝水、活动、工作时长只显示为三个小摘要。`LogView.xaml` 以 `ItemsControl` 按 `ScheduledAt` 升序展示日期、时间、类型、渠道、动作和结果文字，完成 / 延后 / 跳过同时显示文字与颜色，顶部提供“今天”和四个轻量筛选项。`SettingsView.xaml` 显示桌宠开关、当前皮肤、白名单应用、开机启动和 CSV 导出入口；不显示睡眠或医疗字段。

`App.xaml` 移除 `StartupUri`，在资源中注册统一颜色、圆角卡片样式和 `DataTemplate`；`App.xaml.cs` 创建 `%LocalAppData%\\IkuyoPet\\ikuyo-pet.db`，先运行迁移，再组合仓储、循环、Presenters、`MainWindowViewModel` 和 `TrayIconHost`。

- [ ] **步骤 4：构建并运行窗口手工验收**

运行：

```powershell
G:\IkuyoPetDev\dotnet\dotnet.exe build G:\testPet\IkuyoPet.sln --configuration Debug --no-restore
G:\testPet\src\IkuyoPet.App\bin\Debug\net10.0-windows10.0.19041.0\IkuyoPet.exe
```

验收：启动默认显示 A1a；左侧导航可打开今日、日志、规则、桌宠和设置；关闭窗口后进程仍驻留；今日卡不出现睡眠或医疗数据；日志可看到已有 SQLite 记录。

- [ ] **步骤 5：Commit**

```powershell
git -C G:\testPet add src\IkuyoPet.App
git -C G:\testPet commit -m "feat: add today-first dashboard and timeline"
```

## 任务 5：实现 T1 极简托盘和桌宠生命周期

**文件：**
- 创建：`src/IkuyoPet.App/TrayIconHost.cs`
- 修改：`src/IkuyoPet.App/App.xaml`
- 修改：`src/IkuyoPet.App/App.xaml.cs`
- 修改：`src/IkuyoPet.App/MainWindow.xaml.cs`
- 创建：`tests/IkuyoPet.Core.Tests/Presentation/TrayCommandModelTests.cs`

- [ ] **步骤 1：编写托盘命令模型测试**

```csharp
[Fact]
public void T1ContainsOnlyFourQuietCommands()
{
    var commands = TrayMenuModel.T1.Select(item => item.Command).ToArray();

    Assert.Equal(
        [TrayCommand.TogglePet, TrayCommand.PauseReminders,
         TrayCommand.OpenToday, TrayCommand.Exit], commands);
}
```

- [ ] **步骤 2：运行测试确认托盘模型缺失**

运行 Core 测试并预期编译器报告 `TrayMenuModel` 和 `TrayCommand` 未定义。

- [ ] **步骤 3：实现 T1 菜单与窗口关闭行为**

`TrayIconHost` 使用已安装的 `H.NotifyIcon.Wpf` `TaskbarIcon`，菜单固定为：显示 / 隐藏桌宠、暂停提醒 30 分钟、打开今日、退出 Ikuyo Pet。左键和双击打开今日；菜单不显示统计、工作计时或快速记录动作。`MainWindow.Closing` 设置 `e.Cancel = true` 并调用 `Hide()`；只有托盘“退出”先停止 `ReminderLoop`、冲刷 `WorkTrackingLoop`、隐藏 PetWindow，再调用 `Application.Shutdown()`。桌宠开关变化只更新 `ReminderPresentationRouter` 的 `petEnabled` 来源，不改变日志语义。

- [ ] **步骤 4：运行构建、托盘手工验收和全量测试**

验收 T1 四项命令、主窗口隐藏 / 恢复、桌宠显示 / 隐藏、退出时数据库连接和定时器均释放；运行 `dotnet test G:\testPet\IkuyoPet.sln`，预期全部 PASS。

- [ ] **步骤 5：Commit**

```powershell
git -C G:\testPet add src\IkuyoPet.App tests\IkuyoPet.Core.Tests\Presentation\TrayCommandModelTests.cs
git -C G:\testPet commit -m "feat: add quiet tray host and app lifecycle"
```

## 任务 6：白名单应用设置、后台工作统计和用户级启动项

**文件：**
- 创建：`src/IkuyoPet.Infrastructure/Windows/WorkTrackingLoop.cs`
- 创建：`src/IkuyoPet.Infrastructure/Windows/WindowsStartupManager.cs`
- 创建：`tests/IkuyoPet.Infrastructure.Tests/Windows/WorkTrackingLoopTests.cs`
- 创建：`tests/IkuyoPet.Infrastructure.Tests/Windows/WindowsStartupManagerTests.cs`
- 修改：`src/IkuyoPet.App/Views/SettingsView.xaml`
- 修改：`src/IkuyoPet.App/Views/SettingsView.xaml.cs`

- [ ] **步骤 1：编写工作循环和启动项替身测试**

```csharp
[Fact]
public async Task StopFlushesOnlySamplesAcceptedByExistingWorkTrackingService()
{
    var probe = new SequenceProbe(
        new("pycharm64", true, false, TimeSpan.FromMinutes(1), Now),
        new("pycharm64", true, false, TimeSpan.FromMinutes(1), Now.AddSeconds(30)));
    var repository = new RecordingRepository();
    var loop = new WorkTrackingLoop(
        new WorkTrackingService(probe, repository),
        TimeSpan.Zero);

    await loop.RunOneCycleAsync(TestContext.Current.CancellationToken);
    await loop.RunOneCycleAsync(TestContext.Current.CancellationToken);
    await loop.StopAsync(TestContext.Current.CancellationToken);

    Assert.Equal(1, repository.Sessions.Count);
    Assert.Equal(30, repository.Sessions[0].ActiveSeconds);
}

[Fact]
public void StartupManagerUsesCurrentUserStoreWithoutAdminRights()
{
    var store = new RecordingStartupStore();
    var manager = new WindowsStartupManager(store, "G:\\Apps\\IkuyoPet.exe");

    manager.SetEnabled(true);

    Assert.Equal("G:\\Apps\\IkuyoPet.exe", store.Value);
}
```

- [ ] **步骤 2：运行测试确认新循环和启动管理器缺失**

运行 Infrastructure 测试筛选，预期报告 `WorkTrackingLoop` 和 `WindowsStartupManager` 未定义。

- [ ] **步骤 3：实现 30 秒采样、白名单维护和 HKCU 启动项**

`WorkTrackingLoop` 只调用现有 `WorkTrackingService.SampleOnceAsync`，默认周期 30 秒，`StopAsync` 先停止计时再调用服务的 `StopAsync`。`ForegroundActivityProbe` 的白名单来自 `ReadTrackedApplicationsAsync`，设置页允许添加进程名和显示名，默认示例包含 `pycharm64 / PyCharm`；不读取窗口标题，不安装键鼠钩子。

`WindowsStartupManager` 通过 `Microsoft.Win32.Registry.CurrentUser` 访问 `Software\\Microsoft\\Windows\\CurrentVersion\\Run`，值名固定为 `IkuyoPet`，只写当前用户，不请求管理员权限。抽象 `IStartupEntryStore` 后，单元测试只使用内存替身；注册表异常转换为设置页可显示的失败结果。工作统计仍严格要求白名单前台、未锁屏和最近 5 分钟有人机操作，离开电脑、切换窗口和锁屏立即结束会话。

- [ ] **步骤 4：运行测试并完成设置页手工验收**

确认：添加 PyCharm 后前台有效时间累计；后台运行、锁屏和空闲超过 5 分钟不累计；开机启动开关读回真实状态；关闭开关删除当前用户值；设置页不出现睡眠字段。

- [ ] **步骤 5：Commit**

```powershell
git -C G:\testPet add src\IkuyoPet.Infrastructure\Windows src\IkuyoPet.App\Views\SettingsView.xaml src\IkuyoPet.App\Views\SettingsView.xaml.cs tests\IkuyoPet.Infrastructure.Tests\Windows
git -C G:\testPet commit -m "feat: track whitelist apps and user startup"
```

## 任务 7：阶段验证、发布说明和安全边界复核

**文件：**
- 修改：`README.md`
- 修改：`LICENSE`
- 修改：`.gitignore`
- 创建：`assets/skins/default/manifest.json`
- 创建：`assets/skins/default/README.md`
- 创建：`.github/workflows/windows-ci.yml`
- 修改：`docs/superpowers/plans/2026-09-07-main-window-tray-log-plan.md`

- [ ] **步骤 1：添加原创默认皮肤和开源说明**

默认资源只包含原创或几何占位图，manifest 标注作者与许可；README 明确用户导入的喜多郁代等第三方素材不会随仓库发布，应用内 AI 生成工作室属于第二阶段并不是运行时依赖。`.gitignore` 排除 `data/`、`local-skins/`、SQLite WAL 文件和本地发布目录。

- [ ] **步骤 2：添加 Windows CI**

`.github/workflows/windows-ci.yml` 使用 `windows-latest`，固定执行：

```yaml
- run: dotnet restore IkuyoPet.sln
- run: dotnet build IkuyoPet.sln --configuration Release --no-restore -warnaserror
- run: dotnet test IkuyoPet.sln --configuration Release --no-build
```

- [ ] **步骤 3：运行完整验证命令**

```powershell
$env:Path='G:\IkuyoPetDev\dotnet;' + $env:Path
$env:DOTNET_ROOT='G:\IkuyoPetDev\dotnet'
$env:NUGET_PACKAGES='G:\IkuyoPetDev\nuget\packages'
G:\IkuyoPetDev\dotnet\dotnet.exe build G:\testPet\IkuyoPet.sln --configuration Release --no-restore -warnaserror
G:\IkuyoPetDev\dotnet\dotnet.exe test G:\testPet\IkuyoPet.sln --configuration Release --no-build
git -C G:\testPet diff --check
git -C G:\testPet status --short
```

预期：Release 构建 0 警告、0 错误，全部测试通过，`git diff --check` 无输出；手工启动发布目录时不需要 MySQL、SQLite 服务或管理员权限。

- [ ] **步骤 4：逐项手工验收**

1. 默认打开 A1a 今日优先；桌宠透明、可拖动、可隐藏。
2. T1 托盘只显示四项命令；关闭主窗口仍驻留托盘。
3. L1 时间线展示日期、时间、活动、渠道、完成 / 延后 / 跳过 / 未响应。
4. Windows 通知与桌宠共用事件 ID，重复激活不重复写日志。
5. PyCharm 前台、未锁屏、近 5 分钟有操作才累计工作时间。
6. 锁屏、全屏和演示状态不显示桌宠或横幅，不产生睡眠记录。
7. 无效皮肤不改变当前皮肤；有效透明皮肤可无重启替换。
8. CSV 不包含窗口标题、项目名、文件名、输入内容、截图或医疗字段。

- [ ] **步骤 5：Commit**

```powershell
git -C G:\testPet add README.md LICENSE .gitignore assets .github docs\superpowers\plans\2026-09-07-main-window-tray-log-plan.md
git -C G:\testPet commit -m "chore: document and verify Ikuyo Pet first stage"
```

## 执行约束

- 每个任务严格遵循“先写失败测试、运行确认失败、写最小实现、运行通过、单独提交”。
- 只在 `G:\testPet` 隔离工作区修改；开发工具和 NuGet 缓存保持在 `G:\IkuyoPetDev`。
- 不把医疗图片、报告文本、睡眠信息或第三方角色素材复制进源代码、日志或仓库。
- 医生后续建议通过规则配置、白名单和静默时段影响新提醒；既有日志保持不可变，A1a/T1/L1 的总体结构不改变。
- 第二阶段 AI 生成工作室单独建立接口和计划，本计划不添加联网图像服务、API 密钥或上传流程。

## 计划自检

- **规格覆盖度：** A/A1a 主窗口由任务 4；T1 托盘由任务 5；L1 日志和 CSV 由任务 1、2、4；SQLite 离线和隐私由任务 2、7；白名单工作时长和用户启动项由任务 6；提醒重试与统一动作由任务 3；皮肤与 AI 边界由任务 7 覆盖。
- **占位符扫描：** 每个代码任务都给出文件、类型签名、测试命令和预期结果，步骤可以直接执行。
- **类型一致性：** `ReminderRule`、`TrackedApplication`、`DashboardQueryService`、`WorkTrackingLoop`、`WindowsStartupManager` 在文件结构和对应任务中使用同一名称；仓储新增成员在任务 1 定义并在任务 2 实现。
