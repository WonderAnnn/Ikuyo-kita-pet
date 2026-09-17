# 公开原创素材迁移与发布实现计划

> 公开原创资源进入 `assets/`，个人覆盖保留在 `local-*`，发布产物只包含可分发资源。

**目标：** 让全新克隆、Release 发布和本机运行都能加载完整原创皮肤、气泡、互动文案与图标，同时保留本地私有覆盖能力。

**架构：** 项目资源统一从 `assets/` 进入构建输出；`local-assets/` 与 `local-skins/` 继续作为可选私有覆盖目录，不进入 Git。运行时优先用户数据目录，其次公开发布资源，最后回退占位资源。

**技术栈：** .NET/WPF、MSBuild Content、PowerShell 发布脚本、xUnit/Microsoft Testing Platform。

---

### 任务 1：复制并登记公开原创资源

- [x] 复制互动文案、图标、皮肤、气泡到 `assets/`，不删除原文件。
- [x] 校验 manifest、图片和 JSON 可读性。
- [x] 添加素材授权与非官方同人说明。

### 任务 2：调整构建与运行时路径

- [x] 修改 App/Pet 项目的 Content 映射，使公开资源复制到发布输出。
- [x] 将默认皮肤和互动文案切换到公开资源，保留私有覆盖回退。
- [x] 确保气泡目录、皮肤目录和互动目录在 Release 输出中完整存在。

### 任务 3：更新发布与开发校验

- [x] 发布脚本复制公开 `assets/`，不复制 `local-*`、`tmp`、数据库和日志。
- [x] 校验脚本检查公开资源、发布产物和 Git 忽略规则。
- [x] 将私有资源契约测试改为公开资源契约测试。

### 任务 4：验证与交付

- [x] Release restore/build/test 全部通过。
- [x] 检查 `git status`、`git check-ignore` 和 staged diff。
- [x] 记录全新克隆与本机私有覆盖两种运行路径。

> latest 原子替换的发布演练已完成资源校验，但验证机当时有旧 latest\IkuyoPet.exe 正在运行而被 Windows 锁定；退出桌宠后重新运行 scripts/publish-latest.ps1 即可完成目录替换。
