# Task 6 report: real isolated switch/sync integration

## 完成内容

- SimpleApp 测试项目通过 linked source 复用 `TestCodexHomeFixture.cs` 与 `TestEnvironment.cs`；未复制或修改 fixture。
- 新增 `SimpleAppComposition.CreateController(codexHome, processProbe)`，生产程序与集成测试共同使用同一 composition。
- `Program` 仍通过 `CodexHomeService.NormalizeCodexHome(null)` 使用默认 `.codex`，仍使用真实 `CodexProcessProbe`；仅移除原有内联 composition，没有修改 controller、Application plan/apply 或 Core 事务语义。
- 新增两个真实隔离集成测试，均经过 `SimpleAppComposition -> SimpleSwitcherController -> SimpleProviderService -> ApplicationService plan/apply -> Core transaction`，测试未直接调用 Core 写入 API 或修改目标文件。

## RED / GREEN

- RED：先加入 linked fixture 与集成测试，执行 integration filter。生产代码尚无 composition，两个调用点均得到预期 `CS0103: SimpleAppComposition` 不存在；测试未开始执行，因此 RED 阶段没有目标文件写入。
- GREEN：新增共享 composition 并让 Program 使用后，integration filter 通过 2/2。
- linked fixture、SQLite schema 与 Application/Core 数据契约未出现编译或运行问题，没有为测试调整 fixture 或扩大生产接口。

## 集成断言

`Controller_SwitchesConfigRolloutAndSqliteToConfiguredProvider` 从 root `model_provider = "openai"` 切换到已配置的 `apigather`，验证：

- controller 最终为 `SimpleActivity.Success`；
- `config.toml` section 之前的 root `model_provider` 精确为 `apigather`；
- rollout 首行经 `JsonDocument` 解析为 `session_meta`，其 `payload.model_provider` 为 `apigather`；
- SQLite 通过参数化查询验证 `threads.id = thread-a` 的 `model_provider` 为 `apigather`；
- `fixture.BackupRoot()` 存在且至少包含一个受管备份目录，controller 返回的备份目录也经相对路径检查确认位于该 root 内。

`Controller_SameProviderSync_PreservesExactConfigBytes` 选择当前 `openai` 执行 sync，验证：

- `config.toml` 前后 `byte[]` 完全相同；
- rollout 与 SQLite 均从 `apigather` 同步为 `openai`；
- 同样创建了受管备份目录。

## 隔离与真实 profile 边界

- fixture 的实际路径契约为 `%TEMP%\codex-provider-sync-<guid>\.codex`。每个测试运行时断言 `fixture.Root` 位于 `Path.GetTempPath()` 内，且 `CodexHome`、`BackupRoot()` 均位于 `fixture.Root` 内。
- composition 只接收并向下传递显式 `fixture.CodexHome`；集成测试不调用 `NormalizeCodexHome(null)`，不解析或读取 `%USERPROFILE%\.codex`。
- linked `TestEnvironment` 在测试程序集初始化时清除 `CODEX_SQLITE_HOME`，避免环境覆盖将 SQLite 引向 fixture 外部。
- 所有 config、rollout、SQLite、backup 断言路径都由 fixture 派生；SQLite 连接使用 fixture 自带的 `Pooling=False` 连接。每个测试在 `finally` 中删除其唯一临时 root。
- 因约束禁止访问真实 profile，本任务没有通过读取真实 `.codex` 前后快照来证明未修改；可执行的显式路径注入与包含关系断言是未触达真实 profile 的边界证据。

## 事务与 fault-injection 关联

- Task 6 的成功路径确认了真实 switch/sync 会经过 Core transaction 并生成受管 backup。
- Task 5 遗留的 Applying/Committed/RolledBack 风险已有 Core 隔离测试覆盖，因此未重复创建复杂 fixture 或新增生产 hook：
  - `CoreIntegrationTests.SqliteCommitAcknowledgementFailure_RestoresConfigRolloutAndDatabase` 注入 `after_sqlite_commit_before_ack` 故障，断言 journal 中 SQLite target 保持可恢复的 `applying` 证据、最终 transaction 为 `rolledBack`，config/rollout/SQLite 全部恢复且无 pending transaction。
  - `CoreIntegrationTests.CancellationAfterTransactionCommit_DoesNotRollBackCommittedState` 在 `after_transaction_commit` 取消，断言已提交状态保留且无 pending transaction。
  - `TransactionJournalTests.CommittedAppend_ApiFailureAfterDurableWriteReconcilesWithoutRollback` 与 `CommittedJournal_RejectsRollbackWithoutAppendingOrLosingTerminalState` 进一步覆盖 durable committed 尾部的协调与禁止回滚。
- 本次解决方案全量实际执行了上述 Core 测试；Core 188 通过、1 项既有 WSL 环境测试按条件跳过。

## 三组验证

使用 `C:\Users\99675\Documents\Codex\2026-08-11\s-j-ho-i\work\.dotnet\dotnet.exe`（目录置于 `PATH` 首位）：

```powershell
dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj `
  -c Release --filter FullyQualifiedName~SimpleSwitcherIntegrationTests
```

结果：失败 0，通过 2，跳过 0，总计 2。

```powershell
dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj -c Release
```

结果：失败 0，通过 78，跳过 0，总计 78。

```powershell
dotnet test CodexProviderSync.sln -c Release --no-build
```

结果：退出码 0。SimpleApp 78、GuiE2E 36、Application 49、Automation 27、App 66、Core 188 项通过，合计通过 444；Core 中 1 项既有 Windows WSL SQLite Home 安全测试按条件跳过，总计 445。

## 遗留风险

- 新集成测试覆盖本地 Windows 文件系统上的成功 switch/sync 和同 Provider sync；未在 SimpleApp 层重复注入进程崩溃、I/O 失败、文件锁或 rollback 失败。这些事务故障由现有 Core fault-injection 测试覆盖，但 UI 到故障结果的跨层组合仍主要由 controller/service 单元测试证明。
- 真实 WSL UNC SQLite Home 不属于该隔离集成 fixture；解决方案中对应 1 项测试按环境条件跳过。
- 测试进程异常终止时 `finally` 可能无法清理临时 root；路径仍被限制在 `%TEMP%\codex-provider-sync-<guid>`，不会转向真实 profile。
