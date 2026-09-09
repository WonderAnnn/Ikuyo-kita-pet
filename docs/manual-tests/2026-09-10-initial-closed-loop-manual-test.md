# Ikuyo Pet 初步闭环手工测试

**适用分支：** `feat/test-development`  
**工作区：** `G:\testPet`  
**目标：** 验证主页面、托盘、日志筛选器、透明桌宠、点击 JSON 气泡、提醒优先级和隐私边界。本文不调整 40–50 分钟活动提醒、5 分钟活动建议、SQLite 表结构或有效工作统计规则。

## 1. 测试前准备

1. 在任务管理器中确认没有旧的 `IkuyoPet.exe`。若存在，先通过旧程序托盘菜单正常退出，避免把多实例误认为本次故障。
2. 使用项目固定 SDK 构建并发布：

```powershell
G:\IkuyoPetDev\dotnet\dotnet.exe build G:\testPet\IkuyoPet.sln --configuration Release --no-restore -warnaserror
G:\IkuyoPetDev\dotnet\dotnet.exe test --solution G:\testPet\IkuyoPet.sln --configuration Release --no-restore --verbosity minimal
powershell -NoProfile -ExecutionPolicy Bypass -File G:\testPet\eng\publish-local.ps1 -SkipRestore
```

3. 默认发布 EXE 为 `G:\testPet\artifacts\publish\win-x64\IkuyoPet.exe`。
4. 需要验证本机角色文案时，确认源文件位于 `G:\testPet\local-assets\interactions\ikuyo-click.zh-CN.json`；公开仓库测试不得依赖它。

## 2. 为什么“亮屏很久”仍可能没有提醒气泡

亮屏时间不等于有效工作时间。40–50 分钟随机阈值只累计同时满足以下条件的采样：

- 当前前台进程位于设置页启用的白名单，例如 PyCharm 通常为 `pycharm64`；
- 电脑未锁屏，且不处于全屏或演示抑制状态；
- 最近 5 分钟内存在系统级键盘或鼠标操作；
- 白名单应用持续位于前台；切到浏览器、桌面或后台运行的 PyCharm 不累计。

所以只让屏幕亮着、把 PyCharm 放在后台、离开电脑超过 5 分钟，均不会推进下一次活动提醒。首次启动也不会立即弹提醒，而是开始一个 40–50 分钟的有效工作周期。普通“点击桌宠文案”不依赖这个周期，应在单击桌宠后立即出现约 4 秒。

如需快速验证调度闭环，运行自动化的 `FirstClosedLoopTests`，不要直接修改正式数据库或生产默认参数：

```powershell
G:\IkuyoPetDev\dotnet\dotnet.exe test --project G:\testPet\tests\IkuyoPet.Infrastructure.Tests\IkuyoPet.Infrastructure.Tests.csproj --configuration Release --no-restore --filter-class IkuyoPet.Infrastructure.Tests.EndToEnd.FirstClosedLoopTests --verbosity minimal
```

真实手工提醒测试应保持 PyCharm 前台并持续正常操作，等待本轮实际抽取的 40–50 分钟有效工作时间。

## 3. 启动、单实例观察与托盘

| 编号 | 操作 | 预期结果 | 结果/证据 |
|---|---|---|---|
| T-01 | 双击发布版 EXE | 主页面直接显示；桌宠按设置显示 | 待测 |
| T-02 | 查看任务管理器 | 本次只新增一个 `IkuyoPet.exe` | 待测 |
| T-03 | 点击主窗口最小化 | 主窗口与任务栏按钮消失；托盘和已启用桌宠仍在 | 待测 |
| T-04 | 单击托盘图标 | 主窗口恢复、回到 Normal 并置前 | 待测 |
| T-05 | 点击主窗口关闭按钮 | 与最小化相同，只隐藏到托盘，不结束进程 | 待测 |
| T-06 | 托盘菜单选择“退出 Ikuyo Pet” | 主窗口、桌宠、后台循环和进程全部结束 | 待测 |

## 4. 日志筛选器视觉与键盘

1. 打开“日志”，观察日期、类型、结果是否位于同一浅色圆角筛选栏，间距一致，无旧式突兀按钮边框。
2. 使用 Tab 依次聚焦三个筛选器；焦点环应清楚可见。
3. 用 Enter/Space 展开日期或下拉框，用方向键选择、Escape 关闭；筛选结果语义不得变化。
4. 缩窄窗口，筛选器应换行而不是裁掉日期或选项文字。
5. 分别在 Windows 100%、150%、200% 缩放下检查文字、矢量图标和下拉弹层无截断。

## 5. 桌宠点击、随机文案与拖动

| 编号 | 操作 | 预期结果 | 结果/证据 |
|---|---|---|---|
| P-01 | 在角色透明命中区单击一次 | 箭头语言气泡立即出现一条 JSON 文案 | 待测 |
| P-02 | 连续单击 10 次并记录相邻文本 | 每次只显示一个气泡；相邻两次不重复同一 ID | 待测 |
| P-03 | 单击后不操作 | 最后一条普通互动约 4 秒后收起 | 待测 |
| P-04 | 按住角色移动超过系统拖动阈值后释放 | 桌宠移动且不弹普通点击文案 | 待测 |
| P-05 | 拖到屏幕四边 | 松开后保持在当前工作区内 | 待测 |
| P-06 | 观察立绘 | 透明背景正常，`Stretch=Uniform`，人物比例未拉伸 | 待测 |

## 6. 提醒与气泡优先级

1. 当普通互动正在显示时触发 Reminder：Reminder 应立即替换普通互动。
2. Reminder 显示“我这就去 / 等我一下 / 这次先记下”等动作时单击桌宠：普通互动不得覆盖 Reminder。
3. 选择一个提醒动作：出现对应 Feedback；Feedback 显示期间普通点击不得覆盖。
4. Feedback 结束后恢复 Idle；之后单击可再次显示普通互动。
5. 点击提醒中的超链接只触发一次动作，不应同时触发普通互动。

## 7. 资源缺失与损坏回退

请在发布目录的副本中操作，保留原始发布目录：

1. 删除副本中的 `interactions\ikuyo-click.json` 后启动：程序应继续启动并使用 `interactions\default\click.json`。
2. 将副本中的私有 JSON 改成非法 JSON 后启动：不弹阻塞错误框，仍使用公开回退。
3. 再将公开回退移走：程序仍应使用代码内置的最小默认文案启动；诊断只进入调试输出。
4. 恢复文件后重新发布，避免把故障样本继续当日常版本使用。

## 8. 图标与 DPI

在 Windows 100%、150%、200% 缩放下分别检查资源管理器中的 EXE、主窗口任务栏按钮和托盘图标：轮廓清晰、无黑底、无错误透明边缘、未被非等比拉伸。若只有托盘模糊，记录 Windows 缩放比例、显示器编号和截图。

## 9. SQLite、CSV 与隐私边界

1. 记录点击桌宠前后的日志条数；连续点击桌宠后，今日日志和 CSV 不应新增互动文案、消息 ID、mood、角色名或点击次数。
2. 数据库 Schema 仍只有现有提醒、白名单应用、工作会话和设置结构；不应新增点击互动表或字段。
3. 页面、CSV 与数据库不应出现睡眠开始、结束、时长或质量字段，也不记录医疗报告。
4. 工作记录只包含白名单进程标识、时间段与累计时长，不包含窗口标题、项目名、文件名、键鼠内容或屏幕截图。

## 10. 提醒不出现的排查顺序

1. 确认桌宠开关开启；关闭时提醒会走 Windows 通知而不是桌宠气泡。
2. 确认提醒未被托盘菜单暂停。
3. 确认 PyCharm 的真实进程名已在设置页启用（通常为 `pycharm64`）。
4. 确认 PyCharm 当前位于前台，而非只在后台运行。
5. 确认电脑未锁屏、未处于全屏/演示状态，最近 5 分钟有输入。
6. 确认已累计到本轮随机抽取的 40–50 分钟有效工作时间；亮屏墙钟时间不能替代它。
7. 普通点击气泡也不出现时，检查是否正处于 Reminder 或 Feedback 高优先级状态，并确认发布目录至少有公开回退 JSON。
8. 启动失败时查看 `%LOCALAPPDATA%\IkuyoPet\startup-error.log`，并记录 EXE 路径、时间和错误全文。

## 11. 验收记录

- 测试日期：
- Windows 版本：
- 显示缩放：
- 发布 EXE：
- 私有 JSON：存在 / 缺失 / 损坏场景均测
- 自动化测试结果：
- 未通过项目与截图：
- 验收人：
