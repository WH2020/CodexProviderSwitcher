# Task 4 report: switch providers and sync sessions

## 修改

- 新增 `SimpleSwitcherController.ExecuteAsync(CancellationToken)`，以 `ISimpleProviderService.ExecuteAsync` 执行应用层 plan/apply 边界内的写入。
- 为同一 Provider 构造 `SyncIntent`；为不同 Provider 构造带 `FollowProviderModelSelection` 的 `SwitchIntent`；两者均使用 `AppConstants.DefaultBackupRetentionCount`。
- 执行前调用 `ICodexProcessProbe.FindRunning()`：存在 Codex 进程时发布 `Blocked`，显示名称/PID 并要求手动关闭，且不调用服务写入。
- 通过 `Interlocked.CompareExchange` 忽略执行中的第二次点击；执行状态与完成状态通过既有 `_snapshotLock` 原子发布，并在 `finally` 清除执行 guard。
- 新增执行控制器测试，覆盖意图路由、进程阻断、并发、部分成功、恢复和 target_busy 映射。

## RED / GREEN

- RED：执行新增测试命令后，因生产代码尚无 `ExecuteAsync` 而在 8 处得到预期 CS1061 编译错误。
- GREEN：`FullyQualifiedName~SimpleSwitcherControllerExecutionTests` 通过 7/7。
- 控制器回归：`FullyQualifiedName~SimpleSwitcherController` 通过 21/21。

## 全量验证

命令：

```powershell
& C:\Users\99675\Documents\Codex\2026-08-11\s-j-ho-i\work\.dotnet\dotnet.exe test CodexProviderSync.sln -c Release --no-restore
```

结果：成功。SimpleApp 37/37、GuiE2E 36/36、Application 49/49、Automation 27/27、App 66/66、Core 188 通过且 1 项 Windows WSL 安全测试跳过。

## 行为核对

- 意图路由：同 Provider 为 `SyncIntent`，不同 Provider 为 `SwitchIntent + FollowProviderModelSelection`；保留默认备份数。
- 进程阻断：先探测再读取状态/计划；只显示手动关闭提示，未调用关闭、终止、等待、重启或监控 API。
- 并发：`CompareExchange` 拒绝第二次点击而不抛异常；`finally` 清理 guard，完成发布沿用计数不变量。
- 结果/恢复映射：跳过 locked/unreadable rollout 为 `Incomplete`；完整结果为 `Success` 且提示“现在可以重新打开 Codex”；恢复证据为 `RecoveryRequired`；`target_busy` 为提示手动关闭的 `Blocked`；其余异常为保留已选 Provider 的 `Failed`。
- 安全边界：控制器仅调用 `GetStatusAsync` 与 `ISimpleProviderService.ExecuteAsync`；未直接写 config、rollout、SQLite、`auth.json`、API key、base_url、账户、消息、标题、时间戳或 `encrypted_content`。

## 遗留项

- 按任务边界，未实现 WinForms、设置、启动或跨模块集成。

## 独立复核修复（后续提交）

- 修复执行 ownership：只有 `TryBeginExecution` 成功取得令牌并增加 `_activeOperations` 后，`finally` 才调用 `CompleteExecution`，避免在 Refresh 进行时被早退的 Execute 误减计数。
- 将 Ready/选择校验、所选 Provider 捕获、活动计数增加和 `Executing` 快照发布合并到同一个 `_snapshotLock` 临界区；执行期间 `RefreshAsync` 与 `SelectProvider` 均拒绝操作。
- 成功完成后按 `SyncResult.TargetProvider` 重建 `Providers` 的 `IsCurrent`，保持当前 Provider 与目标一致。
- `OperationCanceledException` 及生命周期为 `Cancelled` 的 `SimpleApplicationException` 都在完成写前恢复可执行的 Ready 快照，并向调用方传播 `OperationCanceledException`。
- 新增覆盖：进程阻断不产生额外状态读取或写入、仅 unreadable 跳过、不再配置的选择、pending/SQLite 阻断、通用失败、两类取消，以及 Execute 与 Refresh/Select 的并发交互。

## 复核 RED / GREEN

- RED：新增 9 项独立复核测试时，16 项执行测试中有 4 项失败，暴露 Refresh 计数误完成、执行期间可 Refresh/选择、成功 `IsCurrent` 未更新、取消未恢复 Ready 的问题。
- GREEN：修复后 `FullyQualifiedName~SimpleSwitcherControllerExecutionTests` 通过 16/16；`FullyQualifiedName~SimpleSwitcherController` 通过 30/30。

## 最新验证

使用 `C:\Users\99675\Documents\Codex\2026-08-11\s-j-ho-i\work\.dotnet\dotnet.exe`（10.0.302）执行：

```powershell
dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj -c Release --filter FullyQualifiedName~SimpleSwitcherControllerExecutionTests
dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj -c Release --filter FullyQualifiedName~SimpleSwitcherController
dotnet test CodexProviderSync.sln -c Release --no-restore
```

结果均成功：执行测试 16/16；全部控制器测试 30/30；全量为 SimpleApp 46/46、GuiE2E 36/36、Application 49/49、Automation 27/27、App 66/66、Core 188 通过/1 跳过（Windows WSL SQLite 安全测试）。

## 最终安全核对

- 进程存在时在状态刷新和服务写入之前终止；测试确认没有额外 `GetStatusAsync` 或 `ExecuteAsync`。
- 仅从控制器调用状态查询、进程探测和应用服务写入；未直接读写 config、rollout、SQLite、`auth.json`、API key、base_url、账户、消息、标题、时间戳或 `encrypted_content`。
- 本次仅修改 Task 4 的控制器、执行测试与报告，未扩展到 Task 5。
