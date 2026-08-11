# Task 1 execution report

## Implementation summary

Added the `CodexProviderSync.SimpleApp` WinForms executable project and its
test project. The internal `SimpleProviderService` is the SimpleApp boundary
for status reads and plan/apply writes. It builds an exact authorization from
the created plan, routes `SyncIntent` and `SwitchIntent` to the corresponding
Application service call, retries only one `plan_stale` result, and surfaces
the original lifecycle and structured errors through `SimpleApplicationException`.

## Modified files

- `CodexProviderSync.sln`
- `desktop/CodexProviderSync.SimpleApp/CodexProviderSync.SimpleApp.csproj`
- `desktop/CodexProviderSync.SimpleApp/Properties/AssemblyInfo.cs`
- `desktop/CodexProviderSync.SimpleApp/SimpleProviderService.cs`
- `desktop/CodexProviderSync.SimpleApp/Program.cs`
- `desktop/CodexProviderSync.SimpleApp.Tests/CodexProviderSync.SimpleApp.Tests.csproj`
- `desktop/CodexProviderSync.SimpleApp.Tests/SimpleProviderServiceTests.cs`

`Program.cs` is a deliberate, minimal deviation from the brief's listed file
set. The required `WinExe` output type otherwise fails with CS5001 because no
static `Main` exists. It contains only `[STAThread] private static void Main() { }`:
no UI, controller, process handling, background work, or file access. Task 5
is planned to replace this placeholder with the visual startup path.

## RED evidence

Command (with `C:\Users\99675\Documents\Codex\2026-08-11\s-j-ho-i\work\.dotnet`
prepended to `PATH`):

```powershell
& $dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj -c Release --filter FullyQualifiedName~SimpleProviderServiceTests
```

Result: failed with exit code 1, as intended before implementation. Restore
reported that `CodexProviderSync.SimpleApp.csproj` was missing; compilation
then reported missing `CodexProviderSync.Application` / `Core` namespaces and
the service contract types. This demonstrates the tests could not pass before
the SimpleApp project and boundary existed.

The first post-implementation build additionally exposed CS5001 (no suitable
static `Main`) from the required `WinExe` project configuration. The minimal
placeholder above addressed that project-build requirement only.

## GREEN evidence

Command (same PATH setup):

```powershell
& $dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj -c Release --filter FullyQualifiedName~SimpleProviderServiceTests
```

Result: passed, 5 passed / 0 failed / 0 skipped. Coverage includes successful
status forwarding, exact plan authorization for switch, sync routing, exactly
one stale-plan retry, and recovery evidence propagation.

## Full test result

Command (same PATH setup):

```powershell
& $dotnet test CodexProviderSync.sln -c Release --no-restore
```

Result: exit code 0. Passed: SimpleApp 5, GuiE2E 36, Application 49, App 66,
Automation 27, Core 188; one pre-existing platform-specific Core test was
skipped (`WindowsCore_DoesNotTouchRealWslSqliteHome`). No restore was required
for this full-suite command.

## Constraint check

- Only Task 1 project/service/test/solution work was added; no later UI,
  controller, watcher, process detection, restart, or lock-file behavior was
  implemented.
- The new service only calls `IApplicationService` status, plan, sync, and
  switch APIs; writes remain behind the existing Application plan/apply and
  Core boundaries.
- No code reads or writes `auth.json`, API keys, base URLs, account data,
  session messages, titles, timestamps, or encrypted content.

## Remaining items

The executable has an intentionally inert entry point until Task 5 supplies
the planned UI startup. No other known Task 1 issues remain.

## Review follow-up: intent boundary and stale retry limit

An independent review found that unsupported write intents reached
`CreatePlanAsync` before being rejected, and that the stale retry upper bound
was not explicitly tested. `ExecuteAsync` now rejects every intent other than
`SyncIntent` or `SwitchIntent` before the retry loop, so unsupported
`RestoreIntent`, `PruneIntent`, and future intent types cannot create a plan.

Two tests were added:

- `ExecuteAsync_RejectsUnsupportedIntentBeforeCreatingAPlan` verifies an
  unsupported `RestoreIntent` throws `ArgumentOutOfRangeException` without a
  created plan.
- `ExecuteAsync_StopsAfterTheSecondPlanStale` verifies two continuous
  `plan_stale` outcomes produce the second outcome's `Failed` lifecycle and
  structured error, with exactly two plan and apply calls.

### Follow-up RED evidence

The focused command was run after adding the tests:

```powershell
& $dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj -c Release --filter FullyQualifiedName~SimpleProviderServiceTests
```

It failed with exit code 1. The unsupported-intent test expected
`ArgumentOutOfRangeException`, but the actual exception was `InvalidOperationException:
Queue empty`, with the stack trace showing `SimpleProviderService.ExecuteAsync`
called `FakeApplicationService.CreatePlanAsync` first. This directly reproduced
the reviewed boundary defect. The first draft of the double-stale fixture used
non-`plan_stale` codes and was corrected before the final RED run; with the
actual `plan_stale` code it verified the existing bounded retry behavior.

### Follow-up GREEN evidence

The same focused command passed after adding the pre-loop type guard: 7 passed,
0 failed, 0 skipped.

### Follow-up full-suite evidence

```powershell
& $dotnet test CodexProviderSync.sln -c Release --no-restore
```

Result: exit code 0. Passed: SimpleApp 7, GuiE2E 36, Application 49, App 66,
Automation 27, Core 188; one platform-specific Core test was skipped. No
restore was performed for the full-suite command.
