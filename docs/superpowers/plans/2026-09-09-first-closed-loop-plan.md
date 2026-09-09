# Ikuyo Pet 第一次完整闭环实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）逐任务实现本计划。每个任务先用 TDD 看到失败，再由规格审查子代理和代码质量审查子代理依次审查。步骤使用复选框（`- [ ]`）跟踪进度。

**目标：** 按 `docs/superpowers/specs/2026-09-09-first-closed-loop-design.md` 实现可运行的 Windows 离线提醒闭环，并在当前机器生成包含本机私有皮肤的自包含 EXE 发布目录。

**架构：** Core 保存规则区间、随机阈值、有效工作秒数和提醒状态；Infrastructure 负责 SQLite 迁移、Windows 锁屏/全屏探针、持久化重提和共享采样；Pet 负责安全皮肤加载、406×996 透明立绘的等比布局和反馈气泡；App 负责首次默认规则、设置、日志刷新、托盘和生命周期。用户图片仅作为 Git 忽略的本机/发布素材，缺失时回退占位皮肤。

**技术栈：** C# 14、.NET 10、WPF、Microsoft.Data.Sqlite、Windows App SDK 通知、H.NotifyIcon.Wpf、xUnit v3；使用 `G:\IkuyoPetDev\dotnet\dotnet.exe` 和 `G:\IkuyoPetDev\nuget\packages`。

---

## 文件总览

### 预计创建

- `src/IkuyoPet.Core/Reminders/ReminderRuntimeState.cs`：可持久化的随机周期与重提状态。
- `src/IkuyoPet.Core/Reminders/ActiveWorkReminderScheduler.cs`：只消费有效工作秒数的纯状态机。
- `src/IkuyoPet.Core/Skins/SkinSelection.cs`：当前皮肤选择和安全路径契约。
- `src/IkuyoPet.Infrastructure/Storage/SkinSelectionStore.cs`：本地选择文件的原子读写。
- `src/IkuyoPet.Infrastructure/Windows/WindowsSessionProbe.cs`：锁屏、全屏和演示状态探针。
- `src/IkuyoPet.Pet/Skins/SkinAssetSet.cs`：idle/remind/可选反馈图路径。
- `src/IkuyoPet.Pet/Skins/SkinBootstrapper.cs`：校验、解析和占位回退。
- `tests/IkuyoPet.Core.Tests/Reminders/ActiveWorkReminderSchedulerTests.cs`：随机周期和状态转换测试。
- `tests/IkuyoPet.Infrastructure.Tests/Skins/SkinSelectionStoreTests.cs`：路径隔离和持久化测试。
- `tests/IkuyoPet.Infrastructure.Tests/Windows/WindowsSessionProbeTests.cs`：探针替身和抑制规则测试。
- `tests/IkuyoPet.Infrastructure.Tests/EndToEnd/FirstClosedLoopTests.cs`：到期、动作、重提、日志和下一轮仿真。

### 预计修改

- `src/IkuyoPet.Core/Reminders/ReminderRule.cs`
- `src/IkuyoPet.Core/Storage/IEventRepository.cs`
- `src/IkuyoPet.Infrastructure/Storage/DatabaseMigrator.cs`
- `src/IkuyoPet.Infrastructure/Storage/SqliteEventRepository.cs`
- `src/IkuyoPet.Infrastructure/Windows/WorkTrackingService.cs`
- `src/IkuyoPet.Infrastructure/Windows/WorkTrackingLoop.cs`
- `src/IkuyoPet.Infrastructure/Windows/ForegroundActivityProbe.cs`
- `src/IkuyoPet.Infrastructure/Windows/ReminderLoop.cs`
- `src/IkuyoPet.Infrastructure/Windows/ReminderActionCoordinator.cs`
- `src/IkuyoPet.Pet/PetWindow.xaml`
- `src/IkuyoPet.Pet/PetWindow.xaml.cs`
- `src/IkuyoPet.Pet/PetReminderPresenter.cs`
- `src/IkuyoPet.Pet/Skins/SkinManifest.cs`
- `src/IkuyoPet.Pet/Skins/SkinPackageValidator.cs`
- `src/IkuyoPet.App/App.xaml.cs`
- `src/IkuyoPet.App/MainWindowViewModel.cs`
- `src/IkuyoPet.App/Views/SettingsView.xaml`
- `src/IkuyoPet.App/Views/TodayView.xaml`
- `src/IkuyoPet.App/Views/LogView.xaml`
- `README.md`
- `.gitignore`

---

### 任务 1：强化透明皮肤校验、选择和等比桌宠加载

**文件：**
- 创建：`src/IkuyoPet.Core/Skins/SkinSelection.cs`
- 创建：`src/IkuyoPet.Infrastructure/Storage/SkinSelectionStore.cs`
- 创建：`src/IkuyoPet.Pet/Skins/SkinAssetSet.cs`
- 创建：`src/IkuyoPet.Pet/Skins/SkinBootstrapper.cs`
- 修改：`src/IkuyoPet.Pet/Skins/SkinPackageValidator.cs`
- 修改：`src/IkuyoPet.Pet/PetWindow.xaml`
- 修改：`src/IkuyoPet.Pet/PetWindow.xaml.cs`
- 修改：`tests/IkuyoPet.Infrastructure.Tests/Skins/SkinPackageValidatorTests.cs`
- 创建：`tests/IkuyoPet.Infrastructure.Tests/Skins/SkinSelectionStoreTests.cs`

- [ ] **步骤 1：先写失败测试**

增加真实 PNG 测试：406×996 的透明 PNG 通过；manifest 写成其他尺寸时拒绝；扩展名为 `.png` 但 PNG 签名错误时拒绝；选择路径越出 skins 根目录时拒绝；选择文件不存在或 manifest 损坏时回退占位皮肤。增加比例测试：容器 160×280 时宽高为约 114.14×280，误差小于 0.01。

- [ ] **步骤 2：运行测试确认红灯**

运行：

```powershell
G:\IkuyoPetDev\dotnet\dotnet.exe test G:\testPet\tests\IkuyoPet.Infrastructure.Tests\IkuyoPet.Infrastructure.Tests.csproj --filter "FullyQualifiedName~Skin"
```

预期：新测试因实际尺寸校验、选择存储、bootstrapper 或等比布局 API 尚不存在而失败。

- [ ] **步骤 3：实现最小皮肤边界**

`SkinPackageValidator` 必须检查 PNG 文件头、manifest 画布尺寸和两张图片的实际像素尺寸一致，并保留现有透明像素检查。`SkinSelectionStore` 使用 `%LocalAppData%\IkuyoPet\skins\active-skin.json`，写入临时文件后原子替换；解析结果只能落在 skins 根目录下。

`SkinBootstrapper` 的解析顺序固定为：当前选择 → 重新校验 → 返回 `SkinAssetSet`；失败则返回占位资源并提供错误文本，不抛出启动级异常。不要把图片二进制写入 SQLite。

- [ ] **步骤 4：接入 PetWindow 的等比显示**

把图片区域改为最大 160×280 DIP，`Stretch="Uniform"`、居中、底部对齐。406×996 图必须显示为约 114×280，不得使用 `Fill` 或单独拉伸宽高。启动加载 `idle.png`；提醒时切换 `remind.png`；反馈结束后恢复 idle。拖动命中区域不得覆盖整个透明窗口。

- [ ] **步骤 5：运行皮肤测试并提交**

运行同一过滤测试，再运行 `git -C G:\testPet diff --check`。预期皮肤相关测试全部通过，工作树只包含本任务文件。

```powershell
git -C G:\testPet add src tests
git -C G:\testPet commit -m "feat: load private transparent pet skin safely"
```

---

### 任务 2：扩展规则模型、SQLite 运行状态和随机周期状态机

**文件：**
- 创建：`src/IkuyoPet.Core/Reminders/ReminderRuntimeState.cs`
- 创建：`src/IkuyoPet.Core/Reminders/ActiveWorkReminderScheduler.cs`
- 修改：`src/IkuyoPet.Core/Reminders/ReminderRule.cs`
- 修改：`src/IkuyoPet.Core/Storage/IEventRepository.cs`
- 修改：`src/IkuyoPet.Infrastructure/Storage/DatabaseMigrator.cs`
- 修改：`src/IkuyoPet.Infrastructure/Storage/SqliteEventRepository.cs`
- 创建：`tests/IkuyoPet.Core.Tests/Reminders/ActiveWorkReminderSchedulerTests.cs`
- 修改：`tests/IkuyoPet.Infrastructure.Tests/Storage/SqliteEventRepositoryTests.cs`

- [ ] **步骤 1：写失败的纯 Core 测试**

覆盖以下行为：随机源只在新周期调用一次；只接受 40、45、50 等区间内整数；累计有效秒数达到目标才产生 due；应用重启从已持久化的目标和累计值继续；完成/跳过结束本轮并创建新目标；搁置创建 5 分钟重提时间；第三次无响应转为 `Unanswered`。

测试 API 固定为 `ActiveWorkReminderScheduler.ObserveActiveSeconds(int seconds)`、`StartNewCycle()`、`Apply(ReminderAction action, DateTimeOffset now)`，随机源和运行状态通过构造函数注入，不能在测试中依赖系统随机数或当前时间。

- [ ] **步骤 2：运行测试确认红灯**

运行：

```powershell
G:\IkuyoPetDev\dotnet\dotnet.exe test G:\testPet\tests\IkuyoPet.Core.Tests\IkuyoPet.Core.Tests.csproj --filter "FullyQualifiedName~ActiveWorkReminderScheduler"
```

预期：因新状态模型和调度器尚不存在而失败。

- [ ] **步骤 3：实现兼容的规则模型和状态机**

保留旧构造函数和 `IntervalMinutes` 读取兼容性，新增 `IntervalMinMinutes`、`IntervalMaxMinutes`、`ActivityDurationMinutes`、`ParameterSource` 和 `ParameterVersion`。旧规则没有区间时把单值间隔视为 min=max；默认新活动规则为 40–50 分钟、活动建议 5 分钟、来源 `general-default`。

状态机持久化字段必须包含：`rule_id`、`cycle_id`、`target_active_seconds`、`accumulated_active_seconds`、`state`、`attempt`、`retry_due_at`、`updated_at`。Core 不负责 SQLite，不读取窗口或医疗资料。

- [ ] **步骤 4：写迁移和仓储 round-trip 测试**

在 SQLite 中新增 `reminder_runtime_state` 表，并为 `reminder_rules` 与 `reminder_events` 增加区间、建议时长、参数来源/版本和事件建议时长快照字段。旧数据库迁移后已有日志和规则仍可读；随机周期状态可以写入、读取和覆盖更新。所有 SQL 继续参数化。

- [ ] **步骤 5：运行 Core + Infrastructure 测试并提交**

运行：

```powershell
G:\IkuyoPetDev\dotnet\dotnet.exe test G:\testPet\IkuyoPet.sln --configuration Debug
```

预期：原有测试和新增测试全部通过。

```powershell
git -C G:\testPet add src tests
git -C G:\testPet commit -m "feat: persist randomized active work reminder cycles"
```

---

### 任务 3：共享有效工作采样并补齐锁屏/全屏抑制

**文件：**
- 创建：`src/IkuyoPet.Infrastructure/Windows/WindowsSessionProbe.cs`
- 修改：`src/IkuyoPet.Infrastructure/Windows/ForegroundActivityProbe.cs`
- 修改：`src/IkuyoPet.Infrastructure/Windows/WorkTrackingService.cs`
- 修改：`src/IkuyoPet.Infrastructure/Windows/WorkTrackingLoop.cs`
- 创建：`tests/IkuyoPet.Infrastructure.Tests/Windows/WindowsSessionProbeTests.cs`
- 修改：`tests/IkuyoPet.Infrastructure.Tests/Windows/WorkTrackingServiceTests.cs`

- [ ] **步骤 1：先写失败测试**

增加可注入 session probe：锁屏、演示、全屏或空闲状态时，返回的有效秒数为 0；恢复普通桌面后只从新采样开始。验证工作统计和提醒调度可以消费同一份 `ActiveWorkDelta`，不会重复探测或累计睡眠时间。

- [ ] **步骤 2：运行测试确认红灯**

```powershell
G:\IkuyoPetDev\dotnet\dotnet.exe test G:\testPet\tests\IkuyoPet.Infrastructure.Tests\IkuyoPet.Infrastructure.Tests.csproj --filter "FullyQualifiedName~WorkTracking|FullyQualifiedName~WindowsSession"
```

- [ ] **步骤 3：实现 Windows 适配和共享增量**

使用 Windows API 读取真实锁屏/会话状态；全屏与演示检测保持独立接口。`WorkTrackingService` 每次有效采样返回本次新增秒数，同时继续按应用写工作会话；`WorkTrackingLoop` 只采样一次，将增量交给活动提醒协调器。长采样缺口、系统睡眠、锁屏和离开电脑不计入。

- [ ] **步骤 4：运行全量测试并提交**

```powershell
G:\IkuyoPetDev\dotnet\dotnet.exe test G:\testPet\IkuyoPet.sln --configuration Debug
git -C G:\testPet add src tests
git -C G:\testPet commit -m "feat: share valid work samples and suppress locked desktop"
```

---

### 任务 4：接通提醒循环、重提、默认规则和动作幂等

**文件：**
- 修改：`src/IkuyoPet.Infrastructure/Windows/ReminderLoop.cs`
- 修改：`src/IkuyoPet.Infrastructure/Windows/ReminderActionCoordinator.cs`
- 修改：`src/IkuyoPet.Infrastructure/Windows/WindowsNotificationPresenter.cs`
- 修改：`src/IkuyoPet.Infrastructure/Windows/ForegroundActivityProbe.cs`
- 修改：`src/IkuyoPet.App/App.xaml.cs`
- 修改：`src/IkuyoPet.App/MainWindowViewModel.cs`
- 创建：`tests/IkuyoPet.Infrastructure.Tests/EndToEnd/FirstClosedLoopTests.cs`
- 修改：`tests/IkuyoPet.Infrastructure.Tests/Windows/ReminderLoopTests.cs`
- 修改：`tests/IkuyoPet.Infrastructure.Tests/Windows/ReminderActionCoordinatorTests.cs`

- [ ] **步骤 1：写失败的闭环测试**

使用固定随机源和伪时钟完成仿真：首次启动自动创建默认“离屏活动”规则；累计达到目标后只呈现一次；接受写入完成并开始下一轮；搁置 5 分钟后重提；三次无响应后写入 unanswered；应用重启恢复同一目标和累计值；桌宠不可用时事件渠道更新为 notification；锁屏/全屏恢复后延迟 2 分钟且只补发一条。

- [ ] **步骤 2：运行测试确认红灯**

```powershell
G:\IkuyoPetDev\dotnet\dotnet.exe test G:\testPet\tests\IkuyoPet.Infrastructure.Tests\IkuyoPet.Infrastructure.Tests.csproj --filter "FullyQualifiedName~FirstClosedLoop|FullyQualifiedName~ReminderLoop|FullyQualifiedName~ReminderActionCoordinator"
```

- [ ] **步骤 3：实现默认规则和持久化循环**

首次规则表为空时只写入活动规则；已有规则不覆盖。提醒循环读取持久化运行状态而不是内存 `lastDisplayedAt`，同一事件在写入成功后才呈现。所有动作使用条件更新保证重复点击只落一次结果。重提调度和随机下一轮状态必须在退出前落库。

- [ ] **步骤 4：实现锁屏/全屏恢复策略和通知降级**

抑制期间不显示气泡或系统横幅，不创建多条补发事件；恢复普通桌面并连续满足 2 分钟后再呈现 pending。Presenter 初始化失败时降级 notification，通知请求仍携带同一事件 ID 与动作枚举。

- [ ] **步骤 5：运行全量测试并提交**

```powershell
G:\IkuyoPetDev\dotnet\dotnet.exe test G:\testPet\IkuyoPet.sln --configuration Debug
git -C G:\testPet add src tests
git -C G:\testPet commit -m "feat: close reminder retry and fallback loop"
```

---

### 任务 5：接通反馈气泡、日志刷新、设置持久化和桌宠生命周期

**文件：**
- 修改：`src/IkuyoPet.Pet/PetReminderPresenter.cs`
- 修改：`src/IkuyoPet.Pet/PetWindow.xaml.cs`
- 修改：`src/IkuyoPet.App/MainWindowViewModel.cs`
- 修改：`src/IkuyoPet.App/Views/TodayView.xaml`
- 修改：`src/IkuyoPet.App/Views/LogView.xaml`
- 修改：`src/IkuyoPet.App/Views/SettingsView.xaml`
- 修改：`src/IkuyoPet.App/App.xaml.cs`
- 修改：`tests/IkuyoPet.Infrastructure.Tests/Pet/PetReminderPresenterTests.cs`
- 修改：`tests/IkuyoPet.Core.Tests/Dashboard/MainWindowViewModelTests.cs`

- [ ] **步骤 1：写失败测试**

断言接受、搁置、跳过分别产生对应反馈文本和一个颜文字；反馈结束后皮肤回 idle；动作后今日计数、时间线和当前规则状态立即刷新；桌宠开关和当前皮肤重启后保持。

- [ ] **步骤 2：运行测试确认红灯**

```powershell
G:\IkuyoPetDev\dotnet\dotnet.exe test G:\testPet\tests\IkuyoPet.Infrastructure.Tests\IkuyoPet.Infrastructure.Tests.csproj --filter "FullyQualifiedName~PetReminderPresenter"; G:\IkuyoPetDev\dotnet\dotnet.exe test G:\testPet\tests\IkuyoPet.Core.Tests\IkuyoPet.Core.Tests.csproj --filter "FullyQualifiedName~MainWindowViewModel"
```

- [ ] **步骤 3：实现反馈与状态刷新**

沿用句内加粗文本，不改底层动作枚举；完成使用 `ദ്ദി˶>𖥦<)✧`，搁置使用 `(,,•́ . •̀,,)`，跳过/无响应结束使用 `ʕ.•᷅ࡇ•᷄.ʔ`，每段最多一个。通过单一刷新入口更新今日卡片、时间线和日志筛选，不让 View 直接写 SQLite。

- [ ] **步骤 4：实现设置持久化**

把 `PetEnabled`、当前皮肤选择、规则参数来源和启动开关加载/保存到已有设置边界；首次启动仍保持桌宠默认开启。图片加载失败只显示可恢复设置提示，不影响提醒循环。

- [ ] **步骤 5：运行全量测试并提交**

```powershell
G:\IkuyoPetDev\dotnet\dotnet.exe test G:\testPet\IkuyoPet.sln --configuration Debug
git -C G:\testPet add src tests
git -C G:\testPet commit -m "feat: show feedback and refresh dashboard state"
```

---

### 任务 6：本机皮肤安装、发布 EXE、README 和最终验收

**文件：**
- 创建（Git 忽略）：`local-skins/user.ikuyo-local/1.0.0/manifest.json`
- 创建（Git 忽略）：`local-skins/user.ikuyo-local/1.0.0/idle.png`
- 创建（Git 忽略）：`local-skins/user.ikuyo-local/1.0.0/remind.png`
- 修改：`README.md`
- 修改：`assets/skins/default/README.md`
- 修改：`.gitignore`
- 修改：`eng/README.md`
- 修改：`eng/verify-dev.ps1`
- 创建：`eng/publish-local.ps1`
- 修改：`.github/workflows/windows-ci.yml`

- [ ] **步骤 1：写失败的发布检查**

增加 PowerShell 验证：发布目录包含 `IkuyoPet.exe` 和 `local-skins/user.ikuyo-local/1.0.0` 时通过；发布目录没有私有皮肤时仍能启动并使用占位图；公开 Git 检查拒绝未带资产许可记录的第三方皮肤。

- [ ] **步骤 2：实现本机私有皮肤安装**

只在当前机器把用户 PNG 复制到 `local-skins` 的 Git 忽略目录，并同时作为 idle/remind；生成 `Personal local use; redistribution permission not established` manifest。发布脚本把该目录复制到输出目录，应用启动时从发布目录导入到 `%LocalAppData%`。没有该目录时不报错。

- [ ] **步骤 3：实现自包含发布脚本**

`eng/publish-local.ps1` 使用 `G:\IkuyoPetDev\dotnet\dotnet.exe publish src/IkuyoPet.App/IkuyoPet.App.csproj --configuration Release --runtime win-x64 --self-contained true`，输出到 `G:\testPet\artifacts\publish\win-x64`，并复制本机私有皮肤（若存在）。不把数据库、日志、测试结果或用户设置复制进去。

- [ ] **步骤 4：更新 README 风险边界**

README 明确：代码 MIT 与角色图片许可分离；本机私有皮肤不自动进入 Git；没有授权不能公开再分发；不接受第三方角色素材相关捐赠或赞助；收到可信权利通知时停止分发并移除素材。不得把免责声明写成授权替代品。

- [ ] **步骤 5：运行最终验证**

```powershell
$env:Path='G:\IkuyoPetDev\dotnet;' + $env:Path
$env:DOTNET_ROOT='G:\IkuyoPetDev\dotnet'
$env:NUGET_PACKAGES='G:\IkuyoPetDev\nuget\packages'
G:\IkuyoPetDev\dotnet\dotnet.exe build G:\testPet\IkuyoPet.sln --configuration Release --no-restore -warnaserror
G:\IkuyoPetDev\dotnet\dotnet.exe test --solution G:\testPet\IkuyoPet.sln --configuration Release --no-build --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File G:\testPet\eng\publish-local.ps1
git -C G:\testPet diff --check
git -C G:\testPet status --short
```

预期：Release 构建 0 警告、0 错误；全量测试全部通过；发布目录包含可启动 `IkuyoPet.exe`；工作树只保留 Git 忽略的本机皮肤和构建产物，不跟踪用户图片。

- [ ] **步骤 6：Commit**

```powershell
git -C G:\testPet add README.md assets/skins/default/README.md .gitignore eng .github
git -C G:\testPet commit -m "chore: publish first complete Ikuyo Pet loop"
```

---

## 执行约束

- 每个任务由全新的实现子代理执行；实现完成后先规格审查，再代码质量审查，发现问题必须由同一实现子代理修复并重新审查。
- 不并行运行会修改同一工作树的实现子代理；任务按 1 → 6 顺序执行。
- 每个行为变更先写失败测试并看到预期失败，再写最小生产代码。
- 不把用户图片、数据库、日志、窗口标题、文档内容、睡眠时间或医疗报告加入 Git。
- 不运行任何删除 Git 历史、清理用户目录或覆盖已有皮肤版本的命令。
- 完成前必须重新运行 Release 构建、全量测试、发布检查和 `git diff --check`，不能只依赖子代理报告。
