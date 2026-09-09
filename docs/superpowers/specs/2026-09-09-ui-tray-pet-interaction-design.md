# Ikuyo Pet 日志视觉、品牌图标、托盘生命周期与桌宠点击互动设计

**状态：** 方案 A 已由用户确认；本文是下一阶段实现的唯一设计基线。

**目标：** 在不改变离线提醒、SQLite 日志和有效工作时间统计方向的前提下，优化日志筛选器视觉，统一 EXE、任务栏和托盘图标，使主窗口最小化或关闭后安静驻留托盘，并让用户单击桌宠时随机显示本地 JSON 文案。

## 1. 当前基线与问题

代码基线为提交 `b4853d3`。现有实现已经具备主窗口、透明桌宠、提醒气泡、托盘菜单、SQLite 日志和本地皮肤加载，但四处体验仍不完整：

1. `src/IkuyoPet.App/Views/LogView.xaml` 直接使用默认 `DatePicker` 和 `ComboBox`，因此日历按钮、边框、下拉箭头和选中状态仍是原生 WPF 外观，与主页面卡片风格不一致。
2. `src/IkuyoPet.App/TrayIconHost.cs` 使用 `SystemIcons.Application`；EXE、任务栏和托盘尚未采用用户提供的图标。
3. `src/IkuyoPet.App/App.xaml.cs` 启动时总是显示主窗口；关闭按钮已被托盘宿主拦截并隐藏窗口，但最小化仍保留任务栏按钮。
4. `src/IkuyoPet.Pet/PetWindow.xaml.cs` 在角色命中区的鼠标按下事件中立即调用 `DragMove()`，尚未区分单击和拖动，也没有普通点击互动事件。

用户提供的图标目录包含 `icon.png` 和七个 `.ico`。实测 `icon.png` 为 256×256；七个 ICO 的 SHA-256 完全一致，每个 ICO 都内含 256、64、48、40、32、24、16 像素七个 32 位帧。因此它们不是七个独立单尺寸资源，运行时只需使用其中一个多尺寸 ICO。

互动 JSON 已通过 UTF-8 JSON 解析：`character=喜多郁代`、`event=click`、`language=zh-CN`，包含 105 条消息、20 种 mood、无重复 ID、无重复文案，最长文案 48 个字符。`canon_sources`、`description` 和 `mood` 在本轮只作为元数据保留，不参与执行。

## 2. 范围

### 2.1 本轮包含

- 为日志日期和两个筛选下拉框提供统一的现代圆角样式。
- 将用户图标目录安全复制到项目的本机私有资源区，并在本机发布中统一用于 EXE、主窗口任务栏按钮和托盘。
- 程序启动时仍显示主页面；最小化和关闭主页面时隐藏到托盘；单击托盘恢复主页面。
- 区分桌宠单击和拖动；有效单击从 JSON 文案中随机选择一条并显示在现有箭头语言气泡中。
- 为资源缺失、JSON 损坏、空消息集合和重复点击提供稳定回退。
- 添加自动化测试、发布检查和手工视觉验收项。

### 2.2 本轮不包含

- 不改变提醒间隔、活动时长、饮水量或任何医疗数值。
- 不把桌宠点击互动写入健康日志或工作统计。
- 不让 `mood` 驱动表情图片、动画或语音；这些能力可由未来皮肤/动画阶段单独设计。
- 不引入第三方 WPF UI 框架。
- 不将用户提供的商业动漫角色图标或角色文案提交到公开 Git 仓库。
- 不实现应用内 AI 生成工作室。

## 3. 选择的总体方案

采用原生 WPF 增量优化：保留当前项目分层和 H.NotifyIcon.Wpf，只增加小型样式资源、点击互动模型/加载器、气泡显示状态和主窗口托盘行为。代码在缺少私有图标与角色 JSON 的干净仓库中仍可构建和运行；本机存在私有资源时，发布脚本将其纳入本地发布目录。

资源和行为的边界为：

```text
local-assets（本机私有、Git 忽略）
    ├─ branding/icon/                 用户提供的完整 icon 目录副本
    └─ interactions/
       └─ ikuyo-click.zh-CN.json      用户提供的点击互动 JSON 副本

schemas（可公开）
    └─ pet-interaction.schema.json    互动数据格式

assets（可公开）
    └─ interactions/default/
       └─ click.zh-CN.json            不含第三方角色设定的通用回退文案
```

迁移采用“复制、校验、保留源文件”流程：先复制桌面文件到 `local-assets`，对文件数量、长度和 SHA-256 进行核验；不自动删除桌面原件。`local-assets/` 整体加入 `.gitignore`，避免未来推送 Git 时误传私有角色素材。

## 4. 日志筛选器视觉设计

日志顶部保留“日期、类型、结果”三个筛选项，不改变绑定字段和筛选语义。三个控件放入同一个浅色筛选栏，形成一组而不是三个彼此无关的原生方框。

视觉参数：

- 控件高度 40 DIP，圆角 10 DIP，背景 `#FFFFFFFF`，默认描边 `#FFDDE6F0`。
- 悬停描边 `#FFB8CADF`；键盘焦点描边 `#FFC25692`，2 DIP 焦点环不得仅依赖颜色变化。
- DatePicker 宽 148 DIP，类型 ComboBox 宽 116 DIP，结果 ComboBox 宽 132 DIP，控件间距 12 DIP。
- 日历图标和下拉箭头使用 WPF `Path` 矢量，不使用原生按钮外观；图标位于右侧 32 DIP 命中区。
- 下拉弹层圆角 10 DIP，外边距 4 DIP，阴影透明度不超过 0.14；选中项采用浅粉背景 `#FFFFEDF5` 和文字 `#FF9B5473`。
- 保留 Tab、Enter、Space、方向键和 Escape 操作；不得为了圆角模板破坏键盘导航。
- 窗口宽度不足时筛选栏允许换行，不能裁掉日期或结果文字。

样式集中在 `src/IkuyoPet.App/Themes/LogFilterStyles.xaml`，通过 `App.xaml` 合并。`LogView.xaml` 只引用语义化资源键，避免将大量模板继续堆进页面文件。

## 5. 应用图标设计

本机资源完整保存在 `local-assets/branding/icon/`，但只选择 `icon256.ico` 作为规范多尺寸应用图标；其余同哈希文件保留用于来源核验，不参与编译。

构建与显示规则：

1. `IkuyoPet.App.csproj` 在私有 ICO 存在时通过条件属性设置 `ApplicationIcon`，把多尺寸图标嵌入 EXE；缺失时项目继续使用当前通用图标并正常构建。
2. 主窗口不维护第二份位图，直接使用应用程序的嵌入图标，使 EXE 和任务栏保持一致。
3. `TrayIconHost` 从当前 EXE 提取关联图标；提取失败时回退 `SystemIcons.Application`。托盘宿主持有并在退出时释放 `Icon` 对象，避免 GDI 句柄泄漏。
4. `eng/publish-local.ps1` 识别本机私有图标，发布完成后校验 EXE 存在并记录是否使用了私有品牌资源，但不把私有源目录加入 Git。

手工验收覆盖 Windows 显示缩放 100%、150%、200%，观察资源管理器 EXE、主窗口任务栏和通知区域托盘图标，不能出现明显拉伸、黑底或错误透明边缘。

## 6. 主窗口到托盘的生命周期

启动行为已经确认：每次正常启动仍直接显示主页面。

窗口状态规则：

- 用户点击最小化：拦截 `StateChanged`，隐藏主窗口并把 `ShowInTaskbar` 设为 `false`；桌宠是否显示继续由 `PetEnabled` 独立决定。
- 用户点击关闭：继续取消普通关闭并执行同样的隐藏逻辑，不退出后台提醒、工作统计或托盘。
- 用户单击或双击托盘图标，以及托盘菜单“打开今日”：设置 `ShowInTaskbar=true`、`WindowState=Normal`、`Show()`、`Activate()`，恢复主页面。
- 用户选择托盘菜单“退出 Ikuyo Pet”：允许主窗口真正关闭、取消后台循环、关闭桌宠并释放托盘资源。
- 应用显式采用 `ShutdownMode=OnExplicitShutdown`，确保主窗口和桌宠都隐藏时后台托盘仍存活。

隐藏和恢复逻辑统一放入 `TrayIconHost` 的私有方法，最小化、关闭、左键和菜单入口不得复制四份状态切换代码。

## 7. 桌宠点击与拖动判定

当前 `MouseLeftButtonDown -> DragMove()` 改为明确的手势状态：

1. 鼠标左键按下时记录起始点、起始时间并捕获鼠标。
2. 鼠标移动距离超过 `SystemParameters.MinimumHorizontalDragDistance` 或 `MinimumVerticalDragDistance` 时进入拖动态，仅执行窗口移动。
3. 鼠标释放时，如果从未进入拖动态，则判定为单击并触发 `InteractionRequested`；拖动结束不得附带触发气泡。
4. 右键、双击的第二次按下、气泡中的超链接操作均不得被误判为普通点击互动。
5. 拖动完成后继续调用现有工作区边界约束，防止桌宠移出屏幕。

点击事件只表示“用户请求普通互动”，不携带提醒 EventId，也不进入 `ReminderActionCoordinator`。

## 8. 互动 JSON 与随机选择

运行时只读取以下字段：

```json
{
  "character": "喜多郁代",
  "event": "click",
  "language": "zh-CN",
  "messages": [
    { "id": 1, "text": "纯文本内容", "mood": "元气" }
  ]
}
```

加载规则：

- 文件必须是合法 UTF-8 JSON，`event` 必须等于 `click`，`language` 必须等于 `zh-CN`。
- `messages` 至少包含一条有效记录；`id` 必须为正整数且唯一；`text` 去除首尾空白后长度为 1–160 个字符；`mood` 可为空。
- 文案始终作为普通 TextBlock/Run 纯文本显示，不解析 HTML、Markdown、命令、链接或模板表达式。
- `description` 和 `canon_sources` 不参与运行时行为，也不会触发网络访问。
- 无效条目逐条丢弃；文件不存在、整体无法解析或最终没有有效条目时，使用公开的通用回退文案，并写入本机调试/诊断日志，不能阻止应用启动。

随机选择使用“排除上一次 ID 后均匀随机”的策略：消息多于一条时不连续显示同一条；只有一条时允许重复。随机选择器与 JSON 加载器独立，测试可注入确定性随机源。

## 9. 气泡状态与优先级

桌宠窗口维护四种呈现状态：

```text
Reminder（带完成/搁置/跳过）
    > Feedback（用户处理结果反馈）
    > Interaction（普通点击文案）
    > Idle（无气泡）
```

行为细则：

- 有待处理 Reminder 时，点击桌宠不得替换提醒文本或动作链接。
- Feedback 显示期间不插入普通互动，以免结果反馈被截断。
- Idle 或 Interaction 状态下单击桌宠，从选择器取得一条文案并显示在现有箭头气泡中。
- 普通互动气泡显示 4 秒后自动收起并回到 Idle；连续单击会立即换一条非重复文案并重新计算 4 秒，不排队创建多个延迟任务。
- 新提醒到达时立即取消互动气泡计时并切换为 Reminder。
- 隐藏/关闭桌宠时取消互动计时；再次显示桌宠时从 Idle 开始。
- 普通互动不切换提醒皮肤、不创建提醒事件、不写 SQLite、不刷新健康日志。

为避免异步延迟竞争，`PetWindow` 只保留一个互动气泡 `CancellationTokenSource`。新状态出现时先取消并释放旧令牌，再更新 UI。

## 10. 组件边界与预期文件

实现时按以下职责拆分：

- `src/IkuyoPet.App/Themes/LogFilterStyles.xaml`：日志筛选控件模板和视觉状态。
- `src/IkuyoPet.App/Views/LogView.xaml`：应用筛选栏样式，不承载解析或托盘逻辑。
- `src/IkuyoPet.App/MainWindow.xaml.cs`：把窗口最小化事件交给托盘宿主。
- `src/IkuyoPet.App/TrayIconHost.cs`：统一隐藏、恢复、退出和 EXE 图标提取。
- `src/IkuyoPet.App/App.xaml.cs`：加载互动目录、组合选择器和桌宠事件，不直接解析每条消息。
- `src/IkuyoPet.Pet/Interactions/PetInteractionModels.cs`：互动目录与消息模型。
- `src/IkuyoPet.Pet/Interactions/PetInteractionCatalogLoader.cs`：UTF-8 JSON 加载、验证与回退。
- `src/IkuyoPet.Pet/Interactions/PetInteractionSelector.cs`：均匀随机与禁止连续重复。
- `src/IkuyoPet.Pet/PetWindow.xaml.cs`：手势判定、状态优先级和互动气泡生命周期。
- `schemas/pet-interaction.schema.json`：公开数据格式。
- `assets/interactions/default/click.zh-CN.json`：公开且不依赖第三方角色的通用回退文案。
- `.gitignore`、`README.md`、`eng/publish-local.ps1`：私有资源边界、发布说明和校验。

现有 `ReminderActionCoordinator`、SQLite 表结构、提醒调度器和有效工作统计不因本轮优化而改变。

## 11. 错误处理与隐私

- 私有图标缺失：使用通用应用图标，编译和运行继续进行。
- EXE 图标提取失败：托盘使用 `SystemIcons.Application`。
- 互动 JSON 不合法：使用通用回退文案，不弹阻塞式错误框。
- 普通互动异步任务被新状态取消：视为正常状态切换，不记录错误。
- 桌宠点击内容不进入 SQLite、CSV 或工作时间记录。
- JSON 加载不发起网络请求；`canon_sources` URL 仅是数据文本。
- 公开 README 继续声明：MIT 只覆盖代码和明确可再分发资源；“非商业”或“侵权删除”不等于取得第三方素材传播许可。

## 12. 测试与验收

自动化测试至少覆盖：

1. 公开测试夹具为合法 JSON 时能完整加载；重复 ID、空文本、超长文本被拒绝或过滤。本机私有 JSON 存在时，资源校验脚本另行确认其 105 条消息全部可用，干净仓库测试不得依赖该私有文件。
2. JSON 缺失、损坏或没有有效消息时使用通用回退。
3. 固定随机源下选择结果可预测；多条消息不连续重复。
4. 单击触发一次 `InteractionRequested`；超过系统拖动阈值只移动窗口，不触发互动。
5. Reminder 和 Feedback 状态拒绝普通互动覆盖；新 Reminder 能打断 Interaction。
6. 连续点击只保留一个计时器，最后一条文案在 4 秒后收起。
7. 主窗口最小化与关闭后不可见且不显示任务栏按钮；托盘恢复后重新可见并处于 Normal。
8. 私有 ICO 缺失时 Release 构建仍通过；存在时自包含发布包含已嵌入的多尺寸应用图标。

发布验收命令继续使用：

```powershell
G:\IkuyoPetDev\dotnet\dotnet.exe build G:\testPet\IkuyoPet.sln --configuration Release --no-restore -warnaserror
G:\IkuyoPetDev\dotnet\dotnet.exe test --solution G:\testPet\IkuyoPet.sln --configuration Release --no-restore --verbosity minimal
powershell -NoProfile -ExecutionPolicy Bypass -File G:\testPet\eng\publish-local.ps1 -SkipRestore
```

手工验收路径：启动应用看到主页面；最小化后任务栏按钮消失但托盘和桌宠保持；单击托盘恢复；单击桌宠出现一条 JSON 文案；拖动桌宠不出现文案；提醒到达后普通点击不能覆盖动作选项；托盘退出后所有 IkuyoPet 进程结束。

## 13. 实施顺序

下一步实现计划应按以下顺序拆分并采用测试驱动：

1. 私有资源目录、图标条件构建与发布校验。
2. 主窗口最小化/关闭到托盘和恢复行为。
3. 日志筛选器样式与键盘操作验收。
4. 互动 JSON 模型、加载器、Schema 与随机选择器。
5. 桌宠单击/拖动判定、气泡状态优先级和 App 组合。
6. 全量测试、不同 DPI 图标检查、发布版真实启动验收与 README 更新。

该顺序先保证应用生命周期和资源稳定，再接入互动行为；任何步骤失败都不会改变提醒调度、SQLite 数据或医疗相关配置。