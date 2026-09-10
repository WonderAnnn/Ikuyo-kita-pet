# UI 修整与工作时长口径实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法跟踪进度。

**目标：** 实现 A 方案控件视觉、滚动统计范围、累计/今日工作时长双层展示和设置页版本号。

**架构：** Core 负责滚动日期范围和统计边界；SQLite 仓储提供全量工作会话读取；WPF ViewModel 组合累计与实时值，视图只负责绑定和样式。所有视觉回归通过 XAML 契约测试锁定。

**技术栈：** C# 14、.NET 10、WPF、Microsoft.Data.Sqlite、xUnit v3。

---

### 任务 1：滚动统计范围与范围文案

**文件：**
- 修改：`src/IkuyoPet.Core/Analytics/WorkStatisticsQueryService.cs`
- 修改：`src/IkuyoPet.App/MainWindowViewModel.cs`
- 修改：`src/IkuyoPet.App/Views/LogView.xaml`
- 测试：`tests/IkuyoPet.Core.Tests/Analytics/WorkStatisticsQueryServiceTests.cs`
- 测试：`tests/IkuyoPet.Infrastructure.Tests/App/MainWindowViewModelSettingsTests.cs`
- 测试：`tests/IkuyoPet.Infrastructure.Tests/App/LogFilterStyleContractTests.cs`

- [ ] 编写失败测试：周范围为选定日期前 6 天至次日，月范围为前 29 天至次日；ViewModel 暴露北京时间范围文案。
- [ ] 运行 Core 与 Infrastructure 定向测试确认红灯。
- [ ] 实现滚动日期范围和 `WorkStatisticsRangeText`。
- [ ] 在总有效工作后显示范围文本。
- [ ] 运行定向测试确认通过。

### 任务 2：修复筛选控件和导航焦点视觉

**文件：**
- 修改：`src/IkuyoPet.App/Themes/LogFilterStyles.xaml`
- 修改：`src/IkuyoPet.App/App.xaml`
- 测试：`tests/IkuyoPet.Infrastructure.Tests/App/LogFilterStyleContractTests.cs`

- [ ] 编写失败契约：ComboBox 使用透明自定义 ToggleButton 模板，导航样式关闭 FocusVisualStyle。
- [ ] 运行契约测试确认红灯。
- [ ] 实现 A 方案样式，保留浅粉色选中项和细粉色焦点边框。
- [ ] 运行契约测试确认通过。

### 任务 3：主页面累计与今日工作双层展示

**文件：**
- 修改：`src/IkuyoPet.Core/Storage/IEventRepository.cs`
- 修改：`src/IkuyoPet.Infrastructure/Storage/SqliteEventRepository.cs`
- 修改：`src/IkuyoPet.App/MainWindowViewModel.cs`
- 修改：`src/IkuyoPet.App/Views/TodayView.xaml`
- 测试：`tests/IkuyoPet.Infrastructure.Tests/Storage/SqliteEventRepositoryTests.cs`
- 测试：`tests/IkuyoPet.Infrastructure.Tests/App/MainWindowViewModelSettingsTests.cs`

- [ ] 编写失败测试：全量会话可读，主页面同时暴露累计和今日文案。
- [ ] 运行测试确认红灯。
- [ ] 增加仓储全量读取（旧实现默认抛出 NotSupported），ViewModel 叠加实时秒数。
- [ ] 更新今日卡片布局。
- [ ] 运行定向和全量测试确认通过。

### 任务 4：版本号与设置页显示

**文件：**
- 创建：`src/IkuyoPet.App/AppVersion.cs`
- 修改：`src/IkuyoPet.App/IkuyoPet.App.csproj`
- 修改：`src/IkuyoPet.App/MainWindowViewModel.cs`
- 修改：`src/IkuyoPet.App/Views/SettingsView.xaml`
- 测试：`tests/IkuyoPet.Infrastructure.Tests/App/LogFilterStyleContractTests.cs`

- [ ] 编写失败契约：设置页绑定版本文本且项目版本为 `0.1.1`。
- [ ] 运行测试确认红灯。
- [ ] 添加单一版本源和设置页底部文本。
- [ ] 运行测试确认通过。

### 任务 5：完整验证与发布

- [ ] 运行 `git diff --check`。
- [ ] 运行完整解决方案测试。
- [ ] 运行 Release 构建并确认 0 警告、0 错误。
- [ ] 发布自包含 EXE，启动冒烟测试。
- [ ] 提交本次变更。
