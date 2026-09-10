# 日志页工作统计与控件样式实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 在日志页筛选条和提醒日志之间增加日/周/月有效工作统计与前五应用横向柱状图，并移除日期框、下拉框的突兀蓝色焦点/选中样式。

**架构：** 新增 Core 工作统计查询服务，按系统本地日历范围读取并去重工作会话，按会话有效秒数与范围重叠比例分摊总时长和应用时长。App ViewModel 保存统计范围和可绑定的柱状图数据，日志页 XAML 只负责布局；现有 Dashboard 查询和实时工作增量继续作为数据来源，不改变提醒计时规则。

**技术栈：** .NET 10、C#、WPF/XAML、SQLite 仓储、xUnit/Microsoft Testing Platform。

---

## 文件边界

- 创建：`src/IkuyoPet.Core/Analytics/WorkStatisticsModels.cs`，定义统计范围、应用聚合项和统计结果。
- 创建：`src/IkuyoPet.Core/Analytics/WorkStatisticsQueryService.cs`，实现本地日/周/月范围、跨日会话去重和时长分摊。
- 修改：`src/IkuyoPet.App/MainWindowViewModel.cs`，增加统计范围、统计结果、总时长和前五应用绑定，并在现有刷新流程中加载统计。
- 修改：`src/IkuyoPet.App/App.xaml.cs`，为主 ViewModel 注入工作统计查询服务。
- 修改：`src/IkuyoPet.App/Views/LogView.xaml`，在筛选条与日志列表之间嵌入统计摘要、范围选择和横向柱状图。
- 修改：`src/IkuyoPet.App/Themes/LogFilterStyles.xaml`，关闭默认焦点可视化和蓝色文本选中效果，保留浅粉色可访问焦点边框。
- 创建：`tests/IkuyoPet.Core.Tests/Analytics/WorkStatisticsQueryServiceTests.cs`，覆盖日/周/月、跨午夜、应用聚合和前五排序。
- 修改：`tests/IkuyoPet.Infrastructure.Tests/App/MainWindowViewModelSettingsTests.cs`，覆盖范围切换和统计绑定刷新。
- 修改：`tests/IkuyoPet.Infrastructure.Tests/App/LogFilterStyleContractTests.cs`，覆盖无蓝色 FocusVisualStyle 和浅色 SelectionBrush 契约。

### 任务 1：工作统计模型与范围计算

**文件：**
- 创建：`src/IkuyoPet.Core/Analytics/WorkStatisticsModels.cs`
- 创建：`tests/IkuyoPet.Core.Tests/Analytics/WorkStatisticsQueryServiceTests.cs`

- [ ] **步骤 1：编写失败测试**

编写测试，要求 `WorkStatisticsQueryService.GetAsync` 返回：

```csharp
var result = await service.GetAsync(new DateOnly(2026, 9, 10), WorkStatisticsPeriod.Week, token);
Assert.Equal(TimeSpan.FromMinutes(90), result.TotalWorkTime);
Assert.Equal(["Word", "PyCharm"], result.TopApplications.Select(item => item.DisplayName));
```

测试还应验证：周范围从周一开始、月范围覆盖自然月、跨午夜会话按重叠比例分摊、同一会话被多日查询返回时只计算一次、榜单最多五项并按秒数降序排列。

- [ ] **步骤 2：运行测试确认失败**

运行：

```powershell
& G:\IkuyoPetDev\dotnet\dotnet.exe test --project G:\testPet\tests\IkuyoPet.Core.Tests\IkuyoPet.Core.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~WorkStatisticsQueryServiceTests"
```

预期：因统计模型和服务尚未存在而编译失败。

- [ ] **步骤 3：编写最小模型**

定义 `WorkStatisticsPeriod`（Day/Week/Month）、`WorkApplicationUsage`（进程名、显示名、有效秒数、占比、格式化文本）和 `WorkStatistics`（范围、总秒数、前五应用）。

- [ ] **步骤 4：运行测试确认模型契约通过**

先运行同一过滤命令，确认失败原因从缺少类型转为服务行为断言失败。

- [ ] **步骤 5：提交模型契约**

```powershell
git -C G:\testPet add src/IkuyoPet.Core/Analytics/WorkStatisticsModels.cs tests/IkuyoPet.Core.Tests/Analytics/WorkStatisticsQueryServiceTests.cs
git -C G:\testPet commit -m "test: define work statistics contract"
```

### 任务 2：实现统计查询与跨日分摊

**文件：**
- 创建：`src/IkuyoPet.Core/Analytics/WorkStatisticsQueryService.cs`
- 修改：`tests/IkuyoPet.Core.Tests/Analytics/WorkStatisticsQueryServiceTests.cs`

- [ ] **步骤 1：实现范围边界**

根据 `WorkStatisticsPeriod` 将选中日期映射为本地 `[start, end)`：日为一天，周为周一至下周一，月为当月一日至下月一日；使用 `TimeZoneInfo.Local` 转换 UTC 查询边界。

- [ ] **步骤 2：读取并去重会话**

对范围覆盖的本地日期逐日调用现有 `ReadWorkSessionsAsync(DateOnly, ...)`，以 `WorkSession.Id` 去重，兼容现有仓储接口，同时避免跨午夜会话重复累计。

- [ ] **步骤 3：实现时长分摊**

仅统计 `ActiveSeconds > 0` 的会话；将会话与统计范围的 UTC 重叠时长除以会话墙上时长，再乘以 `ActiveSeconds`，分别累计总时长和进程聚合时长。

- [ ] **步骤 4：运行统计测试确认通过**

```powershell
& G:\IkuyoPetDev\dotnet\dotnet.exe test --project G:\testPet\tests\IkuyoPet.Core.Tests\IkuyoPet.Core.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~WorkStatisticsQueryServiceTests"
```

预期：所有日/周/月、跨日、去重和前五排序测试通过。

- [ ] **步骤 5：提交查询服务**

```powershell
git -C G:\testPet add src/IkuyoPet.Core/Analytics/WorkStatisticsQueryService.cs tests/IkuyoPet.Core.Tests/Analytics/WorkStatisticsQueryServiceTests.cs
git -C G:\testPet commit -m "feat: aggregate work statistics by period"
```

### 任务 3：接入 App ViewModel 和刷新流程

**文件：**
- 修改：`src/IkuyoPet.App/MainWindowViewModel.cs`
- 修改：`src/IkuyoPet.App/App.xaml.cs`
- 修改：`tests/IkuyoPet.Infrastructure.Tests/App/MainWindowViewModelSettingsTests.cs`

- [ ] **步骤 1：编写失败测试**

覆盖：设置 `StatisticsPeriod` 后重新加载选中日期；`WorkStatisticsTotalText` 返回小时/分钟；`TopApplicationStats` 包含最多五项，并且刷新后保留当前 `ApplyActiveWorkDelta` 的实时增量。

- [ ] **步骤 2：运行测试确认失败**

```powershell
& G:\IkuyoPetDev\dotnet\dotnet.exe test --project G:\testPet\tests\IkuyoPet.Infrastructure.Tests\IkuyoPet.Infrastructure.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~MainWindowViewModelSettingsTests"
```

- [ ] **步骤 3：实现最小绑定状态**

在 ViewModel 中增加 `StatisticsPeriodKey`（`day`/`week`/`month`）、`WorkStatistics`、`WorkStatisticsTotalText`、`TopApplicationStats`；范围或日期变化时通过现有 `RefreshAsync` 重新查询。统计查询服务由 `App.xaml.cs` 用同一个 SQLite 仓储注入。

- [ ] **步骤 4：合并实时会话显示**

`WorkStatisticsTotalText` 在日范围中叠加当前 `liveWorkSeconds`，周/月范围不重复叠加已包含在每日仓储结果中的会话；应用榜单只显示已落盘会话，并在日范围当前进程有实时增量时更新对应项。

- [ ] **步骤 5：运行应用层测试确认通过**

运行任务 3 的测试命令，预期全部通过。

- [ ] **步骤 6：提交 ViewModel 接入**

```powershell
git -C G:\testPet add src/IkuyoPet.App/MainWindowViewModel.cs src/IkuyoPet.App/App.xaml.cs tests/IkuyoPet.Infrastructure.Tests/App/MainWindowViewModelSettingsTests.cs
git -C G:\testPet commit -m "feat: bind work statistics to log view"
```

### 任务 4：日志页统计区与焦点样式

**文件：**
- 修改：`src/IkuyoPet.App/Views/LogView.xaml`
- 修改：`src/IkuyoPet.App/Themes/LogFilterStyles.xaml`
- 修改：`tests/IkuyoPet.Infrastructure.Tests/App/LogFilterStyleContractTests.cs`

- [ ] **步骤 1：编写失败样式测试**

断言日志页包含统计范围选择、总时长绑定和前五应用 `ItemsControl`；样式包含 `FocusVisualStyle` 关闭、浅色 `SelectionBrush` 和粉色焦点边框，不包含默认蓝色焦点装饰。

- [ ] **步骤 2：运行测试确认失败**

```powershell
& G:\IkuyoPetDev\dotnet\dotnet.exe test --project G:\testPet\tests\IkuyoPet.Infrastructure.Tests\IkuyoPet.Infrastructure.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~LogFilterStyleContractTests"
```

- [ ] **步骤 3：实现 XAML 统计区**

将日志页顶层网格扩展为标题、筛选、统计、日志四行；统计区使用日/周/月选择器、总时长文本、横向 `ProgressBar` 列表和无数据提示。柱状图最大值绑定为 1，应用占比绑定为 `Value`，前景使用 Ikuyo Pet 粉色。

- [ ] **步骤 4：修复控件选择样式**

为日期框、下拉框及其 ToggleButton/ComboBoxItem 设置 `FocusVisualStyle={x:Null}`；日期文本框设置浅粉色 `SelectionBrush` 与 `SelectionTextBrush`；焦点触发器只改变 `Chrome` 边框，不改变整个控件背景。

- [ ] **步骤 5：运行样式测试确认通过**

运行任务 4 的测试命令，预期通过。

- [ ] **步骤 6：提交 UI**

```powershell
git -C G:\testPet add src/IkuyoPet.App/Views/LogView.xaml src/IkuyoPet.App/Themes/LogFilterStyles.xaml tests/IkuyoPet.Infrastructure.Tests/App/LogFilterStyleContractTests.cs
git -C G:\testPet commit -m "feat: add log analytics panel"
```

### 任务 5：全量验证与发布

**文件：**
- 修改：无（仅生成发布目录）

- [ ] **步骤 1：运行格式和构建检查**

```powershell
git -C G:\testPet diff --check
& G:\IkuyoPetDev\dotnet\dotnet.exe build G:\testPet\IkuyoPet.sln --configuration Release --no-restore -warnaserror
```

预期 0 警告、0 错误。

- [ ] **步骤 2：运行全量测试**

```powershell
& G:\IkuyoPetDev\dotnet\dotnet.exe test --solution G:\testPet\IkuyoPet.sln --configuration Release --no-restore
```

预期原有测试和本次新增测试全部通过。

- [ ] **步骤 3：发布并进行启动烟测**

精确停止当前 `G:\testPet\artifacts\publish\win-x64\IkuyoPet.exe` 后运行：

```powershell
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File G:\testPet\eng\publish-local.ps1 -SkipRestore
Start-Process G:\testPet\artifacts\publish\win-x64\IkuyoPet.exe
```

确认主窗口打开后进入“日志”，日/周/月切换、图表空状态、前五排序和控件焦点样式正常。

- [ ] **步骤 4：提交发布前源码状态**

```powershell
git -C G:\testPet status --short
git -C G:\testPet log -1 --oneline
```

最终报告发布路径、测试数量、统计口径和未计入后台/空闲时间的限制。
