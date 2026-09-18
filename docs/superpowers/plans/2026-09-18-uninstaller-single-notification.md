# 卸载程序与单条通知实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 `superpowers:executing-plans` 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 为便携式 Ikuyo Pet 发布包加入可双击的安全卸载程序，并让桌宠隐藏时 Windows 待处理提醒始终最多一条。

**架构：** 新增独立的 `IkuyoPet.Uninstaller` Windows 可执行项目；卸载选择和路径安全逻辑放在无 UI 的纯 C# 类型中，WPF UI 只负责选择和结果提示。通知路径增加可测试的 `SinglePendingNotificationCoordinator`，由 Windows App Notification sink 用固定 Tag/Group 执行“清理旧提醒→显示最新提醒”，不改变桌宠可见时的气泡路由。

**技术栈：** .NET 10、WPF、Windows App SDK 2.4、xUnit v3、PowerShell 5.1、现有 `artifacts/publish/latest` 发布脚本。

---

## 文件清单

### 新建

- `src/IkuyoPet.Uninstaller/IkuyoPet.Uninstaller.csproj`：Windows 单文件卸载器项目，使用 `assets/branding/icon/icon256.ico`。
- `src/IkuyoPet.Uninstaller/Program.cs`：卸载器入口、选择弹窗、helper 启动。
- `src/IkuyoPet.Uninstaller/UninstallModels.cs`：卸载选择、验证结果、清理计划模型。
- `src/IkuyoPet.Uninstaller/UninstallTargetValidator.cs`：发布目录和数据目录的安全校验。
- `src/IkuyoPet.Uninstaller/UninstallExecutor.cs`：进程检测、快捷方式校验、helper 参数生成和清理执行。
- `src/IkuyoPet.Infrastructure/Windows/SinglePendingNotificationCoordinator.cs`：串行化单条通知替换/清理逻辑。
- `tests/IkuyoPet.Infrastructure.Tests/Windows/SinglePendingNotificationCoordinatorTests.cs`：通知聚合单元测试。
- `tests/IkuyoPet.Infrastructure.Tests/Uninstaller/UninstallTargetValidatorTests.cs`：卸载路径和数据选择测试。

### 修改

- `src/IkuyoPet.Infrastructure/Windows/WindowsNotificationPresenter.cs`：固定通知 Tag/Group、调用协调器、启动时清理、动作完成后清理。
- `src/IkuyoPet.App/App.xaml.cs`：应用启动时初始化通知清理；保留隐藏桌宠的 Windows 通知路由。
- `src/IkuyoPet.App/IkuyoPet.App.csproj`：明确公开图标和卸载器输出的发布契约（主程序已有图标配置，保持不变）。
- `IkuyoPet.sln`：加入卸载器项目。
- `tests/IkuyoPet.Infrastructure.Tests/IkuyoPet.Infrastructure.Tests.csproj`：引用可测试的卸载器项目。
- `scripts/publish-latest.ps1`：同时发布主程序和卸载器，校验两个可执行文件、图标和卸载器版本，记录到 `build-info.json`。
- `eng/verify-dev.ps1`：要求发布目录含 `IkuyoPet.exe`、`IkuyoPet.Uninstaller.exe`、`build-info.json`。
- `README.md`：增加卸载方式和保留/删除数据说明。

---

## 任务 1：先建立通知聚合的失败测试

**文件：**

- 创建：`tests/IkuyoPet.Infrastructure.Tests/Windows/SinglePendingNotificationCoordinatorTests.cs`
- 修改：`tests/IkuyoPet.Infrastructure.Tests/IkuyoPet.Infrastructure.Tests.csproj`（如测试项目尚未自动包含新文件，不增加显式 Compile 项）

- [ ] **步骤 1：编写替换顺序和并发测试**

```csharp
using IkuyoPet.Infrastructure.Windows;

namespace IkuyoPet.Infrastructure.Tests.Windows;

public sealed class SinglePendingNotificationCoordinatorTests
{
    [Fact]
    public async Task ReplaceAsyncClearsBeforeShowingLatestRequest()
    {
        var transport = new RecordingTransport();
        var coordinator = new SinglePendingNotificationCoordinator(transport);
        var first = TestRequest("first");
        var second = TestRequest("second");

        await coordinator.ReplaceAsync(first, CancellationToken.None);
        await coordinator.ReplaceAsync(second, CancellationToken.None);

        Assert.Equal(
            ["clear", "show:first", "clear", "show:second"],
            transport.Events);
        Assert.Equal(second, transport.LastRequest);
    }

    [Fact]
    public async Task ConcurrentReplacementsNeverOverlap()
    {
        var transport = new RecordingTransport(delay: TimeSpan.FromMilliseconds(5));
        var coordinator = new SinglePendingNotificationCoordinator(transport);

        await Task.WhenAll(
            coordinator.ReplaceAsync(TestRequest("a"), CancellationToken.None),
            coordinator.ReplaceAsync(TestRequest("b"), CancellationToken.None),
            coordinator.ReplaceAsync(TestRequest("c"), CancellationToken.None));

        Assert.Equal(0, transport.OverlappingCalls);
        Assert.Equal(6, transport.Events.Count);
        Assert.Matches("^show:[abc]$", transport.Events[^1]);
    }

    [Fact]
    public async Task ClearAsyncIsIdempotent()
    {
        var transport = new RecordingTransport();
        var coordinator = new SinglePendingNotificationCoordinator(transport);

        await coordinator.ClearAsync(CancellationToken.None);
        await coordinator.ClearAsync(CancellationToken.None);

        Assert.Equal(["clear", "clear"], transport.Events);
    }

    private static NotificationRequest TestRequest(string text) => new(
        Guid.NewGuid(), "Ikuyo Pet", text, []);

    private sealed class RecordingTransport(TimeSpan? delay = null)
        : IPendingNotificationTransport
    {
        private int activeCalls;
        private int overlappingCalls;

        public List<string> Events { get; } = [];
        public NotificationRequest? LastRequest { get; private set; }
        public int OverlappingCalls => overlappingCalls;

        public async Task ClearAsync(CancellationToken cancellationToken)
        {
            await EnterAsync(cancellationToken);
            try { Events.Add("clear"); }
            finally { Exit(); }
        }

        public async Task ShowAsync(NotificationRequest request, CancellationToken cancellationToken)
        {
            await EnterAsync(cancellationToken);
            try
            {
                Events.Add($"show:{request.Message}");
                LastRequest = request;
            }
            finally { Exit(); }
        }

        private async Task EnterAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref activeCalls) > 1)
            {
                Interlocked.Increment(ref overlappingCalls);
            }
            if (delay is { } value) await Task.Delay(value, cancellationToken);
        }

        private void Exit() => Interlocked.Decrement(ref activeCalls);
    }
}
```

- [ ] **步骤 2：运行测试确认类型尚不存在**

运行：`dotnet test .\tests\IkuyoPet.Infrastructure.Tests\IkuyoPet.Infrastructure.Tests.csproj --filter FullyQualifiedName~SinglePendingNotificationCoordinatorTests`

预期：FAIL，编译器报告 `SinglePendingNotificationCoordinator` 和 `IPendingNotificationTransport` 尚未定义。

## 任务 2：实现通知协调器并接入 Windows sink

**文件：**

- 创建：`src/IkuyoPet.Infrastructure/Windows/SinglePendingNotificationCoordinator.cs`
- 修改：`src/IkuyoPet.Infrastructure/Windows/WindowsNotificationPresenter.cs`
- 修改：`src/IkuyoPet.App/App.xaml.cs`

- [ ] **步骤 1：实现最小协调器**

```csharp
public interface IPendingNotificationTransport
{
    Task ClearAsync(CancellationToken cancellationToken);
    Task ShowAsync(NotificationRequest request, CancellationToken cancellationToken);
}

public sealed class SinglePendingNotificationCoordinator(IPendingNotificationTransport transport)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task ReplaceAsync(NotificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await transport.ClearAsync(cancellationToken);
            await transport.ShowAsync(request, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { await transport.ClearAsync(cancellationToken); }
        finally { gate.Release(); }
    }
}
```

- [ ] **步骤 2：让 `WindowsAppNotificationSink` 实现 transport**

将固定值定义为 `public const string ReminderTag = "ikuyo-pet-reminder";`，`ShowAsync` 改为调用协调器。Windows SDK transport 的 `ShowAsync` 设置 `notification.Tag = ReminderTag`、`notification.Group = ReminderTag`，清理方法调用当前 SDK 提供的按 Tag/Group 删除 API；若删除 API 返回异常，记录 `Debug.WriteLine` 后仍允许最新通知显示。

通知注册成功后先执行一次 `ClearAsync`，使重启不会保留旧的待处理提醒。`Dispose` 解除事件并释放协调器的 `SemaphoreSlim`。所有 COM/平台异常继续走现有 fallback，不让通知失败阻止主窗口启动。

- [ ] **步骤 3：运行通知测试确认通过**

运行：`dotnet test .\tests\IkuyoPet.Infrastructure.Tests\IkuyoPet.Infrastructure.Tests.csproj --filter FullyQualifiedName~SinglePendingNotificationCoordinatorTests`

预期：3 个测试 PASS。

- [ ] **步骤 4：提交通知变更**

```powershell
git add src/IkuyoPet.Infrastructure/Windows/SinglePendingNotificationCoordinator.cs src/IkuyoPet.Infrastructure/Windows/WindowsNotificationPresenter.cs src/IkuyoPet.App/App.xaml.cs tests/IkuyoPet.Infrastructure.Tests/Windows/SinglePendingNotificationCoordinatorTests.cs
git commit -m "feat: coalesce hidden pet notifications into one"
```

## 任务 3：先建立卸载器安全逻辑的失败测试

**文件：**

- 创建：`tests/IkuyoPet.Infrastructure.Tests/Uninstaller/UninstallTargetValidatorTests.cs`
- 修改：`tests/IkuyoPet.Infrastructure.Tests/IkuyoPet.Infrastructure.Tests.csproj`，添加 `ProjectReference Include="..\..\src\IkuyoPet.Uninstaller\IkuyoPet.Uninstaller.csproj"`。

- [ ] **步骤 1：编写临时目录测试**

测试必须使用 `Path.Combine(Path.GetTempPath(), "IkuyoPet-UninstallTests", Guid.NewGuid().ToString("N"))`，并在 `try/finally` 中删除自身临时目录。覆盖：

```csharp
[Fact]
public void AcceptsOnlyDirectoryWithPublishedMarkers()
{
    using var fixture = PublishedDirectoryFixture.Create();
    var result = UninstallTargetValidator.Validate(fixture.Root, fixture.DataRoot);

    Assert.True(result.IsValid);
    Assert.Equal(fixture.Root, result.InstallRoot);
}

[Fact]
public void RejectsRepositoryRootAndMissingBuildInfo()
{
    using var fixture = PublishedDirectoryFixture.Create(withBuildInfo: false);

    Assert.False(UninstallTargetValidator.Validate(fixture.Root, fixture.DataRoot).IsValid);
    Assert.False(UninstallTargetValidator.Validate(fixture.Root.Parent!.FullName, fixture.DataRoot).IsValid);
}

[Fact]
public void DeleteDataPlanTargetsOnlyLocalAppDataDirectory()
{
    using var fixture = PublishedDirectoryFixture.Create();
    var result = UninstallTargetValidator.Validate(fixture.Root, fixture.DataRoot);
    var plan = UninstallPlan.Create(result, UninstallDataChoice.Delete);

    Assert.Equal(
        Path.GetFullPath(fixture.DataRoot),
        Path.GetFullPath(plan.DataRootToDelete!));
    Assert.EndsWith(
        Path.Combine("IkuyoPet"),
        plan.DataRootToDelete!,
        StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **步骤 2：运行测试确认卸载器项目不存在**

运行：`dotnet test .\tests\IkuyoPet.Infrastructure.Tests\IkuyoPet.Infrastructure.Tests.csproj --filter FullyQualifiedName~UninstallTargetValidatorTests`

预期：FAIL，项目引用路径和类型尚不存在。

## 任务 4：实现卸载器纯逻辑和 WPF 入口

**文件：**

- 创建：`src/IkuyoPet.Uninstaller/IkuyoPet.Uninstaller.csproj`
- 创建：`src/IkuyoPet.Uninstaller/UninstallModels.cs`
- 创建：`src/IkuyoPet.Uninstaller/UninstallTargetValidator.cs`
- 创建：`src/IkuyoPet.Uninstaller/UninstallExecutor.cs`
- 创建：`src/IkuyoPet.Uninstaller/Program.cs`

- [ ] **步骤 1：创建项目文件**

项目设置固定为：`TargetFramework=net10.0-windows10.0.19041.0`、`OutputType=WinExe`、`UseWPF=true`、`AssemblyName=IkuyoPet.Uninstaller`、`ApplicationIcon=..\..\assets\branding\icon\icon256.ico`。`SelfContained` 不写入项目属性，以便测试项目可引用；发布命令显式传入 `--self-contained true`。

- [ ] **步骤 2：实现可测试的数据模型**

定义 `UninstallDataChoice { Preserve, Delete }`、`UninstallValidationResult`、`UninstallPlan`。`UninstallTargetValidator.Validate` 必须同时验证：安装目录存在、`IkuyoPet.exe` 是文件、`build-info.json` 是文件、数据目录位于当前用户的 `%LOCALAPPDATA%\\IkuyoPet`，且安装目录不是数据目录或仓库根目录。`UninstallPlan.Create` 只在验证成功后生成清理计划。

- [ ] **步骤 3：实现执行器和 helper 自删除流程**

执行器按以下顺序工作：

1. 通过 `Process.GetProcessesByName("IkuyoPet")` 检测主程序；发现运行中进程则返回可重试错误，不调用 `Kill`。
2. 使用 `WScript.Shell` 读取桌面 `Ikuyo Pet.lnk` 的 `TargetPath`，仅在目标规范化后等于当前 `IkuyoPet.exe` 时删除。
3. 将当前卸载器复制到 `%TEMP%\\IkuyoPet-uninstall-<guid>\\IkuyoPet.Uninstaller.exe`，启动 `--cleanup <installRoot> <dataRoot> <choice> <parentPid>`，入口进程退出后 helper 等待父 PID 消失，再删除安装目录；`Delete` 选择才删除数据目录。
4. helper 失败时创建 `%TEMP%\\IkuyoPet-uninstall-error.log`，弹窗显示路径，保留数据目录。

- [ ] **步骤 4：实现选择弹窗**

`Program.Main` 使用 `MessageBox` 提供：

- `MessageBoxButton.YesNoCancel`：Yes 映射“保留数据并卸载”，No 映射“删除数据并卸载”，Cancel 直接退出。
- 卸载前显示安装目录和数据目录，删除数据选项使用明确警告。
- 成功启动 helper 后退出，不显示“已完成”假消息；helper 完成后写入结果日志。

- [ ] **步骤 5：运行卸载器测试**

运行：`dotnet test .\tests\IkuyoPet.Infrastructure.Tests\IkuyoPet.Infrastructure.Tests.csproj --filter FullyQualifiedName~UninstallTargetValidatorTests`

预期：测试 PASS。

- [ ] **步骤 6：提交卸载器逻辑**

```powershell
git add src/IkuyoPet.Uninstaller tests/IkuyoPet.Infrastructure.Tests/Uninstaller tests/IkuyoPet.Infrastructure.Tests/IkuyoPet.Infrastructure.Tests.csproj
git commit -m "feat: add safe portable uninstaller"
```

## 任务 5：把卸载器加入解决方案和发布契约

**文件：**

- 修改：`IkuyoPet.sln`
- 修改：`scripts/publish-latest.ps1`
- 修改：`eng/verify-dev.ps1`
- 修改：`README.md`

- [ ] **步骤 1：加入解决方案并验证构建入口**

使用 `dotnet sln .\IkuyoPet.sln add .\src\IkuyoPet.Uninstaller\IkuyoPet.Uninstaller.csproj`，然后确认解决方案项目列表包含 `IkuyoPet.Uninstaller`。

- [ ] **步骤 2：扩展发布脚本**

在现有主程序发布成功后追加：

```powershell
$uninstallerProject = Join-Path $projectRootPath 'src\IkuyoPet.Uninstaller\IkuyoPet.Uninstaller.csproj'
& $DotnetPath publish $uninstallerProject --configuration Release --runtime win-x64 --self-contained true --no-restore --output $staging
if ($LASTEXITCODE -ne 0) { throw "dotnet publish uninstaller failed with exit code $LASTEXITCODE" }

$uninstallerExe = Join-Path $staging 'IkuyoPet.Uninstaller.exe'
if (-not (Test-Path -LiteralPath $uninstallerExe -PathType Leaf)) { throw 'Published uninstaller is missing.' }
```

同时校验 `branding/icon/icon256.ico` 的发布哈希，并在 `build-info.json` 增加：

```powershell
uninstaller = [ordered]@{
    file = 'IkuyoPet.Uninstaller.exe'
    fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($uninstallerExe).FileVersion
    sha256 = (Get-FileHash $uninstallerExe -Algorithm SHA256).Hash.ToLowerInvariant()
}
```

- [ ] **步骤 3：更新开发验证脚本和 README**

`eng/verify-dev.ps1` 在 `-PublishRoot` 下检查两个 exe、`build-info.json` 和 `branding/icon/icon256.ico`。README 的卸载章节说明：运行 `IkuyoPet.Uninstaller.exe`，选择保留数据会保留设置/日志/数据库/备份，选择删除数据不可恢复，源码和其他发布目录不会被处理。

- [ ] **步骤 4：运行发布契约验证**

运行：`dotnet restore .\IkuyoPet.sln`；随后 `dotnet build .\IkuyoPet.sln --configuration Release --no-restore -warnaserror`。

预期：解决方案构建成功，无警告；未执行发布时 `verify-dev` 对缺失发布目录给出明确失败。

- [ ] **步骤 5：提交发布集成**

```powershell
git add IkuyoPet.sln scripts/publish-latest.ps1 eng/verify-dev.ps1 README.md
git commit -m "build: ship uninstaller in latest package"
```

## 任务 6：全量验证和发布包检查

**文件：**

- 修改：`tests/IkuyoPet.Infrastructure.Tests/Windows/WindowsNotificationPresenterTests.cs`（补充固定 Tag/Group 的静态契约断言）
- 修改：`tests/IkuyoPet.Infrastructure.Tests/App/PrivateAssetContractTests.cs` 或对应发布契约测试（若现有测试文件名不同，沿用现有公开资源契约测试）

- [ ] **步骤 1：运行完整测试**

```powershell
dotnet test .\IkuyoPet.sln --configuration Release --no-build --no-restore
```

预期：全部测试通过；通知测试覆盖替换、并发和清理，卸载测试覆盖三种选择和路径保护。

- [ ] **步骤 2：生成 latest 发布包**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-latest.ps1
```

预期：`artifacts/publish/latest` 生成 `IkuyoPet.exe`、`IkuyoPet.Uninstaller.exe`、`build-info.json`、原创皮肤、气泡、互动文案和 `branding/icon/icon256.ico`；桌面快捷方式仍指向 `latest\\IkuyoPet.exe`。

- [ ] **步骤 3：执行发布目录契约检查**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\eng\verify-dev.ps1 -PublishRoot .\artifacts\publish\latest
git diff --check
git status --short
```

预期：验证脚本成功，`git diff --check` 无输出，工作区只包含本次功能的已提交或待提交文件。

- [ ] **步骤 4：在临时目录做卸载 smoke test**

复制 `artifacts/publish/latest` 到 `%TEMP%\\IkuyoPet-uninstall-smoke\\latest`，在其中创建假的 `IkuyoPet.exe` 标记和 `build-info.json`，运行卸载器并选择“保留数据”；确认发布目录和匹配的快捷方式被删除，模拟数据目录仍存在。再次用独立临时数据目录选择“删除数据”，确认数据目录被删除。

- [ ] **步骤 5：提交全量验证结果**

```powershell
git add tests README.md scripts eng IkuyoPet.sln src tests
git commit -m "test: verify uninstaller and single notification release"
```

## 计划自检

- 设计文档中的卸载三选一、图标、路径防护、helper 自删除、快捷方式安全删除、发布包集成均对应任务 3–6。
- 设计文档中的固定通知 Tag/Group、替换顺序、并发串行化、重启清理、动作清理和桌宠可见回归均对应任务 1–2、6。
- 所有实现步骤给出了目标文件、具体 API/字段或命令；没有依赖 MSI/WiX 或未声明的运行时服务。
- 测试先于实现：任务 1 和任务 3 先写失败测试，再添加生产代码。
