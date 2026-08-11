# Codex Provider Switcher Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Build a visible Windows x64 application named CodexProviderSwitcher.exe that only switches the root Provider and synchronizes session visibility metadata.

**Architecture:** Add a separate WinForms frontend that references the existing CodexProviderSync.Application and CodexProviderSync.Core projects. Keep all config, rollout, SQLite, lock, backup, transaction, rollback, and recovery behavior in the existing layers; the new frontend only filters configured Providers, detects known Codex processes, drives the existing plan/apply protocol, and renders a minimal UI.

**Tech Stack:** C# 14, .NET 10, WinForms, xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1, existing Microsoft.Data.Sqlite 10.0.5 and SQLitePCLRaw.bundle_e_sqlite3 2.1.12.

## Global Constraints

- Target platform is Windows x64 and target framework is net10.0-windows.
- The executable name is CodexProviderSwitcher.exe.
- The application uses the default %USERPROFILE%\.codex only; it does not expose Codex Home or SQLite Home editing.
- The application only switches root model_provider and synchronizes rollout and SQLite visibility metadata.
- It must not read or write auth.json, API keys, base_url, account state, message content, titles, timestamps, or encrypted_content.
- It must not create a watcher, monitor in the background, terminate a process, restart Codex, or delete a lock.
- It only offers Providers declared by config plus openai when StatusSnapshot.CurrentProvider is the implicit openai default.
- A same-Provider operation uses SyncIntent; a different-Provider operation uses SwitchIntent with FollowProviderModelSelection.
- Every write uses the existing Application plan/apply protocol and Core transaction, backup, lock, rollback, and recovery implementation.
- The automatic backup retention count remains AppConstants.DefaultBackupRetentionCount, currently 5.
- Busy UI state prevents concurrent refreshes and writes.
- Known process names are codex, codex-app-server, and app-server, compared without case sensitivity; the switcher process itself is excluded.
- A detected Codex process only blocks the operation and tells the user to close it manually.
- A write with skipped locked or unreadable rollout files is an incomplete success and tells the user to close the owner and retry.
- Existing full GUI, macOS GUI, CLI, Automation API, and watcher behavior must remain unchanged.
- Source changes stay on branch feat/simple-provider-switcher in the isolated worktree.

---

## Environment Bootstrap

The repository targets .NET 10, but the current machine only has the 10.0.10 runtime. Install a non-admin SDK into the task workspace so the system-wide runtime and PATH are not changed. Microsoft documents dotnet-install.ps1 as the supported non-admin/CI install mechanism.

- [ ] Download the official installer and install the latest GA SDK from the 10.0 channel:

~~~powershell
$sdkRoot = 'C:\Users\99675\Documents\Codex\2026-08-11\s-j-ho-i\work\.dotnet'
$installer = Join-Path $env:TEMP 'dotnet-install.ps1'
Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
powershell -NoProfile -ExecutionPolicy Bypass -File $installer `
  -Channel 10.0 `
  -Quality GA `
  -Architecture x64 `
  -InstallDir $sdkRoot `
  -NoPath
~~~

- [ ] Verify the local SDK and pin the command for all remaining steps:

~~~powershell
$dotnet = 'C:\Users\99675\Documents\Codex\2026-08-11\s-j-ho-i\work\.dotnet\dotnet.exe'
& $dotnet --list-sdks
~~~

Expected: at least one 10.0.x SDK is listed from the workspace-local .dotnet directory.

- [ ] Restore and verify the current .NET baseline before feature changes:

~~~powershell
& $dotnet restore CodexProviderSync.sln
& $dotnet test CodexProviderSync.sln -c Release --no-restore
~~~

Expected: restore succeeds and all existing .NET tests pass. If the baseline fails, stop feature work and report the failing project and test names.

---

### Task 1: Add the SimpleApp project and plan/apply service boundary

**Files:**
- Create: desktop/CodexProviderSync.SimpleApp/CodexProviderSync.SimpleApp.csproj
- Create: desktop/CodexProviderSync.SimpleApp/Properties/AssemblyInfo.cs
- Create: desktop/CodexProviderSync.SimpleApp/SimpleProviderService.cs
- Create: desktop/CodexProviderSync.SimpleApp.Tests/CodexProviderSync.SimpleApp.Tests.csproj
- Create: desktop/CodexProviderSync.SimpleApp.Tests/SimpleProviderServiceTests.cs
- Modify: CodexProviderSync.sln

**Interfaces:**
- Consumes: IApplicationService, ApplicationStatusRequest, CreateApplicationPlanRequest, SyncApplicationRequest, SwitchApplicationRequest, ApplicationApplyAuthorization, SyncIntent, SwitchIntent, and SyncResult.
- Produces: ISimpleProviderService.GetStatusAsync(string codexHome, CancellationToken) and ISimpleProviderService.ExecuteAsync(ApplicationWriteIntent intent, CancellationToken).
- Produces: SimpleApplicationException with Lifecycle, Errors, and RecoveryRequired properties for controller-level error mapping.

- [ ] **Step 1: Create the test project and write failing service tests**

Create the test project with the same xUnit package versions already used by the repository:

~~~xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\CodexProviderSync.SimpleApp\CodexProviderSync.SimpleApp.csproj" />
  </ItemGroup>
</Project>
~~~

Add tests covering status success, exact plan authorization, sync/switch routing, one bounded plan_stale retry, and recovery evidence propagation. The core test shape is:

~~~csharp
[Fact]
public async Task ExecuteAsync_AppliesTheExactPlanAndRoutesSwitchIntent()
{
    FakeApplicationService application = FakeApplicationService.SwitchSuccess();
    SimpleProviderService service = new(application);
    SwitchIntent intent = new(
        @"C:\fixture\.codex",
        null,
        "custom",
        new FollowProviderModelSelection(),
        AppConstants.DefaultBackupRetentionCount);

    SyncResult result = await service.ExecuteAsync(intent, CancellationToken.None);

    Assert.Equal("custom", result.TargetProvider);
    Assert.Single(application.CreatedPlans);
    SwitchApplicationRequest request = Assert.Single(application.SwitchRequests);
    Assert.True(request.Authorization!.Apply);
    Assert.Same(application.CreatedPlans[0], request.Authorization.Plan);
    Assert.Equal(application.CreatedPlans[0].Digest, request.Authorization.PlanDigest);
}

[Fact]
public async Task ExecuteAsync_RetriesPlanStaleExactlyOnce()
{
    FakeApplicationService application = FakeApplicationService.PlanStaleThenSyncSuccess();
    SimpleProviderService service = new(application);

    await service.ExecuteAsync(
        new SyncIntent(@"C:\fixture\.codex", null, "openai"),
        CancellationToken.None);

    Assert.Equal(2, application.CreatedPlans.Count);
    Assert.Equal(2, application.SyncRequests.Count);
}

[Fact]
public async Task ExecuteAsync_PreservesRecoveryEvidence()
{
    FakeApplicationService application = FakeApplicationService.RecoveryRequired(
        "rollback_failed",
        @"C:\fixture\.codex\backups_state\provider-sync\bound");
    SimpleProviderService service = new(application);

    SimpleApplicationException error = await Assert.ThrowsAsync<SimpleApplicationException>(
        () => service.ExecuteAsync(
            new SyncIntent(@"C:\fixture\.codex", null, "openai"),
            CancellationToken.None));

    Assert.True(error.RecoveryRequired);
    Assert.Contains(error.Errors, item => item.EvidencePath!.EndsWith("bound"));
}
~~~

The fake implements all IApplicationService methods. DescribeAsync, RestoreAsync, and PruneAsync throw NotSupportedException because this frontend never calls them; GetStatusAsync, CreatePlanAsync, SyncAsync, and SwitchAsync return queued deterministic outcomes.

- [ ] **Step 2: Run the focused test and verify the missing project/types failure**

~~~powershell
& $dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj `
  -c Release `
  --filter FullyQualifiedName~SimpleProviderServiceTests
~~~

Expected: FAIL because CodexProviderSync.SimpleApp.csproj and SimpleProviderService do not exist.

- [ ] **Step 3: Create the WinForms project and service implementation**

Create the application project:

~~~xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>CodexProviderSwitcher</AssemblyName>
    <Product>Codex Provider Switcher</Product>
    <Company>Dailin521</Company>
    <Version>0.4.0</Version>
    <AssemblyVersion>0.4.0.0</AssemblyVersion>
    <FileVersion>0.4.0.0</FileVersion>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CodexProviderSync.Application\CodexProviderSync.Application.csproj" />
    <ProjectReference Include="..\CodexProviderSync.Core\CodexProviderSync.Core.csproj" />
  </ItemGroup>
</Project>
~~~

Expose internals only to the test assembly:

~~~csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("CodexProviderSync.SimpleApp.Tests")]
~~~

Implement the boundary with these exact members:

~~~csharp
internal interface ISimpleProviderService
{
    Task<StatusSnapshot> GetStatusAsync(
        string codexHome,
        CancellationToken cancellationToken = default);

    Task<SyncResult> ExecuteAsync(
        ApplicationWriteIntent intent,
        CancellationToken cancellationToken = default);
}

internal sealed class SimpleApplicationException : InvalidOperationException
{
    internal SimpleApplicationException(
        ApplicationOperationLifecycle lifecycle,
        IReadOnlyList<ApplicationError> errors)
        : base(errors.Count == 0
            ? "Application operation ended as " + lifecycle + "."
            : string.Join(Environment.NewLine, errors.Select(item => item.Message)))
    {
        Lifecycle = lifecycle;
        Errors = errors;
    }

    internal ApplicationOperationLifecycle Lifecycle { get; }
    internal IReadOnlyList<ApplicationError> Errors { get; }
    internal bool RecoveryRequired =>
        Lifecycle == ApplicationOperationLifecycle.RecoveryRequired
        || Errors.Any(item => item.RecoveryRequired);
}
~~~

GetStatusAsync and the two outcome guards are exact and preserve structured errors:

~~~csharp
public async Task<StatusSnapshot> GetStatusAsync(
    string codexHome,
    CancellationToken cancellationToken = default)
{
    ApplicationOutcome<StatusSnapshot> outcome = await _application.GetStatusAsync(
        new ApplicationStatusRequest(codexHome),
        cancellationToken);
    if (outcome.Lifecycle == ApplicationOperationLifecycle.Succeeded
        && outcome.Data is not null)
    {
        return outcome.Data;
    }
    throw new SimpleApplicationException(outcome.Lifecycle, outcome.Errors);
}

private static ApplicationOperationPlan RequireReadyPlan(
    ApplicationOutcome<ApplicationOperationPlan> outcome)
{
    if (outcome.Lifecycle == ApplicationOperationLifecycle.ReadyToApply
        && outcome.Data is not null)
    {
        return outcome.Data;
    }
    throw new SimpleApplicationException(outcome.Lifecycle, outcome.Errors);
}

private static SyncResult RequireAppliedResult(
    ApplicationOutcome<ApplicationWriteResult<SyncResult>> outcome)
{
    if (outcome.Lifecycle == ApplicationOperationLifecycle.Succeeded
        && outcome.Data is { Applied: true, Result: not null })
    {
        return outcome.Data.Result;
    }
    throw new SimpleApplicationException(outcome.Lifecycle, outcome.Errors);
}
~~~

SimpleProviderService.ExecuteAsync must create and apply an exact plan, retry error code plan_stale once, route by runtime intent type, and reject dry-run or missing results:

~~~csharp
for (int attempt = 0; attempt < 2; attempt++)
{
    ApplicationOutcome<ApplicationOperationPlan> planned =
        await _application.CreatePlanAsync(
            new CreateApplicationPlanRequest(intent),
            cancellationToken);
    ApplicationOperationPlan plan = RequireReadyPlan(planned);
    ApplicationApplyAuthorization authorization = new(
        Apply: true,
        Plan: plan,
        PlanDigest: plan.Digest);
    ApplicationOutcome<ApplicationWriteResult<SyncResult>> applied = intent switch
    {
        SyncIntent sync => await _application.SyncAsync(
            new SyncApplicationRequest(sync, authorization),
            cancellationToken),
        SwitchIntent change => await _application.SwitchAsync(
            new SwitchApplicationRequest(change, authorization),
            cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(intent))
    };
    if (attempt == 0
        && applied.Errors.Any(item =>
            string.Equals(item.Code, "plan_stale", StringComparison.Ordinal)))
    {
        continue;
    }
    return RequireAppliedResult(applied);
}
throw new InvalidOperationException("The bounded plan retry was exhausted.");
~~~

- [ ] **Step 4: Add both projects to the solution and rerun tests**

~~~powershell
& $dotnet sln CodexProviderSync.sln add `
  desktop\CodexProviderSync.SimpleApp\CodexProviderSync.SimpleApp.csproj `
  desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj `
  --solution-folder desktop
& $dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj `
  -c Release `
  --filter FullyQualifiedName~SimpleProviderServiceTests
~~~

Expected: all SimpleProviderServiceTests pass.

- [ ] **Step 5: Commit the service boundary**

~~~powershell
git add CodexProviderSync.sln `
  desktop/CodexProviderSync.SimpleApp `
  desktop/CodexProviderSync.SimpleApp.Tests
git commit -m "feat: add simple provider service boundary"
~~~

---

### Task 2: Add read-only Codex process detection

**Files:**
- Create: desktop/CodexProviderSync.SimpleApp/CodexProcessProbe.cs
- Create: desktop/CodexProviderSync.SimpleApp.Tests/CodexProcessProbeTests.cs

**Interfaces:**
- Produces: CodexProcessInfo(string Name, int ProcessId).
- Produces: ICodexProcessProbe.FindRunning() returning IReadOnlyList<CodexProcessInfo>.
- Consumes: an injected Func<IReadOnlyList<CodexProcessInfo>> in tests; production uses System.Diagnostics.Process.GetProcesses().

- [ ] **Step 1: Write failing process-filter tests**

~~~csharp
[Theory]
[InlineData("codex", true)]
[InlineData("Codex", true)]
[InlineData("CODEX-APP-SERVER", true)]
[InlineData("app-server", true)]
[InlineData("CodexProviderSwitcher", false)]
[InlineData("ChatGPT", false)]
[InlineData("powershell", false)]
public void IsKnownCodexProcess_UsesTheExactAllowlist(
    string processName,
    bool expected)
{
    Assert.Equal(expected, CodexProcessProbe.IsKnownCodexProcess(processName));
}

[Fact]
public void FindRunning_ExcludesTheSwitcherPidAndSortsResults()
{
    CodexProcessProbe probe = new(
        currentProcessId: 42,
        snapshot: () =>
        [
            new("app-server", 9),
            new("codex", 42),
            new("powershell", 3),
            new("codex", 7)
        ]);

    Assert.Equal(
        [new CodexProcessInfo("codex", 7), new CodexProcessInfo("app-server", 9)],
        probe.FindRunning());
}
~~~

- [ ] **Step 2: Run and verify the missing-type failure**

~~~powershell
& $dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj `
  -c Release `
  --filter FullyQualifiedName~CodexProcessProbeTests
~~~

Expected: FAIL because CodexProcessProbe does not exist.

- [ ] **Step 3: Implement the non-mutating probe**

~~~csharp
internal sealed record CodexProcessInfo(string Name, int ProcessId);

internal interface ICodexProcessProbe
{
    IReadOnlyList<CodexProcessInfo> FindRunning();
}

internal sealed class CodexProcessProbe : ICodexProcessProbe
{
    private static readonly HashSet<string> KnownNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "codex",
            "codex-app-server",
            "app-server"
        };

    internal static bool IsKnownCodexProcess(string processName) =>
        KnownNames.Contains(processName);
}
~~~

The production snapshot enumerates Process.GetProcesses(), captures ProcessName and Id inside try/catch for InvalidOperationException and System.ComponentModel.Win32Exception, disposes every Process, and never calls CloseMainWindow, Kill, WaitForExit, or any handle-writing API. FindRunning filters the exact allowlist, excludes Environment.ProcessId, removes duplicate PIDs, and sorts by ProcessId.

- [ ] **Step 4: Run the process tests**

~~~powershell
& $dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj `
  -c Release `
  --filter FullyQualifiedName~CodexProcessProbeTests
~~~

Expected: all CodexProcessProbeTests pass.

- [ ] **Step 5: Commit the process probe**

~~~powershell
git add desktop/CodexProviderSync.SimpleApp/CodexProcessProbe.cs `
  desktop/CodexProviderSync.SimpleApp.Tests/CodexProcessProbeTests.cs
git commit -m "feat: detect running Codex processes safely"
~~~

---

### Task 3: Add the minimal controller state, refresh, and Provider filtering

**Files:**
- Create: desktop/CodexProviderSync.SimpleApp/SimpleSwitcherModels.cs
- Create: desktop/CodexProviderSync.SimpleApp/SimpleSwitcherController.cs
- Create: desktop/CodexProviderSync.SimpleApp.Tests/SimpleSwitcherControllerRefreshTests.cs
- Create: desktop/CodexProviderSync.SimpleApp.Tests/SimpleTestDoubles.cs

**Interfaces:**
- Consumes: ISimpleProviderService.GetStatusAsync and ICodexProcessProbe.
- Produces: SimpleSwitcherController.Snapshot, SnapshotChanged, RefreshAsync, and SelectProvider.
- Produces: immutable SimpleSwitcherSnapshot, SimpleProviderItem, SimpleSyncSummary, and SimpleActivity.

- [ ] **Step 1: Write failing refresh and selection tests**

~~~csharp
[Fact]
public async Task RefreshAsync_OffersOnlyConfiguredProvidersAndImplicitCurrentOpenAi()
{
    FakeSimpleProviderService service = new(Status(
        current: "openai",
        configured: ["custom"],
        rolloutProviders: ["historical"],
        sqliteSupported: true));
    SimpleSwitcherController controller = new(
        service,
        new FakeProcessProbe(),
        @"C:\fixture\.codex");

    await controller.RefreshAsync("historical");

    Assert.Equal(["openai", "custom"], controller.Snapshot.Providers.Select(item => item.Id));
    Assert.Equal("openai", controller.Snapshot.SelectedProviderId);
    Assert.DoesNotContain(
        controller.Snapshot.Providers,
        item => item.Id == "historical");
    Assert.True(controller.Snapshot.CanExecute);
}

[Fact]
public async Task RefreshAsync_DisablesWritesForPendingRecovery()
{
    StatusSnapshot status = Status(
        current: "openai",
        configured: ["openai", "custom"],
        sqliteSupported: true,
        pendingTransactions:
        [
            new TransactionRecoveryInfo(
                "op-1",
                "recoveryRequired",
                @"C:\fixture\backup",
                @"C:\fixture\journal")
        ]);
    SimpleSwitcherController controller = Controller(status);

    await controller.RefreshAsync();

    Assert.False(controller.Snapshot.CanExecute);
    Assert.Equal(SimpleActivity.RecoveryRequired, controller.Snapshot.Activity);
    Assert.Contains(@"C:\fixture\backup", controller.Snapshot.Details);
}

[Fact]
public async Task RefreshAsync_DisablesWritesForUnsupportedSqlite()
{
    SimpleSwitcherController controller = Controller(Status(
        current: "openai",
        configured: ["openai"],
        sqliteSupported: false));

    await controller.RefreshAsync();

    Assert.False(controller.Snapshot.CanExecute);
    Assert.Contains("不支持", controller.Snapshot.Message);
}
~~~

SimpleTestDoubles.cs contains deterministic builders for every required StatusSnapshot property, plus FakeSimpleProviderService and FakeProcessProbe. It must not access the real user profile.

- [ ] **Step 2: Run and verify the missing-controller failure**

~~~powershell
& $dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj `
  -c Release `
  --filter FullyQualifiedName~SimpleSwitcherControllerRefreshTests
~~~

Expected: FAIL because the controller and models do not exist.

- [ ] **Step 3: Implement immutable presentation models**

~~~csharp
internal enum SimpleActivity
{
    Loading,
    Ready,
    Executing,
    Success,
    Incomplete,
    Blocked,
    Failed,
    RecoveryRequired
}

internal sealed record SimpleProviderItem(string Id, bool IsCurrent);

internal sealed record SimpleSyncSummary(
    string TargetProvider,
    int ChangedRolloutFiles,
    int SqliteRowsUpdated,
    int SkippedRolloutFiles,
    string BackupDirectory);

internal sealed record SimpleSwitcherSnapshot
{
    internal SimpleActivity Activity { get; init; } = SimpleActivity.Loading;
    internal string CodexHome { get; init; } = string.Empty;
    internal string? CurrentProviderId { get; init; }
    internal IReadOnlyList<SimpleProviderItem> Providers { get; init; } = [];
    internal string? SelectedProviderId { get; init; }
    internal string Message { get; init; } = "正在读取状态...";
    internal string Details { get; init; } = string.Empty;
    internal string? EncryptedContentWarning { get; init; }
    internal bool CanRefresh { get; init; }
    internal bool CanExecute { get; init; }
    internal SimpleSyncSummary? LastResult { get; init; }
}
~~~

- [ ] **Step 4: Implement refresh, filtering, and selection**

The controller uses a private lock for snapshot publication and Interlocked for active operations. Provider construction uses an ordinal set:

~~~csharp
HashSet<string> configured = new(
    status.ConfiguredProviders.Where(item => !string.IsNullOrWhiteSpace(item)),
    StringComparer.Ordinal);
if (status.CurrentProvider.Implicit
    && string.Equals(
        status.CurrentProvider.Provider,
        AppConstants.DefaultProvider,
        StringComparison.Ordinal))
{
    configured.Add(AppConstants.DefaultProvider);
}
IReadOnlyList<SimpleProviderItem> providers = configured
    .OrderByDescending(item =>
        string.Equals(item, status.CurrentProvider.Provider, StringComparison.Ordinal))
    .ThenBy(item => item, StringComparer.Ordinal)
    .Select(item => new SimpleProviderItem(
        item,
        string.Equals(item, status.CurrentProvider.Provider, StringComparison.Ordinal)))
    .ToArray();
~~~

Selection precedence is preferred Provider when it is still configured, then current Provider when it remains in the filtered list, then the first configured Provider. Refresh maps pending transactions to RecoveryRequired, unsupported SQLite to Blocked, and successful status to Ready. SelectProvider rejects values not in Snapshot.Providers and recalculates CanExecute.

- [ ] **Step 5: Run refresh tests**

~~~powershell
& $dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj `
  -c Release `
  --filter FullyQualifiedName~SimpleSwitcherControllerRefreshTests
~~~

Expected: all refresh and selection tests pass.

- [ ] **Step 6: Commit controller refresh behavior**

~~~powershell
git add desktop/CodexProviderSync.SimpleApp/SimpleSwitcherModels.cs `
  desktop/CodexProviderSync.SimpleApp/SimpleSwitcherController.cs `
  desktop/CodexProviderSync.SimpleApp.Tests/SimpleSwitcherControllerRefreshTests.cs `
  desktop/CodexProviderSync.SimpleApp.Tests/SimpleTestDoubles.cs
git commit -m "feat: add simple provider switcher state"
~~~

---

### Task 4: Implement switch/sync execution, blocking, and result mapping

**Files:**
- Modify: desktop/CodexProviderSync.SimpleApp/SimpleSwitcherController.cs
- Modify: desktop/CodexProviderSync.SimpleApp/SimpleSwitcherModels.cs
- Create: desktop/CodexProviderSync.SimpleApp.Tests/SimpleSwitcherControllerExecutionTests.cs

**Interfaces:**
- Consumes: ISimpleProviderService.ExecuteAsync and ICodexProcessProbe.FindRunning.
- Produces: SimpleSwitcherController.ExecuteAsync(CancellationToken).
- Produces: SyncIntent for same Provider and SwitchIntent with FollowProviderModelSelection for different Provider.

- [ ] **Step 1: Write failing intent-routing and process-block tests**

~~~csharp
[Fact]
public async Task ExecuteAsync_UsesSyncIntentForTheCurrentProvider()
{
    FakeSimpleProviderService service = ServiceWithReadyStatus("openai", "custom");
    SimpleSwitcherController controller = Controller(service, new FakeProcessProbe());
    await controller.RefreshAsync();
    controller.SelectProvider("openai");

    await controller.ExecuteAsync();

    SyncIntent intent = Assert.IsType<SyncIntent>(Assert.Single(service.ExecutedIntents));
    Assert.Equal("openai", intent.ProviderId);
    Assert.Equal(AppConstants.DefaultBackupRetentionCount, intent.BackupRetentionCount);
}

[Fact]
public async Task ExecuteAsync_UsesFollowProviderSwitchForADifferentProvider()
{
    FakeSimpleProviderService service = ServiceWithReadyStatus("openai", "custom");
    SimpleSwitcherController controller = Controller(service, new FakeProcessProbe());
    await controller.RefreshAsync();
    controller.SelectProvider("custom");

    await controller.ExecuteAsync();

    SwitchIntent intent = Assert.IsType<SwitchIntent>(Assert.Single(service.ExecutedIntents));
    Assert.IsType<FollowProviderModelSelection>(intent.ModelSelection);
    Assert.Equal(AppConstants.DefaultBackupRetentionCount, intent.BackupRetentionCount);
}

[Fact]
public async Task ExecuteAsync_BlocksWithoutWritingWhenCodexIsRunning()
{
    FakeSimpleProviderService service = ServiceWithReadyStatus("openai", "custom");
    FakeProcessProbe processes = new(new CodexProcessInfo("codex", 1234));
    SimpleSwitcherController controller = Controller(service, processes);
    await controller.RefreshAsync();
    controller.SelectProvider("custom");

    await controller.ExecuteAsync();

    Assert.Empty(service.ExecutedIntents);
    Assert.Equal(SimpleActivity.Blocked, controller.Snapshot.Activity);
    Assert.Contains("codex (PID 1234)", controller.Snapshot.Details);
    Assert.Contains("手动关闭", controller.Snapshot.Message);
}
~~~

- [ ] **Step 2: Write failing concurrency, incomplete-success, and recovery tests**

~~~csharp
[Fact]
public async Task ExecuteAsync_RejectsASecondClickWhileTheFirstIsRunning()
{
    TaskCompletionSource<SyncResult> pending =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    FakeSimpleProviderService service = ServiceWithPendingWrite(pending.Task);
    SimpleSwitcherController controller = Controller(service, new FakeProcessProbe());
    await controller.RefreshAsync();

    Task first = controller.ExecuteAsync();
    await service.WriteStarted.Task;
    await controller.ExecuteAsync();

    Assert.Single(service.ExecutedIntents);
    pending.SetResult(SuccessResult("openai"));
    await first;
}

[Fact]
public async Task ExecuteAsync_ReportsSkippedRolloutsAsIncomplete()
{
    FakeSimpleProviderService service = ServiceReturning(SuccessResult(
        "custom",
        skippedLocked: [@"C:\fixture\active.jsonl"],
        skippedUnreadable: []));
    SimpleSwitcherController controller = ReadyCustomController(service);

    await controller.ExecuteAsync();

    Assert.Equal(SimpleActivity.Incomplete, controller.Snapshot.Activity);
    Assert.Equal(1, controller.Snapshot.LastResult!.SkippedRolloutFiles);
    Assert.DoesNotContain("现在可以重新打开 Codex", controller.Snapshot.Message);
    Assert.Contains("再次同步", controller.Snapshot.Details);
}

[Fact]
public async Task ExecuteAsync_ReportsBoundBackupWhenRecoveryIsRequired()
{
    FakeSimpleProviderService service = ServiceThrowing(
        new SimpleApplicationException(
            ApplicationOperationLifecycle.RecoveryRequired,
            [
                new ApplicationError(
                    "rollback_failed",
                    "restore required",
                    RecoveryRequired: true,
                    RollbackStatus: "failed",
                    EvidencePath: @"C:\fixture\bound-backup")
            ]));
    SimpleSwitcherController controller = ReadyCustomController(service);

    await controller.ExecuteAsync();

    Assert.Equal(SimpleActivity.RecoveryRequired, controller.Snapshot.Activity);
    Assert.Contains(@"C:\fixture\bound-backup", controller.Snapshot.Details);
    Assert.False(controller.Snapshot.CanExecute);
}

[Fact]
public async Task ExecuteAsync_MapsTargetBusyToManualCloseBlock()
{
    FakeSimpleProviderService service = ServiceThrowing(
        new SimpleApplicationException(
            ApplicationOperationLifecycle.Rejected,
            [new ApplicationError("target_busy", "state_5.sqlite is in use")]));
    SimpleSwitcherController controller = ReadyCustomController(service);

    await controller.ExecuteAsync();

    Assert.Equal(SimpleActivity.Blocked, controller.Snapshot.Activity);
    Assert.Contains("手动关闭 Codex", controller.Snapshot.Message);
    Assert.Contains("state_5.sqlite is in use", controller.Snapshot.Details);
    Assert.True(controller.Snapshot.CanRefresh);
}
~~~

- [ ] **Step 3: Run and verify missing ExecuteAsync behavior**

~~~powershell
& $dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj `
  -c Release `
  --filter FullyQualifiedName~SimpleSwitcherControllerExecutionTests
~~~

Expected: FAIL because ExecuteAsync is absent or does not satisfy the routing and state rules.

- [ ] **Step 4: Implement bounded execution**

ExecuteAsync must:

1. Use Interlocked.CompareExchange to reject a concurrent invocation without throwing.
2. Require a Ready snapshot and configured selection.
3. call FindRunning before any refresh or plan.
4. publish Blocked and return when any process exists.
5. call a private ReadStatusAsync(selectedProvider, cancellationToken) helper to refresh status without re-entering the public operation guard; do not call public RefreshAsync from ExecuteAsync.
6. create SyncIntent for same Provider or SwitchIntent for a different Provider.
7. publish Executing and call ISimpleProviderService.ExecuteAsync.
8. map skipped locked plus unreadable rollout counts to Incomplete.
9. map full success to Success and include “现在可以重新打开 Codex”.
10. map SimpleApplicationException recovery evidence to RecoveryRequired.
11. map other exceptions to Failed without losing the selected Provider.
12. clear the Interlocked guard in finally.

The exact intent branch is:

~~~csharp
ApplicationWriteIntent intent = string.Equals(
    selectedProvider,
    refreshed.CurrentProviderId,
    StringComparison.Ordinal)
    ? new SyncIntent(
        _codexHome,
        null,
        selectedProvider,
        AppConstants.DefaultBackupRetentionCount)
    : new SwitchIntent(
        _codexHome,
        null,
        selectedProvider,
        new FollowProviderModelSelection(),
        AppConstants.DefaultBackupRetentionCount);
~~~

- [ ] **Step 5: Run all controller tests**

~~~powershell
& $dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj `
  -c Release `
  --filter FullyQualifiedName~SimpleSwitcherController
~~~

Expected: refresh, selection, routing, blocking, concurrency, result, and recovery tests all pass.

- [ ] **Step 6: Commit execution behavior**

~~~powershell
git add desktop/CodexProviderSync.SimpleApp/SimpleSwitcherController.cs `
  desktop/CodexProviderSync.SimpleApp/SimpleSwitcherModels.cs `
  desktop/CodexProviderSync.SimpleApp.Tests/SimpleSwitcherControllerExecutionTests.cs
git commit -m "feat: switch providers and sync sessions"
~~~

---

### Task 5: Build the visible WinForms window, settings, and startup

**Files:**
- Create: desktop/CodexProviderSync.SimpleApp/SimpleAppPaths.cs
- Create: desktop/CodexProviderSync.SimpleApp/SimpleSettingsStore.cs
- Create: desktop/CodexProviderSync.SimpleApp/SimpleInstanceGuard.cs
- Create: desktop/CodexProviderSync.SimpleApp/SimpleMainForm.cs
- Create: desktop/CodexProviderSync.SimpleApp/Program.cs
- Create: desktop/CodexProviderSync.SimpleApp.Tests/SimpleSettingsStoreTests.cs
- Create: desktop/CodexProviderSync.SimpleApp.Tests/SimpleInstanceGuardTests.cs
- Create: desktop/CodexProviderSync.SimpleApp.Tests/SimpleMainFormPresentationTests.cs

**Interfaces:**
- Consumes: SimpleSwitcherController and SimpleSwitcherSnapshot.
- Produces: a single visible form with current status, configured Provider combo box, “切换并同步”, “刷新”, result details, read-only log, and “复制结果”.
- Produces: SimpleUserSettings with LastProvider and WindowBoundsState, stored at %AppData%\codex-provider-switcher\settings.json.
- Produces: SimpleInstanceGuard using a Local\CodexProviderSwitcher.v1 mutex.

- [ ] **Step 1: Write failing settings and single-instance tests**

~~~csharp
[Fact]
public async Task Settings_RoundTripProviderAndWindowBounds()
{
    string path = Path.Combine(
        Path.GetTempPath(),
        "codex-switcher-settings-" + Guid.NewGuid().ToString("N"),
        "settings.json");
    SimpleSettingsStore store = new(path);
    SimpleUserSettings expected = new(
        "custom",
        new WindowBoundsState
        {
            X = 20,
            Y = 30,
            Width = 560,
            Height = 420,
            Maximized = false
        });

    await store.SaveAsync(expected);

    SimpleUserSettings actual = await store.LoadAsync();
    Assert.Equal("custom", actual.LastProvider);
    Assert.NotNull(actual.WindowBounds);
    Assert.Equal(20, actual.WindowBounds.X);
    Assert.Equal(30, actual.WindowBounds.Y);
    Assert.Equal(560, actual.WindowBounds.Width);
    Assert.Equal(420, actual.WindowBounds.Height);
    Assert.False(actual.WindowBounds.Maximized);
}

[Fact]
public void SecondInstanceGuardDoesNotOwnTheSameName()
{
    string name = "Local\\CodexProviderSwitcher.Tests." + Guid.NewGuid().ToString("N");
    using SimpleInstanceGuard first = new(name);
    using SimpleInstanceGuard second = new(name);

    Assert.True(first.IsOwner);
    Assert.False(second.IsOwner);
}
~~~

SimpleSettingsStore must return defaults for a missing or malformed file. Save creates the parent directory, writes JSON to a unique temporary file in that directory, and moves it over settings.json. Its cleanup removes only its own unique temporary file.

- [ ] **Step 2: Write failing form presentation tests**

~~~csharp
[Fact]
public void FormContainsOnlyTheApprovedActions()
{
    using SimpleMainForm form = CreateForm();

    Assert.Equal("Codex Provider Switcher", form.Text);
    Assert.Equal("切换并同步", Field<Button>(form, "_executeButton").Text);
    Assert.Equal("刷新", Field<Button>(form, "_refreshButton").Text);
    Assert.Equal("复制结果", Field<Button>(form, "_copyButton").Text);
    Assert.Equal(ComboBoxStyle.DropDownList, Field<ComboBox>(form, "_providerCombo").DropDownStyle);
    Assert.True(Field<RichTextBox>(form, "_detailsBox").ReadOnly);
}

[Fact]
public void FormDoesNotExposeOutOfScopeControls()
{
    using SimpleMainForm form = CreateForm();
    string[] forbidden =
    [
        "auth.json",
        "API Key",
        "base_url",
        "恢复备份",
        "清理旧备份",
        "检查更新",
        "监控"
    ];
    string allText = string.Join(
        Environment.NewLine,
        Descendants(form).Select(control => control.Text));

    Assert.All(forbidden, value => Assert.DoesNotContain(value, allText));
}

[Fact]
public void MinimumWindowSizeKeepsAllPrimaryControlsVisible()
{
    using SimpleMainForm form = CreateForm();
    form.Size = form.MinimumSize;
    PerformLayoutRecursively(form);

    Assert.True(Field<ComboBox>(form, "_providerCombo").Visible);
    Assert.True(Field<Button>(form, "_executeButton").Visible);
    Assert.True(Field<Button>(form, "_refreshButton").Visible);
    Assert.True(Field<RichTextBox>(form, "_detailsBox").Visible);
}
~~~

- [ ] **Step 3: Run and verify the missing-UI failure**

~~~powershell
& $dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj `
  -c Release `
  --filter "FullyQualifiedName~SimpleSettingsStoreTests|FullyQualifiedName~SimpleInstanceGuardTests|FullyQualifiedName~SimpleMainFormPresentationTests"
~~~

Expected: FAIL because settings, guard, and form types do not exist.

- [ ] **Step 4: Implement paths, settings, and single-instance behavior**

SimpleAppPaths resolves:

~~~csharp
internal sealed record SimpleAppPaths(
    string SettingsPath,
    string StartupErrorPath)
{
    internal static SimpleAppPaths SystemDefault()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "codex-provider-switcher");
        return new(
            Path.Combine(root, "settings.json"),
            Path.Combine(root, "startup-error.log"));
    }
}
~~~

Use these exact settings contracts:

~~~csharp
internal sealed record SimpleUserSettings(
    string? LastProvider,
    WindowBoundsState? WindowBounds)
{
    internal static SimpleUserSettings Default { get; } = new(null, null);
}

internal sealed class SimpleSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private readonly string _path;

    internal SimpleSettingsStore(string path)
    {
        _path = Path.GetFullPath(path);
    }

    internal async Task<SimpleUserSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return SimpleUserSettings.Default;
        }
        try
        {
            string json = await File.ReadAllTextAsync(_path, cancellationToken);
            return JsonSerializer.Deserialize<SimpleUserSettings>(json, JsonOptions)
                ?? SimpleUserSettings.Default;
        }
        catch (JsonException)
        {
            return SimpleUserSettings.Default;
        }
        catch (IOException)
        {
            return SimpleUserSettings.Default;
        }
    }

    internal async Task SaveAsync(
        SimpleUserSettings settings,
        CancellationToken cancellationToken = default)
    {
        string directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        string temp = Path.Combine(
            directory,
            "." + Path.GetFileName(_path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(temp, json, cancellationToken);
            File.Move(temp, _path, overwrite: true);
        }
        catch
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
            throw;
        }
    }
}
~~~

SimpleSettingsStore serializes with PropertyNamingPolicy = JsonNamingPolicy.CamelCase and WriteIndented = true. Load returns SimpleUserSettings.Default when the file is missing, malformed, or deserializes to null.

SimpleInstanceGuard owns a named Mutex only when createdNew is true. Dispose releases the mutex only for the owner, catches ApplicationException from abandoned ownership cleanup, and always disposes the Mutex. It never affects the Core provider-sync file lock.

- [ ] **Step 5: Implement the single-page form**

Use a TableLayoutPanel and these exact control fields so presentation tests remain stable:

~~~csharp
private readonly Label _currentProviderValue = new() { AutoSize = true };
private readonly Label _sqliteStatusValue = new() { AutoSize = true };
private readonly ComboBox _providerCombo = new()
{
    DropDownStyle = ComboBoxStyle.DropDownList,
    Dock = DockStyle.Fill
};
private readonly Button _executeButton = new()
{
    Text = "切换并同步",
    Height = 44,
    Dock = DockStyle.Fill
};
private readonly Button _refreshButton = new() { Text = "刷新", AutoSize = true };
private readonly Button _copyButton = new() { Text = "复制结果", AutoSize = true };
private readonly Label _stateLabel = new() { AutoSize = true };
private readonly RichTextBox _detailsBox = new()
{
    ReadOnly = true,
    Dock = DockStyle.Fill,
    DetectUrls = false
};
~~~

The title area says “只切换 Provider 并同步会话；不会修改账号、密钥或 Provider 地址。” The window starts at 560×420 with MinimumSize 520×380. The primary button uses the same green palette as the existing GUI. The form subscribes to SnapshotChanged, renders on the UI thread with BeginInvoke when required, and does not call Core or Application directly.

On Shown:

1. Load SimpleUserSettings.
2. restore window bounds only when they intersect a current Screen.WorkingArea.
3. call controller.RefreshAsync(settings.LastProvider).

On Provider selection, call SelectProvider. On Refresh, call RefreshAsync with the current selection. On Execute, call ExecuteAsync; show a warning MessageBox only for Blocked or RecoveryRequired. On Copy, place the current message and details on the clipboard. On FormClosing, save the selected Provider and non-minimized window bounds.

SimpleMainFormPresentationTests defines its helpers explicitly:

~~~csharp
private static SimpleMainForm CreateForm()
{
    FakeSimpleProviderService service = new(Status(
        current: "openai",
        configured: ["openai", "custom"],
        sqliteSupported: true));
    SimpleSwitcherController controller = new(
        service,
        new FakeProcessProbe(),
        @"C:\fixture\.codex");
    string settingsPath = Path.Combine(
        Path.GetTempPath(),
        "codex-switcher-form-" + Guid.NewGuid().ToString("N"),
        "settings.json");
    return new SimpleMainForm(controller, new SimpleSettingsStore(settingsPath));
}

private static T Field<T>(object target, string name) where T : class =>
    Assert.IsType<T>(target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(target));

private static IEnumerable<Control> Descendants(Control root)
{
    foreach (Control child in root.Controls)
    {
        yield return child;
        foreach (Control descendant in Descendants(child))
        {
            yield return descendant;
        }
    }
}

private static void PerformLayoutRecursively(Control root)
{
    root.PerformLayout();
    foreach (Control child in root.Controls)
    {
        PerformLayoutRecursively(child);
    }
}
~~~

- [ ] **Step 6: Implement Program composition and startup logging**

Program must use this composition:

~~~csharp
ApplicationConfiguration.Initialize();
SimpleAppPaths paths = SimpleAppPaths.SystemDefault();
using SimpleInstanceGuard instance = new("Local\\CodexProviderSwitcher.v1");
if (!instance.IsOwner)
{
    MessageBox.Show(
        "Codex Provider Switcher 已经在运行。",
        "Codex Provider Switcher",
        MessageBoxButtons.OK,
        MessageBoxIcon.Information);
    return;
}

CodexSyncService syncService = new();
IApplicationService application = new ApplicationService(
    new CoreApplicationStatusPort(syncService),
    new CoreApplicationWritePort(syncService, new CodexHomeService()),
    new InMemoryApplicationPlanLedger());
SimpleProviderService providerService = new(application);
SimpleSwitcherController controller = new(
    providerService,
    new CodexProcessProbe(),
    new CodexHomeService().NormalizeCodexHome(null));
SimpleSettingsStore settings = new(paths.SettingsPath);
Application.Run(new SimpleMainForm(controller, settings));
~~~

Wrap composition and Application.Run in try/catch. On failure, create only the parent of StartupErrorPath, write error.ToString(), and show the log path in a Chinese error dialog. Do not initialize or inspect auth.json.

- [ ] **Step 7: Run all SimpleApp unit and presentation tests**

~~~powershell
& $dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj `
  -c Release
~~~

Expected: all service, process, controller, settings, guard, and presentation tests pass.

- [ ] **Step 8: Commit the visible application**

~~~powershell
git add desktop/CodexProviderSync.SimpleApp `
  desktop/CodexProviderSync.SimpleApp.Tests
git commit -m "feat: add minimal provider switcher window"
~~~

---

### Task 6: Add a real isolated switch/sync integration test

**Files:**
- Create: desktop/CodexProviderSync.SimpleApp/SimpleAppComposition.cs
- Modify: desktop/CodexProviderSync.SimpleApp/Program.cs
- Modify: desktop/CodexProviderSync.SimpleApp.Tests/CodexProviderSync.SimpleApp.Tests.csproj
- Create: desktop/CodexProviderSync.SimpleApp.Tests/SimpleSwitcherIntegrationTests.cs
- Reuse as linked test sources: desktop/CodexProviderSync.Core.Tests/TestCodexHomeFixture.cs
- Reuse as linked test sources: desktop/CodexProviderSync.Core.Tests/TestEnvironment.cs

**Interfaces:**
- Consumes: real CodexSyncService, CoreApplicationStatusPort, CoreApplicationWritePort, ApplicationService, SimpleProviderService, and SimpleSwitcherController.
- Produces: evidence that the frontend path changes config, rollout, and SQLite through the existing transactional implementation.

- [ ] **Step 1: Link the existing isolated fixture into the SimpleApp test project**

~~~xml
<ItemGroup>
  <Compile Include="..\CodexProviderSync.Core.Tests\TestCodexHomeFixture.cs"
           Link="Fixtures\TestCodexHomeFixture.cs" />
  <Compile Include="..\CodexProviderSync.Core.Tests\TestEnvironment.cs"
           Link="Fixtures\TestEnvironment.cs" />
</ItemGroup>
~~~

Do not copy the fixture or modify its behavior.

- [ ] **Step 2: Write the failing real integration test**

~~~csharp
[Fact]
public async Task Controller_SwitchesConfigRolloutAndSqliteToConfiguredProvider()
{
    TestCodexHomeFixture fixture = await TestCodexHomeFixture.CreateAsync();
    try
    {
        await fixture.WriteConfigAsync("model_provider = \"openai\"");
        string rollout = fixture.RolloutPath("sessions", "rollout-a.jsonl");
        await fixture.WriteRolloutAsync(rollout, "thread-a", "openai");
        await fixture.WriteStateDbAsync(
            [("thread-a", "openai", false)],
            model: "gpt-5");

        SimpleSwitcherController controller = SimpleAppComposition.CreateController(
            fixture.CodexHome,
            new FakeProcessProbe());

        await controller.RefreshAsync();
        controller.SelectProvider("apigather");
        await controller.ExecuteAsync();

        Assert.Equal(SimpleActivity.Success, controller.Snapshot.Activity);
        string config = await File.ReadAllTextAsync(
            Path.Combine(fixture.CodexHome, "config.toml"));
        string rolloutText = await File.ReadAllTextAsync(rollout);
        Assert.Contains("model_provider = \"apigather\"", config);
        Assert.Contains("\"model_provider\":\"apigather\"", rolloutText);

        await using SqliteConnection db =
            new("Data Source=" + fixture.StateDbPath());
        await db.OpenAsync();
        await using SqliteCommand command = db.CreateCommand();
        command.CommandText =
            "SELECT model_provider FROM threads WHERE id = 'thread-a'";
        Assert.Equal("apigather", await command.ExecuteScalarAsync());
    }
    finally
    {
        Directory.Delete(fixture.Root, recursive: true);
    }
}
~~~

Add a second test that performs same-Provider sync and compares the exact config bytes before and after:

~~~csharp
byte[] before = await File.ReadAllBytesAsync(configPath);
controller.SelectProvider("openai");
await controller.ExecuteAsync();
Assert.Equal(before, await File.ReadAllBytesAsync(configPath));
~~~

- [ ] **Step 3: Run and verify the missing-composition failure**

~~~powershell
& $dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj `
  -c Release `
  --filter FullyQualifiedName~SimpleSwitcherIntegrationTests
~~~

Expected: FAIL because SimpleAppComposition does not exist; no real %USERPROFILE%\.codex file is touched.

- [ ] **Step 4: Implement the shared production composition and use it from Program**

Create the exact composition used by both Program and the integration test:

~~~csharp
internal static class SimpleAppComposition
{
    internal static SimpleSwitcherController CreateController(
        string codexHome,
        ICodexProcessProbe processProbe)
    {
        CodexSyncService syncService = new();
        IApplicationService application = new ApplicationService(
            new CoreApplicationStatusPort(syncService),
            new CoreApplicationWritePort(syncService, new CodexHomeService()),
            new InMemoryApplicationPlanLedger());
        return new SimpleSwitcherController(
            new SimpleProviderService(application),
            processProbe,
            codexHome);
    }
}
~~~

Replace Program's inline Core/Application construction with:

~~~csharp
string codexHome = new CodexHomeService().NormalizeCodexHome(null);
SimpleSwitcherController controller = SimpleAppComposition.CreateController(
    codexHome,
    new CodexProcessProbe());
~~~

Do not change Core transaction semantics. The final integration assertions must verify:

- config root model_provider is apigather after a switch;
- rollout session_meta model_provider is apigather;
- SQLite threads.model_provider is apigather;
- same-Provider sync preserves config bytes;
- a managed backup directory exists under fixture.BackupRoot().

- [ ] **Step 5: Run integration and all SimpleApp tests**

~~~powershell
& $dotnet test desktop\CodexProviderSync.SimpleApp.Tests\CodexProviderSync.SimpleApp.Tests.csproj `
  -c Release
~~~

Expected: all SimpleApp tests pass and the integration test reports no access outside the temporary fixture.

- [ ] **Step 6: Commit integration coverage**

~~~powershell
git add desktop/CodexProviderSync.SimpleApp `
  desktop/CodexProviderSync.SimpleApp.Tests
git commit -m "test: cover simple switcher end to end"
~~~

---

### Task 7: Add publishing, documentation, and release verification

**Files:**
- Create: scripts/publish-simple-gui.ps1
- Create: docs/README_SIMPLE_GUI_ZH.md
- Create: test/simple-gui-packaging-contract.test.js
- Modify: README.md

**Interfaces:**
- Consumes: CodexProviderSync.SimpleApp.csproj.
- Produces: artifacts/simple-win-x64/CodexProviderSwitcher.exe as a self-contained single-file win-x64 application.
- Produces: an operator guide that states the manual-close workflow and explicit non-goals.

- [ ] **Step 1: Write the failing packaging contract test**

~~~javascript
test("simple GUI publishes the dedicated self-contained executable", async () => {
  const script = await readFile(
    path.join(repoRoot, "scripts", "publish-simple-gui.ps1"),
    "utf8"
  );
  const project = await readFile(
    path.join(
      repoRoot,
      "desktop",
      "CodexProviderSync.SimpleApp",
      "CodexProviderSync.SimpleApp.csproj"
    ),
    "utf8"
  );

  assert.match(script, /CodexProviderSync\.SimpleApp/);
  assert.match(script, /PublishSingleFile=true/);
  assert.match(script, /--self-contained true/);
  assert.match(script, /win-x64/);
  assert.match(project, /<AssemblyName>CodexProviderSwitcher<\/AssemblyName>/);
});
~~~

- [ ] **Step 2: Run and verify the missing-script failure**

~~~powershell
& 'C:\Users\99675\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe' `
  --test test\simple-gui-packaging-contract.test.js
~~~

Expected: FAIL because scripts/publish-simple-gui.ps1 does not exist.

- [ ] **Step 3: Implement the guarded publish script**

The script parameters and defaults are:

~~~powershell
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Output = 'artifacts\simple-win-x64',
    [string]$DotnetPath = 'dotnet'
)
~~~

Resolve repoRoot, project, and outputDir to absolute paths. Refuse outputDir when it equals repoRoot or is outside repoRoot. Before recursive cleanup, print and validate the exact absolute outputDir. Publish with:

~~~powershell
& $DotnetPath publish $project `
  --runtime $Runtime `
  -c $Configuration `
  --self-contained true `
  -o $outputDir `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true `
  /p:EnableCompressionInSingleFile=true `
  /p:DebugType=None `
  /p:DebugSymbols=false
~~~

After publishing, require outputDir\CodexProviderSwitcher.exe to exist and print its absolute path.

- [ ] **Step 4: Write the Chinese usage guide and root README link**

README_SIMPLE_GUI_ZH.md must include:

- the executable only switches Provider and synchronizes sessions;
- close Codex manually before clicking;
- select only an already configured Provider;
- same Provider performs re-sync;
- success means it is safe to reopen Codex;
- incomplete success means close the file owner and retry;
- recovery-required errors must be handled with the full GUI or CLI and the displayed bound backup;
- the executable never manages auth.json, API keys, accounts, base_url, Provider definitions, process termination, process restart, or watcher behavior;
- settings path %AppData%\codex-provider-switcher\settings.json;
- startup error path %AppData%\codex-provider-switcher\startup-error.log.

Add one “精简切换器” link to README.md without changing the existing full GUI instructions.

- [ ] **Step 5: Run packaging and documentation tests**

~~~powershell
& 'C:\Users\99675\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe' `
  --test test\simple-gui-packaging-contract.test.js
& 'C:\Users\99675\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe' `
  --test
~~~

Expected: the focused packaging contract passes and the complete Node suite passes with at least the 192 baseline tests plus the new contract.

- [ ] **Step 6: Run the complete .NET regression suite**

~~~powershell
& $dotnet test CodexProviderSync.sln -c Release --no-restore
~~~

Expected: every existing and new .NET test passes with zero failures.

- [ ] **Step 7: Publish the single-file Windows executable**

~~~powershell
& .\scripts\publish-simple-gui.ps1 `
  -DotnetPath $dotnet `
  -Configuration Release `
  -Runtime win-x64 `
  -Output artifacts\simple-win-x64
Get-FileHash .\artifacts\simple-win-x64\CodexProviderSwitcher.exe -Algorithm SHA256
~~~

Expected: one self-contained CodexProviderSwitcher.exe exists and a SHA-256 is printed.

- [ ] **Step 8: Perform visible desktop smoke verification**

Launch the published EXE in a visible window:

~~~powershell
$process = Start-Process `
  -FilePath .\artifacts\simple-win-x64\CodexProviderSwitcher.exe `
  -PassThru
~~~

Verify on the interactive desktop:

1. title is Codex Provider Switcher;
2. scope text says only Provider switching and session sync;
3. current Provider is visible;
4. the combo contains configured Providers only;
5. the only actions are 切换并同步, 刷新, and 复制结果;
6. while Codex is still running, 切换并同步 shows the manual-close block and does not terminate Codex;
7. the window remains responsive and can close normally.

Do not execute a real switch against the user profile during smoke verification. The isolated integration test is the mutation proof.

- [ ] **Step 9: Copy the verified deliverable to the task output directory**

~~~powershell
$source = 'C:\Users\99675\Documents\Codex\2026-08-11\s-j-ho-i\work\codex-provider-sync-simple\artifacts\simple-win-x64\CodexProviderSwitcher.exe'
$destination = 'C:\Users\99675\Documents\Codex\2026-08-11\s-j-ho-i\outputs\CodexProviderSwitcher.exe'
Copy-Item -LiteralPath $source -Destination $destination -Force
Get-FileHash -LiteralPath $destination -Algorithm SHA256
~~~

Expected: the output copy exists and its SHA-256 matches the published source.

- [ ] **Step 10: Commit publishing and documentation**

~~~powershell
git add scripts/publish-simple-gui.ps1 `
  docs/README_SIMPLE_GUI_ZH.md `
  test/simple-gui-packaging-contract.test.js `
  README.md
git commit -m "build: publish simple provider switcher"
~~~

---

## Final Verification Checklist

- [ ] Run git status --short and confirm only expected ignored build artifacts remain.
- [ ] Run git diff main...HEAD --check and confirm no whitespace errors.
- [ ] Confirm the original repository still only has its pre-existing untracked pnpm-lock.yaml.
- [ ] Confirm the new application does not reference auth.json, API Key, base_url, Kill, CloseMainWindow, watcher, RestoreIntent, or PruneIntent:

~~~powershell
git grep -n -E 'auth\.json|API Key|base_url|\.Kill\(|CloseMainWindow|watcher|RestoreIntent|PruneIntent' `
  -- desktop/CodexProviderSync.SimpleApp
~~~

Expected: no matches.

- [ ] Confirm the final deliverable exists:

~~~powershell
Get-Item 'C:\Users\99675\Documents\Codex\2026-08-11\s-j-ho-i\outputs\CodexProviderSwitcher.exe' |
  Select-Object FullName, Length, LastWriteTime
~~~

- [ ] Record the .NET SDK version, Node version, test totals, publish command, executable SHA-256, visible smoke result, and any unverified risk in the final handoff.

## Reference

- Microsoft .NET Windows installation: https://learn.microsoft.com/en-us/dotnet/core/install/windows
- Microsoft dotnet-install script reference: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script
- Approved design: docs/superpowers/specs/2026-08-11-simple-provider-switcher-design.md
