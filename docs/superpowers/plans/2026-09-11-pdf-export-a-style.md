# A 版日志 PDF 导出实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 在日志页增加离线 PDF 导出，采用已确认的 A 版活泼主题，导出当前筛选范围、工作统计、前五应用和提醒日志，同时保留非官方元素与隐私边界。

**架构：** 由应用层保存对话框负责选择文件路径；独立的 `AStylePdfLogExporter` 将已查询的日志/统计模型绘制成 WPF 位图，再封装为无外部运行时依赖的 PDF。ViewModel 只负责生成导出快照和触发导出请求，不直接依赖窗口 API。

**技术栈：** .NET 10 WPF、现有 `DashboardSnapshot`/`WorkStatistics` 模型、WPF `DrawingVisual`/`RenderTargetBitmap`、内置 Deflate 压缩 PDF 图像对象、xUnit v3。

---

### 任务 1：定义导出快照和失败测试

**文件：**
- 创建：`src/IkuyoPet.App/Export/PdfLogExportModels.cs`
- 创建：`tests/IkuyoPet.Infrastructure.Tests/App/PdfLogExportTests.cs`

- [ ] **步骤 1：编写失败测试**

测试必须覆盖：A 版导出产生以 `%PDF-1.4` 开头的非空字节流；快照包含北京时间日期范围、统计总时长、最多五个应用和筛选后的日志；导出不写入窗口标题、窗口内容、睡眠或医疗字段。

- [ ] **步骤 2：运行测试确认失败**

运行：`dotnet test tests/IkuyoPet.Infrastructure.Tests/IkuyoPet.Infrastructure.Tests.csproj --filter FullyQualifiedName~PdfLogExportTests`

预期：因 `PdfLogExportSnapshot` 和 `AStylePdfLogExporter` 尚不存在而失败。

- [ ] **步骤 3：写入最小模型**

提供 `PdfLogExportSnapshot`、`PdfLogExportApplication` 和 `PdfLogExportEntry` 不可变记录，字段仅包含日期、时间、类型、渠道、结果、备注、统计范围和应用时长。

- [ ] **步骤 4：运行模型编译测试**

运行同一 `dotnet test` 命令；预期仍因导出器未实现而失败，失败原因必须是导出器缺失而非模型编译错误。

### 任务 2：实现离线 A 版 PDF 导出器

**文件：**
- 创建：`src/IkuyoPet.App/Export/AStylePdfLogExporter.cs`
- 修改：`tests/IkuyoPet.Infrastructure.Tests/App/PdfLogExportTests.cs`

- [ ] **步骤 1：补充失败断言**

增加断言：PDF 至少包含一个页面对象、A 版标题、`非官方元素` 和日志中的中文文本；空日志仍能生成带统计卡片和“暂无日志”的 PDF；取消令牌在绘制前抛出取消异常。

- [ ] **步骤 2：运行测试确认失败**

运行同一测试命令，确认失败来自实现缺失。

- [ ] **步骤 3：实现最小导出器**

使用 WPF 绘制 794x1123 的 A4 预览位图：珊瑚/粉色页眉、拨片和五线谱抽象装饰、统计卡片、前五应用横向柱状图、日志表格和非官方免责声明。通过 `RenderTargetBitmap` 转 RGB，使用 Deflate 压缩嵌入 PDF 图像对象；不引入 Python、数据库或在线服务。

- [ ] **步骤 4：运行导出器测试**

运行测试命令，预期全部通过。

### 任务 3：接入 ViewModel 和日志页面

**文件：**
- 修改：`src/IkuyoPet.App/MainWindowViewModel.cs`
- 修改：`src/IkuyoPet.App/MainWindow.xaml.cs`
- 修改：`src/IkuyoPet.App/Views/LogView.xaml`
- 修改：`tests/IkuyoPet.Infrastructure.Tests/App/LogFilterStyleContractTests.cs`

- [ ] **步骤 1：先添加契约测试**

验证日志页含有“导出 PDF”命令绑定、导出状态文本和 A 版隐私提示；ViewModel 暴露 `ExportPdfCommand`、`PdfExportRequested` 和导出状态。

- [ ] **步骤 2：运行契约测试确认失败**

运行：`dotnet test tests/IkuyoPet.Infrastructure.Tests/IkuyoPet.Infrastructure.Tests.csproj --filter FullyQualifiedName~LogFilterStyleContractTests`

预期：因绑定和属性尚不存在而失败。

- [ ] **步骤 3：实现最小接入**

ViewModel 依据当前日期筛选、类型/结果筛选、统计周期构造快照；主窗口响应请求并打开 `SaveFileDialog`，默认文件名为 `IkuyoPet-日志-yyyyMMdd.pdf`，保存成功/取消/失败都更新状态；日志页在筛选栏右侧放置圆角粉色导出入口，保持现有布局。

- [ ] **步骤 4：运行契约和现有测试**

运行日志契约测试和整个解决方案测试，预期全部通过。

### 任务 4：渲染验证与手工测试文档

**文件：**
- 创建：`docs/manual-tests/2026-09-11-pdf-export-a-style-manual-test.md`

- [ ] **步骤 1：发布并生成 PDF**

运行 `dotnet build IkuyoPet.sln`，启动应用，在日志页点击“导出 PDF”，保存到临时目录。

- [ ] **步骤 2：渲染检查**

使用 `pdfinfo`/`pdftoppm` 检查页数、中文、柱状图、页眉、页脚和隐私声明；确认空日志、中文日志、长备注和取消保存都不崩溃。

- [ ] **步骤 3：记录手工测试**

记录 Windows 10/11、北京时间日期范围、导出文件命名、PDF 打开、A 版非官方标识和“不记录窗口内容/睡眠/医疗信息”提示。

---

## 自检

- 需求覆盖：A 版主题、离线生成、统计/前五应用/日志、隐私边界和日志页入口均有任务。
- 范围控制：不修改提醒调度、桌宠行为、SQLite schema 或已有统计口径。
- 失败路径：保存取消、空日志、取消令牌和异常状态均有测试/手工验证。
