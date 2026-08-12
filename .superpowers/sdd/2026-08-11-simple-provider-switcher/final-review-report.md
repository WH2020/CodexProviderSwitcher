# Final Review: Recovery Backup Evidence

## Scope

Review commit baseline `54b6fa53bc26adb2d99f4da59c4f1f762832b267` and the focused uncommitted recovery-evidence change.

## Finding And Resolution

`SyncTransactionException.BackupDirectory` must remain structured evidence when the application outcome is either recovery-required or a cancelled transaction whose rollback completed. `ApplicationService` now assigns that value to `ApplicationError.EvidencePath` in both `SyncTransactionException` catch paths. It does not add the directory to the message or change production interfaces.

The ordinary `OperationCanceledException` catch intentionally has no evidence path: that exception does not expose a backup directory.

## Tests

RED evidence before the production mapping: recovery-required output had a null `EvidencePath`.

GREEN evidence after the mapping:

- `RolledBackCancellationAndRecoveryFailureHaveDistinctStructuredOutcomes` asserts backup evidence for both the cancelled rollback-complete and recovery-required transaction cases.
- `ExecuteAsync_PreservesRecoveryEvidenceFromTheRealApplicationService` exercises `SimpleProviderService` through a real `ApplicationService`, `InMemoryApplicationPlanLedger`, and a minimal write port that throws `SyncTransactionException`; it verifies the `SimpleApplicationException` preserves the structured evidence.

Executed results:

- Application test: passed, 1/1, 261 ms.
- SimpleApp cross-layer test: passed, 1/1, 282 ms.

Only these two exact tests are run because the change is confined to exception-to-application-outcome mapping and the cross-layer propagation path. Broader project or solution suites are outside the requested validation scope.

## Final Safety Contract Fixes

### SQLite busy classification

- Core now exposes `SqliteBusyException`, preserving the existing user message and native `SqliteException` inner cause for SQLite codes 5 and 6.
- `CoreApplicationWritePort` maps that type to `ApplicationPortException("target_busy")` before the separate `LockService` marker catch.
- Simple switcher status blocks when `SqliteCounts.Unreadable` is true, preserves `ProviderCounts.Error` in details, and does not present SQLite as available.
- Deterministic cross-layer evidence originates from a native `SqliteException` code 5, passes through `SqliteStateService.WrapSqliteBusyError`, `CoreApplicationWritePort`, real `ApplicationService`, `SimpleProviderService`, and `SimpleSwitcherController`, and ends as `Blocked` with no execute-port call.

The attempted live `BEGIN IMMEDIATE` test and the repository's existing `RunSync_LeavesRolloutsUntouched_WhenSqliteIsLocked` test both caused this Windows test host to abort after roughly 20 seconds without producing an assertion result, even though the existing Core test requests a zero busy timeout. Per review direction, the unstable live-lock test was removed and was not included in final verification.

### Simple provider scope

- Existing `ListConfiguredProviderIds` and `ConfigDeclaresProvider` behavior remains unchanged, including built-in `openai`.
- `ListDeclaredProviderIds` and backward-compatible `StatusSnapshot.DeclaredProviders` carry only explicit `[model_providers.<id>]` sections.
- Simple switcher candidates use only declared providers, adding `openai` solely for an implicit current `openai` provider.
- Real isolated composition tests pass an explicit temporary Codex Home and verify explicit custom excludes `openai`, while implicit `openai` is included. No real profile is accessed.

### TDD and exact verification

RED evidence:

- Core busy/declared tests initially failed to compile because `SqliteBusyException` and `DeclaredProviders` did not exist.
- Unreadable/provider SimpleApp tests initially failed to compile because `DeclaredProviders` did not exist.
- With the Application SQLite busy catch temporarily removed, the deterministic controller test failed with `Expected: Blocked`, `Actual: Failed`.

GREEN commands and results (Release, repository-local .NET 10 SDK, `--no-restore`):

- `dotnet test desktop/CodexProviderSync.Core.Tests/CodexProviderSync.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~WrapSqliteBusyError_PreservesTypedCauseAndMessage|FullyQualifiedName~GetStatus_SeparatesDeclaredProvidersFromBuiltInConfiguredProviders' --no-restore`: 3/3 passed, 161 ms.
- `dotnet test desktop/CodexProviderSync.Application.Tests/CodexProviderSync.Application.Tests.csproj -c Release --filter 'FullyQualifiedName~MapsCoreSqliteBusyTypeToTargetBusyWithoutStringMatching' --no-restore`: 1/1 passed, 58 ms.
- `dotnet test desktop/CodexProviderSync.SimpleApp.Tests/CodexProviderSync.SimpleApp.Tests.csproj -c Release --filter 'FullyQualifiedName~RefreshAsync_BlocksWhenSqliteCountsAreUnreadable|FullyQualifiedName~Controller_Refresh_OffersOnlyExplicitlyDeclaredCustomProvider|FullyQualifiedName~Controller_Refresh_AddsImplicitOpenAiToDeclaredCustomProvider|FullyQualifiedName~Controller_Execute_MapsCoreSqliteBusyThroughApplicationToManualCloseBlock' --no-restore`: 4/4 passed, 239 ms.

No full project/solution suites, Node tests, publish, or smoke tests were run.
