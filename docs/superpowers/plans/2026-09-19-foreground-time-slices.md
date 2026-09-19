# 前台切屏时间片追踪实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 使用 Windows 前台窗口事件补足轮询采样，使多个白名单软件快速切换时的有效工作时间按真实时间片累计。

**架构：** 新增可测试的前台样本事件源；生产实现使用 WinEventHook，测试使用内存事件源。`WorkTrackingService` 统一处理轮询和事件样本，并用增量元数据区分“已写入会话”和“仍在内存中的实时秒数”，避免 UI 重复统计。

**技术栈：** .NET 10、WPF、Windows User32 WinEventHook、`System.Threading.Channels`、xUnit v3。

---

### 任务 1：补充服务行为测试

**文件：**
- 修改：`tests/IkuyoPet.Infrastructure.Tests/Windows/WorkTrackingServiceTests.cs`
- 修改：`src/IkuyoPet.Core/WorkTracking/WorkTrackingModels.cs`

- [x] **步骤 1：编写失败测试**

添加测试，直接向工作追踪服务提交 `A@t0`、`B@t3`、`A@t7` 样本，断言已完成会话分别为 A=3 秒、B=4 秒，并断言切换时增量标记为已落库。

- [x] **步骤 2：运行测试验证失败**

运行：`dotnet test --solution .\\IkuyoPet.sln --configuration Release --no-restore --filter FullyQualifiedName~WorkTrackingServiceTests`

预期：因缺少样本处理入口或增量落库标记而失败。

### 任务 2：统一样本处理和落库状态

**文件：**
- 修改：`src/IkuyoPet.Core/WorkTracking/WorkTrackingModels.cs`
- 修改：`src/IkuyoPet.Infrastructure/Windows/WorkTrackingService.cs`

- [x] **步骤 1：实现显式样本处理入口**

将轮询捕获与事件样本都接入同一个 `ObserveAsync(ActivitySample, CancellationToken)` 流程；普通连续样本返回未落库增量，发生应用切换并刷新上一会话时返回 `IsPersisted=true` 的增量。

- [x] **步骤 2：运行服务测试验证通过**

运行同一过滤测试，预期新增快速切屏测试和原有服务测试全部通过。

### 任务 3：新增可测试的前台事件源和事件循环

**文件：**
- 创建：`src/IkuyoPet.Infrastructure/Windows/IForegroundActivityChangeSource.cs`
- 创建：`src/IkuyoPet.Infrastructure/Windows/WindowsForegroundActivityChangeSource.cs`
- 修改：`src/IkuyoPet.Infrastructure/Windows/WorkTrackingLoop.cs`
- 修改：`tests/IkuyoPet.Infrastructure.Tests/Windows/WorkTrackingLoopTests.cs`

- [x] **步骤 1：编写内存事件源测试**

用测试事件源发布两个前台样本，断言循环按发布顺序消费，并在事件源停止后仍能刷新最后会话。

- [x] **步骤 2：运行测试验证失败**

运行：`dotnet test --solution .\\IkuyoPet.sln --configuration Release --no-restore --filter FullyQualifiedName~WorkTrackingLoopTests`

预期：因循环尚未订阅事件源而失败。

- [x] **步骤 3：实现事件消费和轮询回退**

使用无界 `Channel<ActivitySample>` 串行消费事件；事件源启动失败只记录诊断，不阻断现有定时轮询；停止时取消事件源并调用现有服务刷新逻辑。

- [x] **步骤 4：实现 WinEventHook 生产源**

在后台线程创建消息队列，使用 `SetWinEventHook(EVENT_SYSTEM_FOREGROUND, ...)`，回调通过未缓存的前台探针捕获样本并写入事件；`Dispose` 发送退出消息并解除钩子。

- [x] **步骤 5：运行循环测试验证通过**

运行 `WorkTrackingLoopTests`，预期全部通过且无死循环或未观察异常。

### 任务 4：接入应用并修正实时统计合并

**文件：**
- 修改：`src/IkuyoPet.App/App.xaml.cs`
- 修改：`src/IkuyoPet.App/MainWindowViewModel.cs`
- 修改：`tests/IkuyoPet.Infrastructure.Tests/App/MainWindowViewModelSettingsTests.cs`

- [x] **步骤 1：添加重复统计回归测试**

构造已落库增量后刷新仪表盘，断言主界面只显示一次；普通未落库增量仍会即时显示。

- [x] **步骤 2：运行测试验证失败**

运行：`dotnet test --solution .\\IkuyoPet.sln --configuration Release --no-restore --filter FullyQualifiedName~MainWindowViewModelSettingsTests`

预期：已落库增量场景显示多算而失败。

- [x] **步骤 3：接入未缓存探针事件源**

应用创建 `WindowsForegroundActivityChangeSource(rawActivityProbe)`，工作循环同时接收事件和原有 30 秒校正轮询；退出时由循环释放事件源。

- [x] **步骤 4：按落库标记合并实时秒数**

主界面收到 `IsPersisted=true` 时清空实时缓存但不再次累加；未落库增量保持原有即时显示逻辑。

- [x] **步骤 5：运行应用层测试验证通过**

运行 `MainWindowViewModelSettingsTests`，预期新增和现有断言全部通过。

### 任务 5：完整验证和记录

**文件：**
- 修改：`docs/manual-tests/work-tracking.md`（若该文件存在则追加验证步骤）

- [x] **步骤 1：运行构建和全量测试**

运行：

```powershell
dotnet build .\\IkuyoPet.sln --configuration Release --no-restore -warnaserror
dotnet test --solution .\\IkuyoPet.sln --configuration Release --no-build --no-restore
```

预期：构建 0 警告、0 错误；全部测试通过。

- [ ] **步骤 2：执行手动快速切屏验证**

将两个编辑器和一个非白名单程序加入/移出白名单，连续切换 1 分钟，核对每个应用时间片之和、总有效工作时间、锁屏后暂停和跨午夜拆分。

- [ ] **步骤 3：提交实现**

```powershell
git add src tests docs/superpowers/specs docs/superpowers/plans
git commit -m "fix: track foreground work in precise time slices"
```
