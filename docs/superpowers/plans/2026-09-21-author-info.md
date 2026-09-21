# 作者信息与 README 收口实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [x]`）语法跟踪进度。

**目标：** 在 Ikuyo Pet 设置页底部和 README 中显示“成都信息工程大学 WonderAnn”，并补全 README 的 Windows 通知回退截图表格。

**架构：** 作者信息是静态展示文案，不进入 ViewModel、不写入数据库、不上传数据。设置页沿用现有 `ScrollViewer` 与底部状态/版本文本布局；README 使用仓库已有的 3 张通知图片和现有材料表。

**技术栈：** WPF XAML、Markdown/HTML 表格、xUnit 契约测试、.NET 10 Windows。

---

### 任务 1：先添加作者信息与 README 展示的失败契约测试

**文件：**
- 创建：`tests/IkuyoPet.Infrastructure.Tests/App/AuthorAttributionContractTests.cs`

- [x] **步骤 1：编写失败测试**

添加两个静态契约测试：

```csharp
[Fact]
public void SettingsViewShowsAuthorAttributionAtTheBottom()
{
    var view = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "IkuyoPet.App", "Views", "SettingsView.xaml"));
    Assert.Contains("作者：成都信息工程大学 WonderAnn", view, StringComparison.Ordinal);
}

[Fact]
public void ReadmeShowsAuthorAndAllWindowsNotificationImages()
{
    var root = RepositoryRoot();
    var readme = File.ReadAllText(Path.Combine(root, "README.md"));
    Assert.Contains("作者：成都信息工程大学 WonderAnn", readme, StringComparison.Ordinal);
    foreach (var image in new[] { "win通知喝水.png", "win通知休息.png", "windows通知休息.png" })
    {
        Assert.Contains(image, readme, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "docs", "assets", "kita", image)));
    }
}
```

`RepositoryRoot()` 沿用现有契约测试，从 `AppContext.BaseDirectory` 向上查找包含 `src/IkuyoPet.App` 的目录。

- [x] **步骤 2：运行测试确认红灯**

运行：

```powershell
dotnet test tests/IkuyoPet.Infrastructure.Tests/IkuyoPet.Infrastructure.Tests.csproj --filter FullyQualifiedName~AuthorAttributionContractTests
```

预期：失败，原因是设置页和 README 尚未包含作者文案。

### 任务 2：实现设置页作者信息

**文件：**
- 修改：`src/IkuyoPet.App/Views/SettingsView.xaml`

- [x] **步骤 1：添加最少 XAML**

在现有版本文本之后、`StackPanel` 结束之前加入：

```xml
<TextBlock Margin="0,0,0,20"
           Foreground="#FF9AA6B8"
           FontSize="12"
           Text="作者：成都信息工程大学 WonderAnn"/>
```

不新增绑定、命令或导航项。

- [x] **步骤 2：运行契约测试确认通过**

运行同任务 1 的 `dotnet test` 命令，预期作者信息测试不再因设置页失败。

### 任务 3：更新 README 并修复通知截图表格

**文件：**
- 修改：`README.md`

- [x] **步骤 1：加入作者说明**

在 README 免责声明之前加入“作者与项目”小节，包含：

```markdown
## 作者与项目

作者：成都信息工程大学 WonderAnn。

Ikuyo Pet 是个人离线桌宠与健康提醒项目；喜多郁代相关内容属于非官方同人创作，与原作版权方无关联。
```

- [x] **步骤 2：补全 Windows 通知回退表格**

在现有 `#### Windows 通知回退` 说明下使用 3 行 HTML 表格展示：

```html
<tr>
<td><img src="./docs/assets/kita/win通知喝水.png" alt="Windows 喝水通知" width="420"></td>
<td><img src="./docs/assets/kita/win通知休息.png" alt="Windows 休息通知" width="420"></td>
</tr>
<tr>
<td colspan="2"><img src="./docs/assets/kita/windows通知休息.png" alt="Windows 通知回退样例" width="420"></td>
</tr>
```

保留现有其它图片和材料表，不删除或替换素材。

### 任务 4：完整验证、提交并推送

- [x] **步骤 1：运行契约测试**

运行：

```powershell
dotnet test tests/IkuyoPet.Infrastructure.Tests/IkuyoPet.Infrastructure.Tests.csproj --filter FullyQualifiedName~AuthorAttributionContractTests
```

预期：全部测试通过。

- [x] **步骤 2：运行解决方案构建**

运行：

```powershell
dotnet build IkuyoPet.sln --no-restore
```

预期：退出码为 0。

- [x] **步骤 3：检查文档和工作区**

运行：

```powershell
git diff --check
git status --short
```

同时确认 README 中 3 张通知图片的路径均存在。

- [x] **步骤 4：提交并推送**

```powershell
git add README.md src/IkuyoPet.App/Views/SettingsView.xaml tests/IkuyoPet.Infrastructure.Tests/App/AuthorAttributionContractTests.cs
git commit -m "docs: add author attribution"
git push origin codex/publish-ready
git push origin HEAD:main
```
