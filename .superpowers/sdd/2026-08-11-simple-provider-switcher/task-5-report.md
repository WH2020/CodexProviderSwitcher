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
