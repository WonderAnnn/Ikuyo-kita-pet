# Ikuyo Pet 提醒呈现与桌宠实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 实现桌宠优先的提醒呈现路由、Windows 原生通知、透明可拖动桌宠和可校验的本地皮肤包替换。

**架构：** `IkuyoPet.Core` 定义提醒事件、动作和 Presenter 契约，路由器只选择桌宠或通知渠道。`IkuyoPet.Infrastructure` 负责 Windows App SDK 通知适配；`IkuyoPet.Pet` 负责透明 WPF 窗口、语言气泡和皮肤校验/导入。两个渠道使用同一个事件 ID、动作枚举和 SQLite 仓储，不复制重试或日志逻辑。

**技术栈：** C# 14、.NET 10、WPF、Windows App SDK 2.4.0、`System.Text.Json`、Windows `BitmapDecoder`、xUnit v3、Microsoft Testing Platform。

---

## 文件结构与职责

```text
src/IkuyoPet.Core/Presentation/
  ReminderPresentationModels.cs       # ReminderDue、动作选项和 Presenter 契约
  ReminderPresentationRouter.cs       # C/A 渠道选择与桌宠故障降级
src/IkuyoPet.Infrastructure/Windows/
  WindowsNotificationPresenter.cs     # Windows App SDK 本地交互通知适配
tests/IkuyoPet.Infrastructure.Tests/Windows/
  WindowsNotificationPresenterTests.cs # 通知请求模型和取消令牌测试
src/IkuyoPet.Pet/
  Skins/SkinManifest.cs                # manifest 强类型模型
  Skins/SkinPackageValidator.cs        # 文件、字段、尺寸、帧率和 alpha 校验
  Skins/SkinPackageImporter.cs         # staging 校验后原子导入和版本目录
  PetWindow.xaml                       # 透明、无任务栏、可拖动的 WPF 窗口
  PetWindow.xaml.cs                    # 拖动、位置约束和动作点击桥接
  PetReminderPresenter.cs              # ReminderDue 到语言气泡的呈现
schemas/skin-manifest.schema.json     # 皮肤包 JSON Schema
tests/IkuyoPet.Core.Tests/Presentation/
  ReminderPresentationRouterTests.cs  # C/A 路由和故障降级测试
tests/IkuyoPet.Infrastructure.Tests/Skins/
  SkinPackageValidatorTests.cs        # 皮肤包拒绝/接受/原子导入测试
```

现有 `ReminderAction`、`ReminderOutcome`、`IEventRepository` 和 SQLite 表结构继续复用。动作显示文案可以变化，但动作 ID 固定为 `Complete`、`Snooze`、`Skip`；医生建议只改变规则配置，不改变这些接口。

## 任务 6：提醒呈现路由与 Windows 通知

### 任务 6.1：用 TDD 定义 Presenter 契约和 C/A 路由

**文件：**
- 创建：`src/IkuyoPet.Core/Presentation/ReminderPresentationModels.cs`
- 创建：`src/IkuyoPet.Core/Presentation/ReminderPresentationRouter.cs`
- 创建：`tests/IkuyoPet.Core.Tests/Presentation/ReminderPresentationRouterTests.cs`

- [x] **步骤 1：编写失败的路由测试**

```csharp
using IkuyoPet.Core.Presentation;
using IkuyoPet.Core.Reminders;
using Xunit;

namespace IkuyoPet.Core.Tests.Presentation;

public sealed class ReminderPresentationRouterTests
{
    [Theory]
    [InlineData(true, "pet")]
    [InlineData(false, "notification")]
    public async Task RoutesToConfiguredPresenter(bool petEnabled, string expected)
    {
        var pet = new RecordingPresenter("pet");
        var notification = new RecordingPresenter("notification");
        var router = new ReminderPresentationRouter(pet, notification);

        await router.ShowAsync(
            new ReminderDue(
                Guid.NewGuid(),
                "activity",
                "陪我起来走两分钟嘛～",
                [
                    new ReminderActionOption(ReminderAction.Complete, "现在出发"),
                    new ReminderActionOption(ReminderAction.Snooze, "让我等你一会儿"),
                    new ReminderActionOption(ReminderAction.Skip, "先放过自己"),
                ]),
            petEnabled,
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, pet.LastChannel ?? notification.LastChannel);
        Assert.Equal(petEnabled, pet.LastDue is not null);
        Assert.Equal(!petEnabled, notification.LastDue is not null);
    }

    private sealed class RecordingPresenter(string channel) : IReminderPresenter
    {
        public string Channel => channel;
        public ReminderDue? LastDue { get; private set; }
        public string? LastChannel { get; private set; }

        public Task ShowAsync(ReminderDue due, CancellationToken cancellationToken)
        {
            LastDue = due;
            LastChannel = Channel;
            return Task.CompletedTask;
        }
    }
}
```

- [x] **步骤 2：运行测试确认因契约缺失而失败**

运行：

```powershell
G:\IkuyoPetDev\dotnet\dotnet.exe run --project G:\testPet\tests\IkuyoPet.Core.Tests\IkuyoPet.Core.Tests.csproj --configuration Debug --no-restore
```

预期：FAIL，编译器报告 `ReminderDue`、`IReminderPresenter` 或 `ReminderPresentationRouter` 未定义。

- [x] **步骤 3：实现最小契约与路由**

在 `ReminderPresentationModels.cs` 中定义：

```csharp
namespace IkuyoPet.Core.Presentation;

using IkuyoPet.Core.Reminders;

public sealed record ReminderActionOption(ReminderAction Action, string Label);

public sealed record ReminderDue(
    Guid EventId,
    string Kind,
    string Message,
    IReadOnlyList<ReminderActionOption> Actions);

public interface IReminderPresenter
{
    string Channel { get; }
    Task ShowAsync(ReminderDue due, CancellationToken cancellationToken);
}
```

在 `ReminderPresentationRouter.cs` 中实现：

```csharp
namespace IkuyoPet.Core.Presentation;

public sealed class ReminderPresentationRouter(
    IReminderPresenter petPresenter,
    IReminderPresenter notificationPresenter)
{
    public async Task ShowAsync(
        ReminderDue due,
        bool petEnabled,
        CancellationToken cancellationToken)
    {
        var selected = petEnabled ? petPresenter : notificationPresenter;
        try
        {
            await selected.ShowAsync(due, cancellationToken);
        }
        catch when (petEnabled)
        {
            await notificationPresenter.ShowAsync(due, cancellationToken);
        }
    }
}
```

- [x] **步骤 4：运行 Core 全量测试确认通过**

运行：

```powershell
$env:Path='G:\IkuyoPetDev\dotnet;' + $env:Path
$env:DOTNET_ROOT='G:\IkuyoPetDev\dotnet'
$env:NUGET_PACKAGES='G:\IkuyoPetDev\nuget\packages'
G:\IkuyoPetDev\dotnet\dotnet.exe run --project G:\testPet\tests\IkuyoPet.Core.Tests\IkuyoPet.Core.Tests.csproj --configuration Debug --no-restore
```

预期：Core 测试全部通过，输出无编译警告和失败。

- [x] **步骤 5：Commit**

```powershell
git -C G:\testPet add src\IkuyoPet.Core\Presentation tests\IkuyoPet.Core.Tests\Presentation
git -C G:\testPet commit -m "feat: route reminders to pet or notification"
```

### 任务 6.2：实现 Windows 原生通知 Presenter

**文件：**
- 创建：`src/IkuyoPet.Infrastructure/Windows/WindowsNotificationPresenter.cs`
- 修改：`src/IkuyoPet.Infrastructure/IkuyoPet.Infrastructure.csproj`（确认 Windows App SDK 引用保持集中管理）

- [x] **步骤 1：先写通知序列化测试夹具**

在 `tests/IkuyoPet.Infrastructure.Tests/Windows/WindowsNotificationPresenterTests.cs` 创建一个记录通知请求的 `INotificationSink` 测试替身，断言一次请求包含原始 `EventId`、`Complete`、`Snooze`、`Skip` 三个动作和相同显示文案。测试只验证 Presenter 发出的请求模型，不启动系统通知服务。

```csharp
var due = new ReminderDue(
    Guid.Parse("11111111-1111-1111-1111-111111111111"),
    "water",
    "喝口水再继续吧～",
    [
        new(ReminderAction.Complete, "现在喝"),
        new(ReminderAction.Snooze, "十分钟后"),
        new(ReminderAction.Skip, "今天先不啦"),
    ]);

await presenter.ShowAsync(due, TestContext.Current.CancellationToken);

Assert.Equal(due.EventId, sink.LastRequest!.EventId);
Assert.Equal([ReminderAction.Complete, ReminderAction.Snooze, ReminderAction.Skip], sink.LastRequest.Actions);
```

- [x] **步骤 2：运行测试确认通知适配器和请求模型缺失而失败**

运行：

```powershell
G:\IkuyoPetDev\dotnet\dotnet.exe run --project G:\testPet\tests\IkuyoPet.Infrastructure.Tests\IkuyoPet.Infrastructure.Tests.csproj --configuration Debug --no-restore
```

预期：FAIL，编译器报告 `WindowsNotificationPresenter` 或通知请求模型未定义。

- [x] **步骤 3：实现请求模型、可测试 Sink 和 Windows App SDK 适配**

在 `WindowsNotificationPresenter.cs` 中保持三层边界：

```csharp
public sealed record NotificationRequest(
    Guid EventId,
    string Title,
    string Message,
    IReadOnlyList<ReminderAction> Actions);

public interface INotificationSink
{
    Task ShowAsync(NotificationRequest request, CancellationToken cancellationToken);
}

public sealed class WindowsNotificationPresenter(INotificationSink sink) : IReminderPresenter
{
    public string Channel => "notification";

    public Task ShowAsync(ReminderDue due, CancellationToken cancellationToken) =>
        sink.ShowAsync(
            new NotificationRequest(
                due.EventId,
                "Ikuyo Pet",
                due.Message,
                due.Actions.Select(option => option.Action).ToArray()),
            cancellationToken);
}
```

另建 Windows App SDK Sink（同文件私有实现或独立内部类）：应用启动时注册 `AppNotificationManager`，用 `AppNotificationBuilder` 添加标题、正文和三个按钮；按钮参数只携带事件 ID 与动作枚举。激活回调解析参数后调用统一的提醒动作处理器，不打开主窗口、不保存窗口内容。无法注册时抛出可诊断异常，让路由器执行 C 渠道降级。

- [x] **步骤 4：运行基础设施测试并验证取消令牌**

运行：

```powershell
G:\IkuyoPetDev\dotnet\dotnet.exe run --project G:\testPet\tests\IkuyoPet.Infrastructure.Tests\IkuyoPet.Infrastructure.Tests.csproj --configuration Debug --no-restore
```

预期：通知请求测试与已有 SQLite、工作跟踪测试全部通过；取消令牌能在 Sink 调用前终止操作。

- [x] **步骤 5：Commit**

```powershell
git -C G:\testPet add src\IkuyoPet.Infrastructure\Windows\WindowsNotificationPresenter.cs tests\IkuyoPet.Infrastructure.Tests\Windows\WindowsNotificationPresenterTests.cs
git -C G:\testPet commit -m "feat: add native Windows reminder notifications"
```

## 任务 7：皮肤校验、原子导入与透明桌宠

### 任务 7.1：用 TDD 实现皮肤包模型和校验器

**文件：**
- 创建：`src/IkuyoPet.Pet/Skins/SkinManifest.cs`
- 创建：`src/IkuyoPet.Pet/Skins/SkinPackageValidator.cs`
- 创建：tests/IkuyoPet.Infrastructure.Tests/Skins/SkinPackageValidatorTests.cs
- 修改：tests/IkuyoPet.Infrastructure.Tests/IkuyoPet.Infrastructure.Tests.csproj（加入 UseWPF=true，让测试夹具可生成真实 PNG）
- 创建：`schemas/skin-manifest.schema.json`

校验边界固定为：`canvasWidth` 和 `canvasHeight` 均为 32–2048 像素，`fps` 为 1–30；必需文件为 `manifest.json`、`idle.png`、`remind.png`；每张 PNG 至少有一个 alpha 小于 255 的像素。

- [x] **步骤 1：编写失败测试**

测试创建临时目录，覆盖四类行为：

```csharp
[Fact]
public void RejectsPackageWhenRequiredImageIsMissing()
{
    var directory = CreatePackageDirectory();
    try
    {
        WriteManifest(directory, canvasWidth: 160, canvasHeight: 160, fps: 12);
        WritePng(Path.Combine(directory, "idle.png"), hasTransparentPixel: true);
        var result = new SkinPackageValidator().Validate(directory);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("remind.png", StringComparison.Ordinal));
    }
    finally { Directory.Delete(directory, recursive: true); }
}

[Fact]
public void RejectsPackageWhenCanvasOrFpsIsOutOfRange()
{
    var directory = CreatePackageDirectory();
    try
    {
        WriteManifest(directory, canvasWidth: 16, canvasHeight: 160, fps: 31);
        WritePng(Path.Combine(directory, "idle.png"), hasTransparentPixel: true);
        WritePng(Path.Combine(directory, "remind.png"), hasTransparentPixel: true);
        var result = new SkinPackageValidator().Validate(directory);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("canvasWidth", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("fps", StringComparison.Ordinal));
    }
    finally { Directory.Delete(directory, recursive: true); }
}

[Fact]
public void RejectsOpaquePngWithoutAlphaChannel()
{
    var directory = CreatePackageDirectory();
    try
    {
        WriteManifest(directory, canvasWidth: 160, canvasHeight: 160, fps: 12);
        WritePng(Path.Combine(directory, "idle.png"), hasTransparentPixel: false);
        WritePng(Path.Combine(directory, "remind.png"), hasTransparentPixel: true);
        var result = new SkinPackageValidator().Validate(directory);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("idle.png", StringComparison.Ordinal));
    }
    finally { Directory.Delete(directory, recursive: true); }
}

[Fact]
public void AcceptsValidTransparentPackageAndReturnsManifest()
{
    var directory = CreatePackageDirectory();
    try
    {
        WriteManifest(directory, canvasWidth: 160, canvasHeight: 160, fps: 12);
        WritePng(Path.Combine(directory, "idle.png"), hasTransparentPixel: true);
        WritePng(Path.Combine(directory, "remind.png"), hasTransparentPixel: true);
        var result = new SkinPackageValidator().Validate(directory);
        Assert.True(result.IsValid);
        Assert.Equal("default", result.Manifest!.Id);
    }
    finally { Directory.Delete(directory, recursive: true); }
}
```

每个测试必须使用真实临时文件和真实 PNG 字节，不用只模拟 File.Exists 的 Mock；测试资源在测试结束时删除。测试类加入 using System.Windows.Media; 和 using System.Windows.Media.Imaging;，并保留以下完整辅助方法：

    private static string CreatePackageDirectory() => Directory.CreateTempSubdirectory("ikuyo-skin-").FullName;

    private static void WriteManifest(string directory, int canvasWidth, int canvasHeight, int fps) =>
        File.WriteAllText(Path.Combine(directory, "manifest.json"), $$"""
            {"id":"default","name":"测试皮肤","version":"1.0.0","author":"test","license":"MIT","canvasWidth":{{canvasWidth}},"canvasHeight":{{canvasHeight}},"fps":{{fps}}}
            """);

    private static void WritePng(string path, bool hasTransparentPixel)
    {
        var alpha = hasTransparentPixel ? (byte)0 : (byte)255;
        var pixels = new[] { (byte)255, (byte)128, (byte)196, alpha };
        var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(stream);
    }

- [x] **步骤 2：运行测试确认校验器缺失而失败**

运行：

```powershell
G:\IkuyoPetDev\dotnet\dotnet.exe run --project G:\testPet\tests\IkuyoPet.Infrastructure.Tests\IkuyoPet.Infrastructure.Tests.csproj --configuration Debug --no-restore
```

预期：FAIL，编译器报告 `SkinManifest` 或 `SkinPackageValidator` 未定义。

- [x] **步骤 3：实现强类型 manifest 与明确错误结果**

```csharp
public sealed record SkinManifest(
    string Id,
    string Name,
    string Version,
    string Author,
    string License,
    int CanvasWidth,
    int CanvasHeight,
    int Fps);

public sealed record SkinValidationResult(
    bool IsValid,
    SkinManifest? Manifest,
    IReadOnlyList<string> Errors);
```

`SkinPackageValidator.Validate(string packageDirectory)` 使用 `JsonSerializer.Deserialize<SkinManifest>`、`BitmapDecoder` 和 `FormatConvertedBitmap` 检查文件、字段、尺寸、帧率及 alpha；错误结果列出具体相对路径或字段，不移动、不删除、不改变当前皮肤。

- [x] **步骤 4：运行皮肤测试确认通过并检查 JSON Schema**

运行：

```powershell
G:\IkuyoPetDev\dotnet\dotnet.exe run --project G:\testPet\tests\IkuyoPet.Infrastructure.Tests\IkuyoPet.Infrastructure.Tests.csproj --configuration Debug --no-restore
```

预期：皮肤测试全部通过；`schemas/skin-manifest.schema.json` 与 C# 字段同名，限制尺寸和帧率范围。

- [x] **步骤 5：Commit**

```powershell
git -C G:\testPet add src\IkuyoPet.Pet\Skins tests\IkuyoPet.Infrastructure.Tests\Skins schemas\skin-manifest.schema.json
git -C G:\testPet commit -m "feat: validate transparent pet skin packages"
```

### 任务 7.2：实现 staging 校验和版本化导入

**文件：**
- 创建：`src/IkuyoPet.Pet/Skins/SkinPackageImporter.cs`
- 修改：`tests/IkuyoPet.Infrastructure.Tests/Skins/SkinPackageValidatorTests.cs`

- [x] **步骤 1：编写原子导入失败测试**
以下测试加入同一个 SkinPackageValidatorTests 类，并复用 7.1 的 CreatePackageDirectory、WriteManifest、WritePng 辅助方法；CreateInvalidPackage 和 CreateValidPackage 定义如下：

    private static string CreateInvalidPackage()
    {
        var directory = CreatePackageDirectory();
        WriteManifest(directory, canvasWidth: 16, canvasHeight: 160, fps: 12);
        WritePng(Path.Combine(directory, "idle.png"), hasTransparentPixel: true);
        return directory;
    }

    private static string CreateValidPackage(string id, string version)
    {
        var directory = CreatePackageDirectory();
        File.WriteAllText(Path.Combine(directory, "manifest.json"), $$"""
            {"id":"{{id}}","name":"测试皮肤","version":"{{version}}","author":"test","license":"MIT","canvasWidth":160,"canvasHeight":160,"fps":12}
            """);
        WritePng(Path.Combine(directory, "idle.png"), hasTransparentPixel: true);
        WritePng(Path.Combine(directory, "remind.png"), hasTransparentPixel: true);
        return directory;
    }

```csharp
[Fact]
public async Task InvalidImportLeavesCurrentSkinAndStagingClean()
{
    var source = CreateInvalidPackage();
    var root = Directory.CreateTempSubdirectory("ikuyo-skins-").FullName;
    try
    {
        var result = await new SkinPackageImporter(new SkinPackageValidator(), root)
            .ImportAsync(source, TestContext.Current.CancellationToken);
        Assert.False(result.IsImported);
        var staging = Path.Combine(root, ".staging");
        Assert.True(!Directory.Exists(staging) || !Directory.EnumerateDirectories(staging).Any());
    }
    finally
    {
        Directory.Delete(source, recursive: true);
        Directory.Delete(root, recursive: true);
    }
}

[Fact]
public async Task ValidImportMovesStagedPackageToVersionedDirectory()
{
    var source = CreateValidPackage("default", "1.0.0");
    var root = Directory.CreateTempSubdirectory("ikuyo-skins-").FullName;
    try
    {
        var result = await new SkinPackageImporter(new SkinPackageValidator(), root)
            .ImportAsync(source, TestContext.Current.CancellationToken);
        Assert.True(result.IsImported);
        Assert.True(File.Exists(Path.Combine(root, "default", "1.0.0", "manifest.json")));
    }
    finally
    {
        Directory.Delete(source, recursive: true);
        Directory.Delete(root, recursive: true);
    }
}

[Fact]
public async Task ImportWithExistingIdAndVersionDoesNotOverwriteExistingFiles()
{
    var first = CreateValidPackage("default", "1.0.0");
    var second = CreateValidPackage("default", "1.0.0");
    var root = Directory.CreateTempSubdirectory("ikuyo-skins-").FullName;
    try
    {
        var importer = new SkinPackageImporter(new SkinPackageValidator(), root);
        var firstResult = await importer.ImportAsync(first, TestContext.Current.CancellationToken);
        var secondResult = await importer.ImportAsync(second, TestContext.Current.CancellationToken);
        Assert.True(firstResult.IsImported);
        Assert.True(secondResult.IsImported);
        Assert.NotEqual(firstResult.InstalledDirectory, secondResult.InstalledDirectory);
        Assert.Equal(File.ReadAllBytes(Path.Combine(first, "idle.png")), File.ReadAllBytes(Path.Combine(root, firstResult.InstalledDirectory!, "idle.png")));
    }
    finally
    {
        Directory.Delete(first, recursive: true);
        Directory.Delete(second, recursive: true);
        Directory.Delete(root, recursive: true);
    }
}
```

- [x] **步骤 2：运行测试确认导入器缺失而失败**

运行同一 Infrastructure 测试命令，预期编译器报告 `SkinPackageImporter` 未定义或导入结果缺失。

- [x] **步骤 3：实现 staging、校验、移动和结果对象**

```csharp
public sealed record SkinImportResult(
    bool IsImported,
    string? InstalledDirectory,
    SkinValidationResult Validation);

public sealed class SkinPackageImporter(
    SkinPackageValidator validator,
    string skinsRoot)
{
    public Task<SkinImportResult> ImportAsync(
        string sourceDirectory,
        CancellationToken cancellationToken);
}
```

实现顺序固定为：创建 `skinsRoot/.staging/<guid>` → 复制源目录文件 → 在 staging 目录完成完整校验 → 选择 `<id>/<version>`，冲突时追加短 GUID → `Directory.Move` 到最终目录 → 返回安装路径。任何异常都删除本次 staging 目录并保留已有目录。

- [x] **步骤 4：运行所有皮肤测试确认通过**

预期：有效包可导入，无效包不改变已有皮肤，同 ID/版本不会覆盖原文件。

- [x] **步骤 5：Commit**

```powershell
git -C G:\testPet add src\IkuyoPet.Pet\Skins\SkinPackageImporter.cs tests\IkuyoPet.Infrastructure.Tests\Skins\SkinPackageValidatorTests.cs
git -C G:\testPet commit -m "feat: import pet skins atomically"
```

### 任务 7.3：实现透明可拖动 PetWindow 和语言气泡 Presenter

**文件：**
- 创建：`src/IkuyoPet.Pet/PetWindow.xaml`
- 创建：`src/IkuyoPet.Pet/PetWindow.xaml.cs`
- 创建：`src/IkuyoPet.Pet/PetReminderPresenter.cs`
- 修改：`src/IkuyoPet.Pet/IkuyoPet.Pet.csproj`（保持 `UseWPF=true`）

- [x] **步骤 1：编写 Presenter 的最小行为测试**

使用注入的 IPetWindowHost 测试替身，断言 ShowAsync 传入完整 ReminderDue，关闭动作调用隐藏而不调用主窗口。测试不启动 WPF Dispatcher。生产代码先定义：

    public interface IPetWindowHost
    {
        bool IsVisible { get; }
        bool OpenedMainWindow { get; }
        Task ShowAsync(ReminderDue due, CancellationToken cancellationToken);
        void Hide();
    }

    public sealed class PetReminderPresenter(IPetWindowHost host) : IReminderPresenter
    {
        public string Channel => "pet";

        public Task ShowAsync(ReminderDue due, CancellationToken cancellationToken) =>
            host.ShowAsync(due, cancellationToken);
    }

测试文件提供可运行的替身和测试：

    private sealed class RecordingPetHost : IPetWindowHost
    {
        public bool IsVisible { get; private set; }
        public bool OpenedMainWindow => false;
        public ReminderDue? LastDue { get; private set; }

        public Task ShowAsync(ReminderDue due, CancellationToken cancellationToken)
        {
            LastDue = due;
            IsVisible = true;
            return Task.CompletedTask;
        }

        public void Hide() => IsVisible = false;
    }

    [Fact]
    public async Task ShowsDueInPetHostWithoutOpeningMainWindow()
    {
        var host = new RecordingPetHost();
        var presenter = new PetReminderPresenter(host);

        await presenter.ShowAsync(
            new ReminderDue(Guid.NewGuid(), "water", "喝口水再继续吧～", []),
            TestContext.Current.CancellationToken);

        Assert.True(host.IsVisible);
        Assert.False(host.OpenedMainWindow);
        Assert.Equal("water", host.LastDue!.Kind);
    }

- [x] **步骤 2：运行测试确认 Pet Presenter 缺失而失败**

运行 Core/Infrastructure 全量测试命令，预期编译器报告 `PetReminderPresenter` 或 `IPetWindowHost` 未定义。

- [x] **步骤 3：实现透明窗口和箭头语言气泡**

`PetWindow.xaml` 必须包含：

```xml
<Window WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        ShowInTaskbar="False"
        ShowActivated="False">
  <Canvas>
    <Polygon x:Name="BubbleArrow" Fill="White" Points="0,0 18,0 5,14" />
    <Border x:Name="Bubble" Background="White" CornerRadius="18">
      <TextBlock x:Name="ReminderText" TextWrapping="Wrap" />
    </Border>
    <Image x:Name="PetImage" Width="160" Height="160" />
  </Canvas>
</Window>
```

`PetWindow.xaml.cs` 处理角色可见区域拖动、气泡箭头定位、工作区边界约束和隐藏/显示；透明区域不设置命中测试。动作文字使用无边框的可聚焦 `Hyperlink`/`Run` 交互元素，点击时传回 `ReminderAction`，不打开主窗口。

`PetReminderPresenter` 实现 `IReminderPresenter`，将提醒内容、动作标签和对应颜文字作为同一气泡文本：提醒态不添加颜文字；完成使用 `ദ്ദി˶>𖥦<)✧`，搁置使用 `(,,•́ . •̀,,)`，跳过或重试结束使用 `ʕ.•᷅ࡇ•᷄.ʔ`。每段最多一个颜文字并放在句尾。

- [ ] **步骤 4：完成手工窗口验收（自动验证已在任务 7.4 执行）**

自动运行：

```powershell
$env:Path='G:\IkuyoPetDev\dotnet;' + $env:Path
$env:DOTNET_ROOT='G:\IkuyoPetDev\dotnet'
$env:NUGET_PACKAGES='G:\IkuyoPetDev\nuget\packages'
G:\IkuyoPetDev\dotnet\dotnet.exe build G:\testPet\IkuyoPet.sln --configuration Debug --no-restore
G:\IkuyoPetDev\dotnet\dotnet.exe test --solution G:\testPet\IkuyoPet.sln --configuration Debug --no-build
```

手工验收：普通桌面右下角显示约 160 px 透明桌宠；拖动不抢焦点；箭头指向角色；句内动作可点击；关闭桌宠时路由到通知；锁屏、全屏、演示状态隐藏。

- [x] **步骤 5：Commit**

```powershell
git -C G:\testPet add src\IkuyoPet.Pet tests\IkuyoPet.Infrastructure.Tests
git -C G:\testPet commit -m "feat: add transparent draggable pet presenter"
```

## 任务 7.4：阶段集成验证与计划收口

**文件：**
- 修改：`docs/superpowers/plans/2026-09-07-presentation-and-pet-plan.md`
- 修改：`docs/superpowers/plans/2026-09-06-ikuyo-pet-mvp.md`

- [x] **步骤 1：运行全量构建、测试和空白检查**

```powershell
$env:Path='G:\IkuyoPetDev\dotnet;' + $env:Path
$env:DOTNET_ROOT='G:\IkuyoPetDev\dotnet'
$env:NUGET_PACKAGES='G:\IkuyoPetDev\nuget\packages'
G:\IkuyoPetDev\dotnet\dotnet.exe build G:\testPet\IkuyoPet.sln --configuration Debug --no-restore
G:\IkuyoPetDev\dotnet\dotnet.exe test --solution G:\testPet\IkuyoPet.sln --configuration Debug --no-build
git -C G:\testPet diff --check
git -C G:\testPet status --short
```

预期：构建 0 警告、0 错误；所有 Core/Infrastructure 测试通过；工作树只包含计划复选框变更或为空。

- [x] **步骤 2：核对规格覆盖度**

逐项核对 `2026-09-07-presentation-and-pet-design.md`：C/A 渠道路由、箭头气泡、句内加粗动作、B 语气、颜文字映射、皮肤原子导入、锁屏/全屏隐藏、隐私边界和医生可配置数值均必须能在任务 6–7 中找到对应实现或验收项。

- [x] **步骤 3：Commit 计划进度**

```powershell
git -C G:\testPet add docs\superpowers\plans\2026-09-07-presentation-and-pet-plan.md docs\superpowers\plans\2026-09-06-ikuyo-pet-mvp.md
git -C G:\testPet commit -m "docs: track presentation and pet implementation progress"
```

## 执行约束

- 每个任务都必须先看到对应失败测试，再写生产代码；测试立即通过时先检查测试是否真正覆盖了缺失行为。
- 只使用 `G:\IkuyoPetDev\dotnet\dotnet.exe` 和 `G:\IkuyoPetDev\nuget\packages`，不要求 MySQL 或 SQLite 服务。
- 不把官方喜多郁代图片、原作台词或用户本地素材加入 Git；默认皮肤使用原创或占位资源。
- 不保存窗口标题、文档内容、键盘内容、屏幕截图或睡眠时间；Presenter 只接收 `ReminderDue` 和动作。
- 医生后续建议通过规则配置改变间隔、静默时段和活动类型；已有日志不可变，代码接口保持兼容。
