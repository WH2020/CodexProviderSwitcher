# Task 5 report: visible WinForms window, settings, and startup

## 修改

- 新增 `SimpleAppPaths`，只解析 `%AppData%\codex-provider-switcher\settings.json` 与同目录 `startup-error.log`。
- 新增 `SimpleUserSettings`、`WindowBoundsState` 和 `SimpleSettingsStore`；仅保存最后选择的 Provider 与窗口边界，使用 camelCase/缩进 JSON，并通过同目录唯一临时文件后原子覆盖。
- 新增 `SimpleInstanceGuard`，使用 `Local\CodexProviderSwitcher.v1` 命名 Mutex；仅 `createdNew` owner 释放 Mutex，非 owner 不释放，也不访问 Core provider-sync 文件锁。
- 新增紧凑单页 `SimpleMainForm`：显示当前 Provider、SQLite 状态、已配置 Provider 下拉框、切换并同步、刷新、只读结果和复制结果。窗口初始 `560x420`，最小 `520x380`，主按钮沿用既有 GUI 绿色配色。
- 将 Task 1 的空 `Program.Main` 替换为指定 Application/Core composition、默认 `.codex`、单实例检查和启动异常日志。
- 测试项目启用 WinForms，并新增 settings、guard 和 presentation 测试。

## RED / GREEN

- RED 第一次运行：测试项目未启用 WinForms，除 Task 5 类型缺失外还出现 `Control` 框架类型错误；只补充 `<UseWindowsForms>true</UseWindowsForms>` 后重跑。
- 有效 RED：聚焦命令因生产代码缺失 `SimpleMainForm` 得到预期 CS0246，未添加生产实现前没有伪造失败。
- 初次 GREEN：10 项中 9 项通过；最小尺寸测试因未显示父 Form 时 WinForms 子控件 `Visible` 必然为 false 而失败。测试先 `Show()` 后验证真实显示状态。
- 最终 GREEN：聚焦 settings/guard/presentation 测试 10/10 通过。

## 三组测试

使用 `C:\Users\99675\Documents\Codex\2026-08-11\s-j-ho-i\work\.dotnet\dotnet.exe`，并在每次测试前将该目录置于 `PATH` 首位。

```powershell
dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj `
  -c Release `
  --filter "FullyQualifiedName~SimpleSettingsStoreTests|FullyQualifiedName~SimpleInstanceGuardTests|FullyQualifiedName~SimpleMainFormPresentationTests"
```

结果：通过 10/10。

```powershell
dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj -c Release
```

结果：通过 56/56。

```powershell
dotnet test CodexProviderSync.sln -c Release --no-restore
```

结果：成功，命令退出码 0。SimpleApp 56、GuiE2E 36、Application 49、Automation 27、App 66、Core 188 项通过；Core 中 1 项依赖真实 Windows WSL SQLite Home 的安全测试按既有条件跳过。

## 行为与安全核对

- 最小尺寸：presentation 测试实际显示 Form 后设置为 `520x380`，确认 Provider 下拉框、切换并同步、刷新和只读结果均可见、位于客户区内且互不重叠。
- UI 线程：Form 订阅 `SnapshotChanged`；跨线程通知通过 `InvokeRequired` 与 `BeginInvoke` 回到 UI 线程，释放时解除订阅。事件处理器只调用 controller/settings 或 WinForms 平台 API。
- 设置：缺失、损坏或 JSON `null` 均返回默认设置；保存创建父目录，使用 GUID 唯一临时文件并只清理自身临时文件；测试确认不删除同目录的无关临时文件。
- 窗口恢复：仅当保存边界与当前任一 `Screen.WorkingArea` 相交时恢复；关闭时保存选中 Provider 和非最小化边界。
- 单实例：同名第二 guard 不取得 ownership；释放非 owner 不会释放首实例 Mutex。Mutex 不复用或访问 provider-sync 文件锁。
- 启动：只使用 `new CodexHomeService().NormalizeCodexHome(null)` 的默认 `.codex`，未提供或读取 Codex Home 覆盖与 `auth.json`。启动失败只创建 `StartupErrorPath` 的父目录，写入 `startup-error.log` 并在中文错误框显示该路径。
- 范围：窗口没有 auth.json、API key、base_url、账号、恢复/清理备份、检查更新、监控、Codex Home 编辑等控件；Form 不直接调用 Core/Application。未执行 Task 6 集成测试实现或 Task 7 发布工作。

## 遗留项

- 未做 Task 6 的跨模块/启动集成测试与 Task 7 的发布打包；这些明确保留给后续任务。
- 本任务验证了 presentation 布局和全量单元测试，未在多显示器、非 100% DPI 的真实桌面上进行人工视觉巡检。

## 独立复核修复

- 执行中关窗：`FormClosing` 在 controller 为 `Executing` 时设置 `Cancel=true`，状态行提示“操作正在进行，请在操作完成后再关闭。”，不保存设置也不释放窗口。gated 写入测试分别覆盖释放后 `Success` 和 `RecoveryRequired`，完成后再次关窗成功。
- 设置与 Shown 边界：`SimpleSettingsStore.LoadAsync` 将 `UnauthorizedAccessException` 与 `IOException` 一样降级为默认设置，仍让 `OperationCanceledException` 传播；Form 的设置加载失败降级默认并继续 Refresh，Refresh 异常由 controller 的 `Failed` 快照呈现且不逃出 `async void`。
- 启动错误边界：新增 `SimpleStartupErrorReporter`。目录创建/日志写入失败不会替换原始启动异常；对话框明确显示日志路径或“启动错误日志写入失败”，对话框自身失败被最终边界吞掉。测试通过注入 writer/dialog，不弹真实对话框。
- SQLite 状态：`SimpleSwitcherSnapshot` 新增 nullable `SqliteSupported`，controller 从 `StatusSnapshot.SqliteAccess.Supported` 填充并由 record 状态流保留。UI 只按强类型值显示“可用”或“不支持”；Loading 显示“读取中”，首次失败/未知显示“未知”，不再解析 Message 文案。
- 超长 Provider：当前 Provider 改为固定单行 Label，启用 `AutoEllipsis`，完整值放入 ToolTip；状态行为固定高度。80 字符 Provider 在 `520x380` 及 1.25 倍模拟 DPI 缩放后均不越界、不覆盖 SQLite 区域，且 Form 保持 `AutoScaleMode.Dpi`。
- 剪贴板：通过最小 `Action<string>` delegate 封装 `Clipboard.SetText`；`ExternalException` 转为状态行“复制失败，请重试。”，不进入 UI 未处理异常。
- 额外生命周期：排队的 `BeginInvoke` 回调在 Dispose 后检查窗体生命周期，不再访问已释放控件；设置保存的任意异常不阻止窗口关闭。延迟 Shown/Refresh 在释放后也直接返回。

## 复核 RED / GREEN

- 有效 RED：新增复核测试后，编译因缺少 `SimpleStartupErrorReporter`、Form settings/clipboard 测试接缝、Settings 读取 delegate 构造和 `SimpleSwitcherSnapshot.SqliteSupported` 等得到预期 CS0103、CS1729、CS1061。
- GREEN 聚焦命令：settings、guard、presentation、lifecycle、startup reporter 共 28/28 通过。
- GREEN SimpleApp：74/74 通过。
- GREEN 解决方案：`dotnet test CodexProviderSync.sln -c Release --no-restore` 成功，440 项通过；1 项真实 Windows WSL SQLite Home 安全测试按既有条件跳过。

## 最新验证命令

```powershell
dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj `
  -c Release `
  --filter "FullyQualifiedName~SimpleSettingsStoreTests|FullyQualifiedName~SimpleInstanceGuardTests|FullyQualifiedName~SimpleMainFormPresentationTests|FullyQualifiedName~SimpleMainFormLifecycleTests|FullyQualifiedName~SimpleStartupErrorReporterTests"

dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj -c Release

dotnet test CodexProviderSync.sln -c Release --no-restore
```

三次测试均使用工作区本地 `.dotnet`，并将其目录置于 `PATH` 首位。

## 复核后遗留风险

- 依照任务边界，本轮没有实施 Task 6 的真实 Core fault injection。特别是 Core 写入过程中真实 I/O 故障、恢复证据落盘失败和进程级异常退出后的 UI/恢复联动，仍需在 Task 6 集成验证中覆盖。
- 非 100% DPI 已通过 1.25 倍程序化缩放验证稳定布局约束；多显示器混合 DPI 的人工视觉巡检仍未执行。
- 未新增任何 auth.json、API key、base_url、账号、备份恢复/清理、检查更新、监控、自启动或 Codex Home 编辑控件；未加入说明性教程文案，Form 未直接调用 Core/Application。

## 终审 Minor 修复：启动期间保留 Provider 偏好

- Form 记录 `_settingsLoadCompleted` 与 `_loadedLastProvider`。设置尚未完成加载时关闭窗口会跳过保存，避免用未初始化 UI 状态覆盖已有设置。
- 设置已加载但初始 Refresh 仍 pending 时，保存顺序为 combo 当前选择、controller 当前选择、已加载 `LastProvider`；因此会保留 `custom` 而不是写入 `null`。
- Refresh 或用户选择完成后，combo/controller 仍优先于加载值，不改变正常保存语义。
- gated 设置/状态测试的所有等待均有明确超时和 `Assert.True`，异步完成通过 `PumpUntil` 的最终断言确认，没有静默 timeout。

### Minor RED / GREEN

- 有效 RED：生命周期聚焦测试 9 项中新增 2 项失败。Refresh pending 关窗实际保存 `null`（期望 `custom`）；设置加载 pending 关窗实际保存 1 次（期望 0 次）。
- GREEN 生命周期聚焦：`FullyQualifiedName~SimpleMainFormLifecycleTests` 通过 9/9。
- GREEN SimpleApp：76/76。
- GREEN 解决方案：442 项通过；1 项真实 Windows WSL SQLite Home 安全测试按既有条件跳过。

```powershell
dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj `
  -c Release `
  --filter "FullyQualifiedName~SimpleMainFormLifecycleTests"

dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj -c Release

dotnet test CodexProviderSync.sln -c Release --no-restore
```
