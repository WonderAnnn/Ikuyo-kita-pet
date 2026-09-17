# 紧凑气泡合成实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 让真实桌宠气泡按文字量紧凑扩展，同时保持装饰原比例和单一连续白色内腔。

**架构：** Core 只计算紧凑布局；WPF `BubbleChrome` 在一个控件中依次绘内腔、装饰、排列文字；`PetWindow` 把一次布局结果完整传给控件。

**技术栈：** .NET 10、WPF、xUnit

---

### 任务 1：锁定紧凑尺寸

**文件：**
- 修改：`tests/IkuyoPet.Core.Tests/Bubbles/BubbleLayoutCalculatorTests.cs`
- 修改：`src/IkuyoPet.Core/Bubbles/BubbleLayoutCalculator.cs`

- [ ] 编写测试，断言短、中、长文本根尺寸单调增长，方形 PNG 的短文本根节点不是正方形。
- [ ] 运行 `dotnet test --project tests/IkuyoPet.Core.Tests/IkuyoPet.Core.Tests.csproj --filter FullyQualifiedName~BubbleLayoutCalculatorTests`，确认旧实现失败。
- [ ] 删除 PNG 宽高比对根尺寸的约束，按文字测量、主题安全区预留和 padding 计算紧凑宽高。
- [ ] 重跑测试并确认通过。

### 任务 2：锁定单控件合成

**文件：**
- 修改：`tests/IkuyoPet.Infrastructure.Tests/Pet/BubbleChromeContractTests.cs`
- 修改：`src/IkuyoPet.Pet/BubbleChrome.cs`
- 修改：`src/IkuyoPet.Pet/PetWindow.xaml.cs`

- [ ] 编写契约，要求 `InteriorBounds`、`DecorationBounds`，白色无描边只绘内腔，PNG 以保持比例的装饰矩形绘制一次。
- [ ] 运行筛选测试并确认旧实现失败。
- [ ] 在 `OnRender` 中执行 `DrawRoundedRectangle(White, null, InteriorBounds)` 后执行 `DrawImage(Source, DecorationBounds)`；文字继续由 `TextBounds` 排列。
- [ ] 让 `ApplyBubbleLayout` 同步四组布局值，重跑契约测试。

### 任务 3：验证与发布

**文件：**
- 验证：`src/IkuyoPet.App/IkuyoPet.App.csproj`

- [ ] 用 SDK10 跑 Core/WPF 主题测试和 Release `-warnaserror` 构建。
- [ ] 发布到 `artifacts/publish/win-x64-bubble-themes-v7`，检查三张 PNG。
- [ ] 精确停止 v6 进程、更新桌面快捷方式、删除 v6 发布目录、启动 v7；保留 `%LocalAppData%/IkuyoPet`。
