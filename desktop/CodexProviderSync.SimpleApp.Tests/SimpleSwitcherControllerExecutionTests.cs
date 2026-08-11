using CodexProviderSync.Application;
using CodexProviderSync.Core;
using CodexProviderSync.SimpleApp;
using static CodexProviderSync.SimpleApp.Tests.SimpleSwitcherTestData;

namespace CodexProviderSync.SimpleApp.Tests;

public sealed class SimpleSwitcherControllerExecutionTests
{
    [Fact]
    public async Task ExecuteAsync_UsesSyncIntentForTheCurrentProvider()
    {
        RecordingSimpleProviderService service = ServiceWithReadyStatus("openai", "custom");
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
        RecordingSimpleProviderService service = ServiceWithReadyStatus("openai", "custom");
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
        RecordingSimpleProviderService service = ServiceWithReadyStatus("openai", "custom");
        FakeProcessProbe processes = new([new CodexProcessInfo("codex", 1234)]);
        SimpleSwitcherController controller = Controller(service, processes);
        await controller.RefreshAsync();
        controller.SelectProvider("custom");

        await controller.ExecuteAsync();

        Assert.Empty(service.ExecutedIntents);
        Assert.Equal(SimpleActivity.Blocked, controller.Snapshot.Activity);
        Assert.Contains("codex (PID 1234)", controller.Snapshot.Details);
        Assert.Contains("手动关闭", controller.Snapshot.Message);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsASecondClickWhileTheFirstIsRunning()
    {
        TaskCompletionSource<SyncResult> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingSimpleProviderService service = new(Status("openai", ["openai"]), _ => pending.Task);
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
        RecordingSimpleProviderService service = new(Status("openai", ["openai", "custom"]), _ => Task.FromResult(SuccessResult(
            "custom", skippedLocked: [@"C:\fixture\active.jsonl"], skippedUnreadable: [])));
        SimpleSwitcherController controller = await ReadyCustomController(service);

        await controller.ExecuteAsync();

        Assert.Equal(SimpleActivity.Incomplete, controller.Snapshot.Activity);
        Assert.Equal(1, controller.Snapshot.LastResult!.SkippedRolloutFiles);
        Assert.DoesNotContain("现在可以重新打开 Codex", controller.Snapshot.Message);
        Assert.Contains("再次同步", controller.Snapshot.Details);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsBoundBackupWhenRecoveryIsRequired()
    {
        RecordingSimpleProviderService service = new(Status("openai", ["openai", "custom"]), _ => throw new SimpleApplicationException(
            ApplicationOperationLifecycle.RecoveryRequired,
            [new ApplicationError("rollback_failed", "restore required", RecoveryRequired: true, RollbackStatus: "failed", EvidencePath: @"C:\fixture\bound-backup")]));
        SimpleSwitcherController controller = await ReadyCustomController(service);

        await controller.ExecuteAsync();

        Assert.Equal(SimpleActivity.RecoveryRequired, controller.Snapshot.Activity);
        Assert.Contains(@"C:\fixture\bound-backup", controller.Snapshot.Details);
        Assert.False(controller.Snapshot.CanExecute);
    }

    [Fact]
    public async Task ExecuteAsync_MapsTargetBusyToManualCloseBlock()
    {
        RecordingSimpleProviderService service = new(Status("openai", ["openai", "custom"]), _ => throw new SimpleApplicationException(
            ApplicationOperationLifecycle.Rejected,
            [new ApplicationError("target_busy", "state_5.sqlite is in use")]));
        SimpleSwitcherController controller = await ReadyCustomController(service);

        await controller.ExecuteAsync();

        Assert.Equal(SimpleActivity.Blocked, controller.Snapshot.Activity);
        Assert.Contains("手动关闭 Codex", controller.Snapshot.Message);
        Assert.Contains("state_5.sqlite is in use", controller.Snapshot.Details);
        Assert.True(controller.Snapshot.CanRefresh);
    }

    [Fact]
    public async Task ExecuteAsync_EarlyReturnDuringPendingRefreshDoesNotCompleteTheRefresh()
    {
        TaskCompletionSource<StatusSnapshot> pendingStatus = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingSimpleProviderService service = new(_ => pendingStatus.Task, _ => Task.FromResult(SuccessResult("openai")));
        SimpleSwitcherController controller = Controller(service, new FakeProcessProbe());

        Task refresh = controller.RefreshAsync();
        await service.StatusStarted.Task;
        await controller.ExecuteAsync();

        pendingStatus.SetResult(Status("openai", ["openai", "custom"]));
        await refresh;

        Assert.Equal(SimpleActivity.Ready, controller.Snapshot.Activity);
        Assert.True(controller.Snapshot.CanRefresh);
        Assert.True(controller.Snapshot.CanExecute);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsRefreshAndSelectionWhileWriteIsPending()
    {
        TaskCompletionSource<SyncResult> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingSimpleProviderService service = new(Status("openai", ["openai", "custom"]), _ => pending.Task);
        SimpleSwitcherController controller = await ReadyCustomController(service);

        Task execution = controller.ExecuteAsync();
        await service.WriteStarted.Task;
        await controller.RefreshAsync();

        Assert.False(controller.SelectProvider("openai"));
        Assert.Equal(SimpleActivity.Executing, controller.Snapshot.Activity);
        Assert.Equal("custom", controller.Snapshot.SelectedProviderId);
        pending.SetResult(SuccessResult("custom"));
        await execution;

        Assert.Equal(SimpleActivity.Success, controller.Snapshot.Activity);
        Assert.Equal("custom", controller.Snapshot.SelectedProviderId);
        Assert.True(Assert.Single(controller.Snapshot.Providers, item => item.Id == "custom").IsCurrent);
        Assert.Contains("现在可以重新打开 Codex", controller.Snapshot.Message);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotReadStatusOrWriteWhenProcessBlocks()
    {
        RecordingSimpleProviderService service = ServiceWithReadyStatus("openai", "custom");
        SimpleSwitcherController controller = Controller(service, new FakeProcessProbe([new CodexProcessInfo("codex", 1)]));
        await controller.RefreshAsync();

        await controller.ExecuteAsync();

        Assert.Equal(1, service.StatusCalls);
        Assert.Empty(service.ExecutedIntents);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsUnreadableRolloutsAsIncomplete()
    {
        RecordingSimpleProviderService service = new(Status("openai", ["openai", "custom"]), _ => Task.FromResult(SuccessResult(
            "custom", skippedUnreadable: [@"C:\fixture\bad.jsonl"])));
        SimpleSwitcherController controller = await ReadyCustomController(service);

        await controller.ExecuteAsync();

        Assert.Equal(SimpleActivity.Incomplete, controller.Snapshot.Activity);
        Assert.Equal(1, controller.Snapshot.LastResult!.SkippedRolloutFiles);
    }

    [Fact]
    public async Task ExecuteAsync_FailsWithoutWritingWhenStatusRemovesSelection()
    {
        int statusCall = 0;
        RecordingSimpleProviderService service = new(_ => Task.FromResult(++statusCall == 1
            ? Status("openai", ["openai", "custom"])
            : Status("openai", ["openai"])), _ => Task.FromResult(SuccessResult("custom")));
        SimpleSwitcherController controller = await ReadyCustomController(service);

        await controller.ExecuteAsync();

        Assert.Equal(SimpleActivity.Failed, controller.Snapshot.Activity);
        Assert.Empty(service.ExecutedIntents);
    }

    [Fact]
    public async Task ExecuteAsync_MapsPendingAndUnsupportedStatusBeforeWriting()
    {
        int statusCall = 0;
        RecordingSimpleProviderService recoveryService = new(_ => Task.FromResult(++statusCall == 1
            ? Status("openai", ["openai", "custom"])
            : Status("openai", ["openai", "custom"], pendingTransactions: [new TransactionRecoveryInfo("id", "pending", @"C:\backup", @"C:\journal")])), _ => Task.FromResult(SuccessResult("custom")));
        SimpleSwitcherController recovery = await ReadyCustomController(recoveryService);
        await recovery.ExecuteAsync();
        Assert.Equal(SimpleActivity.RecoveryRequired, recovery.Snapshot.Activity);
        Assert.Empty(recoveryService.ExecutedIntents);

        statusCall = 0;
        RecordingSimpleProviderService unsupportedService = new(_ => Task.FromResult(++statusCall == 1
            ? Status("openai", ["openai", "custom"])
            : Status("openai", ["openai", "custom"], sqliteSupported: false)), _ => Task.FromResult(SuccessResult("custom")));
        SimpleSwitcherController unsupported = await ReadyCustomController(unsupportedService);
        await unsupported.ExecuteAsync();
        Assert.Equal(SimpleActivity.Blocked, unsupported.Snapshot.Activity);
        Assert.Empty(unsupportedService.ExecutedIntents);
    }

    [Fact]
    public async Task ExecuteAsync_MapsGenericFailureWithoutLosingSelection()
    {
        RecordingSimpleProviderService service = new(Status("openai", ["openai", "custom"]), _ => throw new InvalidOperationException("broken"));
        SimpleSwitcherController controller = await ReadyCustomController(service);

        await controller.ExecuteAsync();

        Assert.Equal(SimpleActivity.Failed, controller.Snapshot.Activity);
        Assert.Equal("custom", controller.Snapshot.SelectedProviderId);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesServiceCancellationAndRestoresReadySnapshot()
    {
        RecordingSimpleProviderService service = new(Status("openai", ["openai", "custom"]), _ => Task.FromCanceled<SyncResult>(new CancellationToken(true)));
        SimpleSwitcherController controller = await ReadyCustomController(service);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.ExecuteAsync());

        Assert.Equal(SimpleActivity.Ready, controller.Snapshot.Activity);
        Assert.Equal("custom", controller.Snapshot.SelectedProviderId);
        Assert.True(controller.Snapshot.CanExecute);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesApplicationCancellationAndRestoresReadySnapshot()
    {
        RecordingSimpleProviderService service = new(Status("openai", ["openai", "custom"]), _ => throw new SimpleApplicationException(ApplicationOperationLifecycle.Cancelled, []));
        SimpleSwitcherController controller = await ReadyCustomController(service);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.ExecuteAsync());

        Assert.Equal(SimpleActivity.Ready, controller.Snapshot.Activity);
        Assert.Equal("custom", controller.Snapshot.SelectedProviderId);
        Assert.True(controller.Snapshot.CanExecute);
    }

    private static RecordingSimpleProviderService ServiceWithReadyStatus(string current, string other) =>
        new(Status(current, [current, other]), intent => Task.FromResult(SuccessResult(intent is SyncIntent sync ? sync.ProviderId : ((SwitchIntent)intent).ProviderId)));

    private static SimpleSwitcherController Controller(RecordingSimpleProviderService service, ICodexProcessProbe processes) =>
        new(service, processes, @"C:\fixture\.codex");

    private static async Task<SimpleSwitcherController> ReadyCustomController(RecordingSimpleProviderService service)
    {
        SimpleSwitcherController controller = Controller(service, new FakeProcessProbe());
        await controller.RefreshAsync();
        controller.SelectProvider("custom");
        return controller;
    }

    private static SyncResult SuccessResult(string provider, IReadOnlyList<string>? skippedLocked = null, IReadOnlyList<string>? skippedUnreadable = null) => new()
    {
        CodexHome = @"C:\fixture\.codex", TargetProvider = provider, PreviousProvider = "openai", BackupDir = @"C:\fixture\backup",
        ChangedSessionFiles = 1, SkippedLockedRolloutFiles = skippedLocked ?? [], SkippedUnreadableRolloutFiles = skippedUnreadable ?? [],
        SqliteRowsUpdated = 2, SqlitePresent = true, RolloutCountsBefore = new ProviderCounts(), EncryptedContentCounts = new ProviderCounts()
    };

    private sealed class RecordingSimpleProviderService : ISimpleProviderService
    {
        private readonly Func<CancellationToken, Task<StatusSnapshot>> _getStatus;
        private readonly Func<ApplicationWriteIntent, Task<SyncResult>> _execute;

        internal RecordingSimpleProviderService(StatusSnapshot status, Func<ApplicationWriteIntent, Task<SyncResult>> execute)
            : this(_ => Task.FromResult(status), execute)
        {
        }

        internal RecordingSimpleProviderService(
            Func<CancellationToken, Task<StatusSnapshot>> getStatus,
            Func<ApplicationWriteIntent, Task<SyncResult>> execute)
        {
            _getStatus = getStatus;
            _execute = execute;
        }

        internal List<ApplicationWriteIntent> ExecutedIntents { get; } = [];
        internal TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource StatusStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int StatusCalls { get; private set; }

        public Task<StatusSnapshot> GetStatusAsync(string codexHome, CancellationToken cancellationToken = default)
        {
            StatusCalls++;
            StatusStarted.TrySetResult();
            return _getStatus(cancellationToken);
        }

        public Task<SyncResult> ExecuteAsync(ApplicationWriteIntent intent, CancellationToken cancellationToken = default)
        {
            ExecutedIntents.Add(intent);
            WriteStarted.TrySetResult();
            return _execute(intent);
        }
    }
}
