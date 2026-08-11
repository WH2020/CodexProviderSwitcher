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
        private readonly StatusSnapshot _status;
        private readonly Func<ApplicationWriteIntent, Task<SyncResult>> _execute;

        internal RecordingSimpleProviderService(StatusSnapshot status, Func<ApplicationWriteIntent, Task<SyncResult>> execute)
        {
            _status = status;
            _execute = execute;
        }

        internal List<ApplicationWriteIntent> ExecutedIntents { get; } = [];
        internal TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<StatusSnapshot> GetStatusAsync(string codexHome, CancellationToken cancellationToken = default) => Task.FromResult(_status);

        public Task<SyncResult> ExecuteAsync(ApplicationWriteIntent intent, CancellationToken cancellationToken = default)
        {
            ExecutedIntents.Add(intent);
            WriteStarted.TrySetResult();
            return _execute(intent);
        }
    }
}
