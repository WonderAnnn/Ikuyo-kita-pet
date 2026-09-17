# 自适应手绘气泡主题实现计划

> 本计划按 `superpowers:subagent-driven-development` 执行：每个任务由独立实现代理完成，并在进入下一个任务前做规格审查和质量审查。

## 任务 1：主题模型、目录、资源与持久化

**范围：** `src/IkuyoPet.Core`、`src/IkuyoPet.Infrastructure`、`assets/bubbles`、相关测试。

- [ ] 添加 `BubbleThemeDefinition`、`BubbleSliceInsets`、`BubbleSafeArea` 和 `BubbleLayoutCalculator`。布局计算覆盖空文本、短文本、中文长文本、操作链接额外宽度和最小/最大尺寸。
- [ ] 先写失败单元测试，再实现最小计算逻辑；测试主题 2 的安全区比主题 1 更靠右且长文本优先增加宽度。
- [ ] 将三个已处理的原创透明 PNG 复制到 `assets/bubbles/{cloud-chibi,cloud-guitar,cloud-smile}/bubble.png`，为每个主题添加 `manifest.json` 和缩略图/说明元数据。
- [ ] 添加 `BubbleThemeCatalog`，只返回三个内置主题，未知或缺失资源回退主题 1。
- [ ] 添加独立 `BubbleThemeSelectionStore`（`bubble-theme.json`），使用安全 ID、临时文件和原子替换；为加载损坏、未知 ID、往返保存添加测试。
- [ ] 增加 `assets/bubbles/README.md` 与 `NOTICE.md`，注明图片由用户提供且授权本仓库跟踪，代码许可证与图片授权分离，不宣称第三方权利。

## 任务 2：WPF 九宫格气泡与桌宠接入

**范围：** `src/IkuyoPet.Pet`、必要的应用启动接线、WPF/契约测试。

- [ ] 先写失败的渲染契约测试/可测试布局测试，锁定无裁切、主题 2 右移安全区、动作链接保留和动态宽高。
- [ ] 实现 `BubbleChrome` 自定义 `FrameworkElement`：四角固定、四边单轴拉伸、中间区域仅绘制白色可扩展片；透明源图上的装饰不被二次缩放。
- [ ] 在 `PetWindow.xaml` 移除固定圆角矩形气泡，保留透明窗口和可拖动桌宠；将 `ReminderText` 放在 `BubbleChrome` 内容安全区上方，链接层级高于装饰。
- [ ] 在 `PetWindow.xaml.cs` 接入主题、文本测量和动态窗口/Canvas 尺寸；提醒、互动、反馈三种状态共用布局，长文本不截断，主题切换实时生效。
- [ ] 保留 `ActionInvoked`、淡入淡出、桌宠拖动和工作区边界限制；气泡隐藏后恢复原有桌宠尺寸。

## 任务 3：设置页三卡片选择、启动加载与版本

**范围：** `src/IkuyoPet.App`、`IkuyoPet.App.csproj`、测试与手工文档。

- [ ] 先写失败契约测试：桌宠面板包含三个主题卡片、默认第一项、选择命令/状态和实时变更事件。
- [ ] 在“桌宠与皮肤”面板增加统一风格的三张缩略图卡片，使用柔和边框/粉色选中态，不出现矩形虚线焦点框；卡片点击即可选择。
- [ ] ViewModel 加载主题目录和选择文件，暴露 `BubbleThemes`、`SelectedBubbleTheme`、`SelectBubbleThemeCommand`、状态文本及变更事件；App 启动时将选择传给 `PetWindow`，点击后立即更新窗口并持久化。
- [ ] 将 `assets/bubbles/**` 复制到构建/发布目录，版本号从 0.1.3 增至 0.1.4；保留现有桌宠皮肤包逻辑。
- [ ] 增加手工测试文档，覆盖三主题、短/中/长文本、主题 2 避让左侧人物、链接点击、重启持久化、损坏资源回退和发布目录图片存在性。

## 任务 4：整体验证

- [ ] 运行新增测试、全量 `dotnet test` 和 `dotnet build IkuyoPet.sln`。
- [ ] 发布 win-x64，检查 `assets/bubbles` 被复制、应用启动无异常、设置页可选择、桌宠文字完整显示。
- [ ] 审查所有变更只包含本次范围；保留用户已有未提交改动，不执行 reset/clean。

